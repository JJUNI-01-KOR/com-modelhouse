using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 옵션을 바꿀 수 있는 부분 하나 (FR-03, FR-29 / WBS 3.10 공통 적용기)
///
/// [재질 교체형] 벽지·바닥재·상판·소파·러그
///   - sourceMaterial에 에셋 재질(예: m_walls)을 넣으면,
///     renderers를 비워 둘 때 씬 전체에서 그 재질을 쓰는 칸을 자동으로 찾는다.
///     → 벽 OptionPart 하나로 "집 전체 벽"이 같이 바뀜 (FR-29-01)
///   - 같은 재질을 쓰는 가구까지 같이 바뀌면, 그 가구는 재질을 복제해서 나눠 줄 것 (WBS 2.06)
///
/// [모델 교체형] 그림 액자·화분
///   - modelSlot 아래의 자식을 지우고 옵션 prefab을 그 자리에 생성
///
/// 붙이는 곳: 빈 오브젝트 "Option_Wall" 등 (파트마다 1개), 또는 소품 자체
/// </summary>
public class OptionPart : MonoBehaviour, IClickable
{
    [Header("파트")]
    public PartType part;

    [Header("재질 교체형")]
    [Tooltip("에셋의 원본 재질 (m_walls, m_floor …). 이 재질을 쓰는 칸만 바꾼다.")]
    [SerializeField] private Material sourceMaterial;
    [Tooltip("비워 두면 씬 전체에서 sourceMaterial을 쓰는 렌더러를 자동으로 찾음")]
    [SerializeField] private Renderer[] renderers;

    [Header("모델 교체형")]
    [SerializeField] private Transform modelSlot;

    [Header("강조")]
    [SerializeField] private float highlightBoost = 1.4f;  // 커서가 올라가면 색을 이만큼 밝게

    /// <summary>클릭됐을 때. HomeEvents가 생기면 HomeEvents.OnPartClicked로 바꿀 자리.</summary>
    public static event Action<OptionPart> Clicked;

    // 렌더러의 "몇 번째 재질 칸"이 이 파트인지
    private struct Slot { public Renderer renderer; public int index; }
    private readonly List<Slot> slots = new List<Slot>();

    // 클릭된 렌더러(+재질 칸)로 OptionPart를 찾기 위한 표
    private static readonly Dictionary<(Renderer, int), OptionPart> bySlot = new Dictionary<(Renderer, int), OptionPart>();
    private static readonly Dictionary<Renderer, OptionPart> byRenderer = new Dictionary<Renderer, OptionPart>();

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // 예전 셰이더
    private MaterialPropertyBlock mpb;
    private bool isHighlighted;

    public OptionItem Current { get; private set; }

    // ------------------------------------------------------------
    private void Awake()
    {
        mpb = new MaterialPropertyBlock();
        CollectSlots();
    }

    private void OnDestroy()
    {
        Unregister();
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }

    private void CollectSlots()
    {
        Unregister();
        slots.Clear();

        IEnumerable<Renderer> targets;
        if (modelSlot != null)
            targets = modelSlot.GetComponentsInChildren<Renderer>();
        else if (renderers != null && renderers.Length > 0)
            targets = renderers;
        else if (sourceMaterial != null)
            targets = FindObjectsByType<Renderer>();
        else
            targets = GetComponentsInChildren<Renderer>();

        foreach (Renderer r in targets)
        {
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                // sourceMaterial이 정해져 있으면 그 재질을 쓰는 칸만
                if (modelSlot == null && sourceMaterial != null && mats[i] != sourceMaterial) continue;
                slots.Add(new Slot { renderer = r, index = i });
            }
        }

        foreach (Slot s in slots)
        {
            bySlot[(s.renderer, s.index)] = this;
            byRenderer.TryAdd(s.renderer, this);
        }

        if (slots.Count == 0)
            Debug.LogWarning($"[OptionPart] {name}: 바꿀 대상을 못 찾음 (sourceMaterial / renderers / modelSlot 확인)", this);
    }

    private void Unregister()
    {
        foreach (Slot s in slots)
        {
            if (bySlot.TryGetValue((s.renderer, s.index), out OptionPart p) && p == this)
                bySlot.Remove((s.renderer, s.index));
            if (byRenderer.TryGetValue(s.renderer, out p) && p == this)
                byRenderer.Remove(s.renderer);
        }
    }

    // ------------------------------------------------------------
    /// <summary>
    /// 옵션 적용 (FR-29). 재질이면 등록된 칸 전부, 모델이면 슬롯의 자식 교체.
    /// </summary>
    public void Apply(OptionItem item)
    {
        if (item == null) return;
        if (item.part != part)
        {
            Debug.LogWarning($"[OptionPart] {name}({part})에 다른 파트 옵션({item.part})을 적용하려 함", this);
            return;
        }

        bool wasHighlighted = isHighlighted;
        if (wasHighlighted) Highlight(false);

        if (item.prefab != null && modelSlot != null)
            SwapModel(item.prefab);
        else if (item.useColor)
            ApplyColor(item);
        else if (item.material != null)
            SwapMaterial(item.material);
        else
            Debug.LogWarning($"[OptionPart] {item.displayName}: material·color·prefab 중 아무것도 없음", this);

        Current = item;
        if (wasHighlighted) Highlight(true);
    }

    // ------------------------------------------------------------
    // 색상만 바꾸기 (벽지)
    // 에셋 원본 재질을 직접 고치면 에디터에서도 색이 바뀐 채로 남으므로,
    // Play 중에만 쓰는 복사본을 만들어 그 색을 바꾼다. Play를 끄면 복사본은 사라짐.
    private Material runtimeMaterial;
    private Color originalColor = Color.white;

    private void ApplyColor(OptionItem item)
    {
        if (runtimeMaterial == null)
        {
            Material src = sourceMaterial;
            if (src == null && slots.Count > 0) src = slots[0].renderer.sharedMaterials[slots[0].index];
            if (src == null) { Debug.LogWarning($"[OptionPart] {name}: 색을 바꿀 원본 재질이 없음", this); return; }

            runtimeMaterial = new Material(src) { name = src.name + " (Runtime)" };
            originalColor = GetColor(src);
            SwapMaterial(runtimeMaterial);
        }

        Color c = item.isDefault ? originalColor : item.color;
        if (runtimeMaterial.HasProperty(BaseColorId)) runtimeMaterial.SetColor(BaseColorId, c);
        if (runtimeMaterial.HasProperty(ColorId)) runtimeMaterial.SetColor(ColorId, c);
    }

    private static Color GetColor(Material m)
    {
        if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
        if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
        return Color.white;
    }

    private void SwapMaterial(Material mat)
    {
        // 렌더러별로 한 번만 배열을 복사해서 바꿈
        // (renderer.material을 쓰면 재질 복제본이 계속 생기므로 sharedMaterials 사용)
        var groups = new Dictionary<Renderer, Material[]>();
        foreach (Slot s in slots)
        {
            if (s.renderer == null) continue;
            if (!groups.TryGetValue(s.renderer, out Material[] mats))
            {
                mats = s.renderer.sharedMaterials;
                groups.Add(s.renderer, mats);
            }
            mats[s.index] = mat;
        }
        foreach (var g in groups)
            g.Key.sharedMaterials = g.Value;
    }

    private void SwapModel(GameObject prefab)
    {
        for (int i = modelSlot.childCount - 1; i >= 0; i--)
            Destroy(modelSlot.GetChild(i).gameObject);

        GameObject go = Instantiate(prefab, modelSlot);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        // 클릭되도록 레이어를 슬롯과 같게, 콜라이더가 없으면 붙여 줌
        SetLayerRecursive(go, modelSlot.gameObject.layer);
        if (go.GetComponentInChildren<Collider>() == null)
            foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>())
                mr.gameObject.AddComponent<BoxCollider>();

        // Destroy는 프레임 끝에 지워지므로, 새 모델만 대상으로 다시 수집
        Unregister();
        slots.Clear();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            for (int i = 0; i < r.sharedMaterials.Length; i++)
                slots.Add(new Slot { renderer = r, index = i });
        foreach (Slot s in slots)
        {
            bySlot[(s.renderer, s.index)] = this;
            byRenderer.TryAdd(s.renderer, this);
        }
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    // ------------------------------------------------------------
    // IClickable
    public void Highlight(bool on)
    {
        isHighlighted = on;
        foreach (Slot s in slots)
        {
            if (s.renderer == null) continue;
            mpb.Clear();
            if (on)
            {
                Material m = s.renderer.sharedMaterials[s.index];
                if (m != null)
                {
                    if (m.HasProperty(BaseColorId)) mpb.SetColor(BaseColorId, m.GetColor(BaseColorId) * highlightBoost);
                    else if (m.HasProperty(ColorId)) mpb.SetColor(ColorId, m.GetColor(ColorId) * highlightBoost);
                }
            }
            // 빈 블록을 넣으면 강조가 지워짐
            s.renderer.SetPropertyBlock(mpb, s.index);
        }
    }

    public void OnClick()
    {
        Debug.Log($"[OptionPart] 클릭: {part}");
        Clicked?.Invoke(this);
        // TODO(HomeEvents 합류 후): HomeEvents.OnPartClicked?.Invoke(this);
    }

    // ------------------------------------------------------------
    /// <summary>
    /// CursorPicker용: 맞은 렌더러와 재질 칸(subMesh)으로 파트 찾기.
    /// subMeshIndex를 모르면 -1 → 그 렌더러를 쓰는 아무 파트.
    /// </summary>
    public static OptionPart FindByRenderer(Renderer r, int subMeshIndex = -1)
    {
        if (r == null) return null;
        OptionPart p;
        // 칸을 정확히 알면 그 칸만 본다 (벽·바닥이 한 메쉬에 섞여 있어도 구분됨)
        if (subMeshIndex >= 0) return bySlot.TryGetValue((r, subMeshIndex), out p) ? p : null;
        return byRenderer.TryGetValue(r, out p) ? p : null;
    }
}
