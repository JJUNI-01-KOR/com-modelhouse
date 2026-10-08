using UnityEngine;

// 옵션 적용 대상 (FR-29). 클래스 다이어그램 OptionItem 페이지의 OptionPart.
// ※ WBS 3.10(권오민)에서 공통 적용기를 이미 만들었다면 그쪽 파일을 쓰고 이 파일은 지운다.
//   ConfigurationState는 part 필드와 Apply(OptionItem)만 쓴다.
// - 재질형(벽지·바닥재·주방 상판·소파·러그): renderers의 재질 슬롯 중 sourceMaterial을 쓰는 칸만 바꾼다.
// - 모델형(그림 액자·화분): modelSlot 아래 원래 모델을 숨기고 옵션 프리팹을 놓는다.
public class OptionPart : MonoBehaviour
{
    public PartType part;

    [Header("재질형")]
    [Tooltip("바꿀 렌더러. 비우면 자식의 렌더러 전부")]
    [SerializeField] Renderer[] renderers;
    [Tooltip("바꿀 원본 재질 (예: m_walls). 비우면 렌더러의 모든 재질 칸을 바꾼다")]
    [SerializeField] Material sourceMaterial;

    [Header("모델형")]
    [Tooltip("모델을 놓을 자리. 비우면 이 오브젝트. 처음 자식이 원래 모델")]
    [SerializeField] Transform modelSlot;

    Material[][] originalMaterials;
    GameObject originalModel;
    GameObject currentModel;

    void Awake()
    {
        if (part.IsMaterialPart())
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();
            originalMaterials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
                originalMaterials[i] = renderers[i].sharedMaterials;
        }
        else
        {
            if (modelSlot == null) modelSlot = transform;
            if (modelSlot.childCount > 0) originalModel = modelSlot.GetChild(0).gameObject;
        }
    }

    public void Apply(OptionItem item)
    {
        if (item == null || item.part != part) return;
        if (part.IsMaterialPart()) ApplyMaterial(item);
        else ApplyModel(item);
    }

    void ApplyMaterial(OptionItem item)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            Material[] mats = (Material[])originalMaterials[i].Clone();
            if (item.material != null)
            {
                for (int j = 0; j < mats.Length; j++)
                    if (sourceMaterial == null || originalMaterials[i][j] == sourceMaterial)
                        mats[j] = item.material;
            }
            renderers[i].sharedMaterials = mats;   // 에셋 원본 재질 파일은 바뀌지 않는다
        }
    }

    void ApplyModel(OptionItem item)
    {
        if (currentModel != null) Destroy(currentModel);
        currentModel = null;

        if (item.prefab == null)
        {
            if (originalModel != null) originalModel.SetActive(true);
            return;
        }

        if (originalModel != null) originalModel.SetActive(false);
        currentModel = Instantiate(item.prefab, modelSlot);
        currentModel.transform.localPosition = Vector3.zero;
        currentModel.transform.localRotation = Quaternion.identity;
    }

    // 커서 테두리 표시 (FR-03, WBS 3.08 권오민)
    public void Highlight(bool on) { }
}
