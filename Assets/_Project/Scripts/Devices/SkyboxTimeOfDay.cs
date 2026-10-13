using System.Collections.Generic;
using UnityEngine;

// 시간대별 스카이박스 교체 (10/13 강사 피드백 3번, FR-30 하늘 배경 · 부록 A-8)
// 시각 버튼(HomeEvents.OnHourChanged)마다 Resources/Skybox/Sky_XX 하늘로 바꾸고,
// 그림 속 해(+Z 쪽에 그려 둠)를 실제 햇빛(Directional Light) 방향에 맞춰 돌린다.
//   06 해 뜸(주황) · 09 아침(맑음) · 12 낮(맑음) · 18 해 질 녘(노을) · 22 밤(별·달)
// 씬에 붙일 필요 없음 — Play 하면 자동으로 생긴다. TimeOfDayController는 고치지 않는다
// (그쪽이 Procedural 하늘 색을 바꿔도, 시각이 바뀐 직후 이 컴포넌트가 하늘을 다시 지정한다).
public class SkyboxTimeOfDay : MonoBehaviour
{
    static readonly int[] Hours = { 6, 9, 12, 18, 22 };
    static readonly int RotationId = Shader.PropertyToID("_Rotation");
    readonly Dictionary<int, Material> skies = new Dictionary<int, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindAnyObjectByType<SkyboxTimeOfDay>() != null) return;
        if (Resources.Load<Material>("Skybox/Sky_12") == null) return;   // 아직 COM 세팅 전
        new GameObject("SkyboxTimeOfDay (자동)").AddComponent<SkyboxTimeOfDay>();
    }

    void Awake()
    {
        foreach (int h in Hours)
        {
            Material src = Resources.Load<Material>($"Skybox/Sky_{h:00}");
            if (src != null) skies[h] = new Material(src);   // 회전값을 바꾸므로 복사본 사용 (원본 에셋은 그대로)
        }
    }

    void OnEnable()  { HomeEvents.OnHourChanged += Apply; }
    void OnDisable() { HomeEvents.OnHourChanged -= Apply; }

    void Start()
    {
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            if (mb is ITimeOfDay t) { Apply(t.CurrentHour >= 0 ? t.CurrentHour : 12); return; }
        Apply(12);
    }

    void Apply(int hour)
    {
        Material sky = Nearest(hour);
        if (sky == null) return;

        Light sun = RenderSettings.sun;
        if (sun != null && sun.enabled)
        {
            Vector3 d = -sun.transform.forward;                      // 해가 있는 쪽
            float az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;        // +Z 기준 방위
            sky.SetFloat(RotationId, Mathf.Repeat(az, 360f));
        }
        RenderSettings.skybox = sky;
        DynamicGI.UpdateEnvironment();
    }

    Material Nearest(int hour)
    {
        int best = -1, diff = int.MaxValue;
        foreach (int h in skies.Keys) { int d = Mathf.Abs(h - hour); if (d < diff) { diff = d; best = h; } }
        return best >= 0 ? skies[best] : null;
    }
}
