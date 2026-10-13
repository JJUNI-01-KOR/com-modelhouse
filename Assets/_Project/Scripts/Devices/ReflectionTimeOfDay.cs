using System.Collections.Generic;
using UnityEngine;

// 반사 밝기를 시각에 맞춘다 (10/13 강사 피드백, FR-30 · NFR-10)
// 반사 프로브(구역·거울)는 낮에 한 번 구워 둔 사진이라, 밤에도 거울·창·광택 바닥에 낮 반사가 그대로 비쳤다.
// → 시각이 바뀔 때마다(HomeEvents.OnHourChanged) 모든 반사 프로브 세기와 기본(하늘) 반사 세기를 부록 A-8 환경광 비율에 맞춰 줄인다.
//   실내 천장 조명이 있으므로 완전히 꺼지지는 않게 하한을 둔다.
// 창유리도 시각마다 불투명도를 바꿔 밤에 흰 판처럼 보이지 않게 한다 (낮 30%, 밤 12%).
// 씬에 붙일 필요 없음 — Play 하면 자동으로 하나 생긴다. 시간대 코드(TimeOfDayController)는 고치지 않는다.
public class ReflectionTimeOfDay : MonoBehaviour
{
    // 시각 → 반사 세기 비율 (A-8 환경광 30/70/100/30/5%를 따르되 실내 조명 몫 하한)
    static readonly Dictionary<int, float> Ratio = new Dictionary<int, float>
    {
        { 6, 0.45f }, { 9, 0.80f }, { 12, 1.00f }, { 18, 0.45f }, { 22, 0.10f },   // 22시: 천장 조명 추가 후 밤 창유리가 실내 반사로 벽처럼 보여 0.25 → 0.10
    };

    // 창유리 불투명도 (에셋 m_glass는 흰색 60% 불투명이라 밤에 천장 조명을 받으면 흰 벽처럼 보임)
    // 실행 중 복사본(TimeOfDayController가 만든 "m_glass (Runtime)")만 바꾼다. 원본 에셋은 그대로.
    static readonly Dictionary<int, float> GlassAlpha = new Dictionary<int, float>
    {
        { 6, 0.30f }, { 9, 0.30f }, { 12, 0.30f }, { 18, 0.30f }, { 22, 0.12f },
    };
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    readonly Dictionary<ReflectionProbe, float> baseIntensity = new Dictionary<ReflectionProbe, float>();
    readonly List<Material> glassMats = new List<Material>();
    float baseReflection = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindAnyObjectByType<ReflectionTimeOfDay>() != null) return;
        if (FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude).Length == 0) return;
        new GameObject("ReflectionTimeOfDay (자동)").AddComponent<ReflectionTimeOfDay>();
    }

    void Awake()
    {
        foreach (ReflectionProbe p in FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude))
        {
            if (p.GetComponentInParent<MirrorProbeHours>() != null) continue;   // 시각별로 찍어 둔 거울은 사진이 밝기를 담고 있음
            baseIntensity[p] = p.intensity;
        }
        baseReflection = RenderSettings.reflectionIntensity;
    }

    void OnEnable()  { HomeEvents.OnHourChanged += Apply; }
    void OnDisable() { HomeEvents.OnHourChanged -= Apply; }

    void Start()
    {
        // 창유리 실행 중 복사본 찾기 (TimeOfDayController.Awake에서 만들어짐 → Start 시점에는 있음)
        foreach (Renderer rd in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            foreach (Material m in rd.sharedMaterials)
                if (m != null && m.name.StartsWith("m_glass") && m.name.EndsWith("(Runtime)") && m.HasProperty(BaseColorId) && !glassMats.Contains(m))
                {
                    // 에셋은 SrcBlend=One(premultiply 키워드 없음)이라 알파를 낮춰도 유리 색이 줄지 않고 더해짐 → 일반 알파 블렌드로
                    if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    glassMats.Add(m);
                }

        // 시작 시각은 이벤트 없이 정해지므로 한 번 직접 맞춘다
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            if (mb is ITimeOfDay t) { if (t.CurrentHour >= 0) Apply(t.CurrentHour); return; }
    }

    void Apply(int hour)
    {
        float k = RatioFor(hour);
        foreach (var kv in baseIntensity)
            if (kv.Key != null) kv.Key.intensity = kv.Value * k;
        RenderSettings.reflectionIntensity = baseReflection * k;

        float a = Lookup(GlassAlpha, hour);
        foreach (Material m in glassMats)
        {
            Color c = m.GetColor(BaseColorId);
            c.a = a;
            m.SetColor(BaseColorId, c);
        }
    }

    static float RatioFor(int hour) => Lookup(Ratio, hour);

    static float Lookup(Dictionary<int, float> table, int hour)
    {
        if (table.TryGetValue(hour, out float v)) return v;
        int best = 12, diff = int.MaxValue;
        foreach (int h in table.Keys) { int d = Mathf.Abs(h - hour); if (d < diff) { diff = d; best = h; } }
        return table[best];
    }
}
