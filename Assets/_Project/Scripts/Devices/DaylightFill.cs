using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 낮 실내 채광 보강 (10/13 강사 피드백 "낮인데 거실·주방·침실이 어둡다", NFR-10)
// 집 벽·바닥 빛은 구워 둔 라이트맵이라 시각과 상관없이 같고, 햇빛은 창 근처 바닥에만 닿아서 낮에도 방 안쪽이 어둡다.
// 화면 노출만 올리면 하얗게 뜬 느낌이 나서 → 실제로 방 안에 빛을 더한다.
//   거실·주방·침실 구역(ZoneVolume 박스) 천장 아래에 그림자 없는 점광원을 격자로 깔고,
//   시각이 바뀔 때마다(HomeEvents.OnHourChanged) 낮에는 켜고 밤에는 끈다. 색은 그 시각 햇빛 색을 조금 섞은 흰색.
// 씬에 붙일 필요 없음 — Play 하면 자동으로 하나 생긴다. 값은 Play 중 Inspector에서 바로 바꿔 볼 수 있음.
public class DaylightFill : MonoBehaviour
{
    // 시각 → 채광 세기 비율 (12:00 = 1)
    static readonly Dictionary<int, float> Ratio = new Dictionary<int, float>
    {
        { 6, 0.35f }, { 9, 0.85f }, { 12, 1.0f }, { 18, 0.35f }, { 22, 0f },
    };
    // 시각 → 빛 색 (A-8 햇빛 색을 흰색에 조금 섞음)
    static readonly Dictionary<int, Color> Tint = new Dictionary<int, Color>
    {
        { 6, new Color(1f, 0.86f, 0.72f) }, { 9, new Color(1f, 0.97f, 0.93f) }, { 12, new Color(0.98f, 0.99f, 1f) },
        { 18, new Color(1f, 0.84f, 0.70f) }, { 22, Color.black },
    };
    static readonly string[] Zones = { "living", "kitchen", "bedroom" };

    [Tooltip("12:00 기준 점광원 하나 세기")]
    [SerializeField] float intensity = 1.6f;
    [Tooltip("점광원 사이 간격 (m)")]
    [SerializeField] float spacing = 3.2f;
    [Tooltip("천장에서 내려 다는 높이 (m)")]
    [SerializeField] float belowCeiling = 0.35f;

    readonly List<Light> lights = new List<Light>();
    readonly List<float> zoneFactor = new List<float>();
    int hour = 12;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindAnyObjectByType<DaylightFill>() != null) return;
        if (FindAnyObjectByType<ZoneVolume>() == null) return;
        new GameObject("DaylightFill (자동)").AddComponent<DaylightFill>();
    }

    void Awake()
    {
        foreach (ZoneVolume z in FindObjectsByType<ZoneVolume>(FindObjectsInactive.Exclude))
        {
            if (z.IsSensorOnly || z.Data == null || System.Array.IndexOf(Zones, z.Data.zoneId) < 0) continue;
            float factor = Mathf.Clamp(z.Data.luxFactor, 0.5f, 1f);   // 침실 0.6 → 조금 약하게
            foreach (BoxCollider b in z.GetComponentsInChildren<BoxCollider>(true))
                AddGrid(z.Data.displayName, b.bounds, factor);
        }
        Debug.Log($"[DaylightFill] 낮 채광 점광원 {lights.Count}개 (거실·주방·침실)");
    }

    void AddGrid(string zoneName, Bounds bb, float factor)
    {
        int nx = Mathf.Max(1, Mathf.RoundToInt(bb.size.x / spacing));
        int nz = Mathf.Max(1, Mathf.RoundToInt(bb.size.z / spacing));
        float y = Mathf.Min(bb.max.y, bb.min.y + 2.6f) - belowCeiling;
        float range = Mathf.Max(bb.size.x / nx, bb.size.z / nz) * 1.25f + 1.5f;
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
            {
                var go = new GameObject($"Fill_{zoneName}_{lights.Count}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(
                    bb.min.x + bb.size.x * (i + 0.5f) / nx, y,
                    bb.min.z + bb.size.z * (k + 0.5f) / nz);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;

                // [10/14 빌드 에러 수정] CS1061: 'Light'에 'lightmapBakeType'이 없음
                // Light.lightmapBakeType은 에디터 전용 속성이라 빌드(실행 파일)에서는 존재하지 않는다.
                // 에디터에서는 잘 되다가 빌드에서만 컴파일 에러가 나던 원인.
                // → 에디터에서만 이 줄이 컴파일되도록 #if UNITY_EDITOR로 감쌌다.
#if UNITY_EDITOR
                l.lightmapBakeType = LightmapBakeType.Realtime;
#endif
                // 빌드에서도 "구운 빛이 아닌 실시간 빛"이라고 확실히 알려 주는 부분.
                // bakingOutput은 실행 중에도 쓸 수 있는 속성이라 빌드에서도 동작한다.
                // isBaked = false → 라이트맵에 구워진 적 없음, Realtime → 매 프레임 실시간으로 비춤.
                // (런타임에 새로 만든 Light는 원래 실시간이지만, 에디터와 빌드 결과를 똑같이 맞추려고 명시)
                l.bakingOutput = new LightBakingOutput
                {
                    isBaked = false,
                    lightmapBakeType = LightmapBakeType.Realtime,
                };

                l.shadows = LightShadows.None;
                l.range = range;
                l.renderMode = LightRenderMode.Auto;
                l.cullingMask = ~0;
                l.enabled = false;
                lights.Add(l);
                zoneFactor.Add(factor);
            }
    }

    void OnEnable() { HomeEvents.OnHourChanged += Apply; }
    void OnDisable() { HomeEvents.OnHourChanged -= Apply; }

    void Start()
    {
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            if (mb is ITimeOfDay t) { Apply(t.CurrentHour >= 0 ? t.CurrentHour : 12); return; }
        Apply(12);
    }

    // Inspector에서 값 바꾸면 바로 반영 (Play 중)
    void OnValidate() { if (Application.isPlaying && lights.Count > 0) Apply(hour); }

    void Apply(int h)
    {
        hour = h;
        float k = Lookup(Ratio, h);
        Color c = Lookup(Tint, h);
        for (int i = 0; i < lights.Count; i++)
        {
            Light l = lights[i];
            if (l == null) continue;
            l.color = c;
            l.intensity = intensity * k * zoneFactor[i];
            l.enabled = k > 0f;
        }
    }

    static T Lookup<T>(Dictionary<int, T> table, int h)
    {
        if (table.TryGetValue(h, out T v)) return v;
        int best = 12, diff = int.MaxValue;
        foreach (int key in table.Keys) { int d = Mathf.Abs(key - h); if (d < diff) { diff = d; best = key; } }
        return table[best];
    }
}