using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 낮 실내 밝기 보정 (10/13 강사 피드백 "낮인데 너무 어둡다", NFR-10)
// 집 벽·바닥 빛은 구워 둔 라이트맵이라 시각이 바뀌어도 그대로 → 낮에도 실내가 밤과 비슷하게 어두워 보였다.
// → 시각이 바뀔 때마다(HomeEvents.OnHourChanged) 시각별로 화면을 밝게 한다. 밤은 그대로.
//   1) 노출(Post Exposure, EV): 전체를 밝게. 많이 올리면 창가·가까운 흰 벽이 하얗게 날아가서 적당히만.
//   2) 중간톤 올리기(Lift Gamma Gain의 Gamma): 어두운 복도·거실 안쪽을 끌어올린다. 밝은 곳은 덜 바뀜.
//   전역 Volume을 실행 중에 하나 만들어 이 두 항목만 덮어쓴다 (씬·프로필 에셋은 건드리지 않음).
// 씬에 붙일 필요 없음 — Play 하면 자동으로 하나 생긴다. 시간대 코드(TimeOfDayController)는 고치지 않는다.
public class ExposureTimeOfDay : MonoBehaviour
{
    // 시각 → 노출 보정 (EV, +1 = 2배 밝게)
    static readonly Dictionary<int, float> Exposure = new Dictionary<int, float>
    {
        { 6, 0.15f }, { 9, 0.35f }, { 12, 0.45f }, { 18, 0.15f }, { 22, 0f },   // 실제 밝기는 DaylightFill(채광 조명)이 담당, 여기는 살짝만
    };
    // 시각 → 중간톤 올림 (Gamma 오프셋, 0 = 그대로, 0.3이면 어두운 곳이 눈에 띄게 밝아짐)
    static readonly Dictionary<int, float> Midtone = new Dictionary<int, float>
    {
        { 6, 0f }, { 9, 0f }, { 12, 0f }, { 18, 0f }, { 22, 0f },   // 하얗게 뜬 느낌이 나서 끔 (필요하면 0.05~0.1)
    };

    Volume volume;
    VolumeProfile profile;
    ColorAdjustments color;
    LiftGammaGain lgg;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindAnyObjectByType<ExposureTimeOfDay>() != null) return;
        if (FindAnyObjectByType<ClockUI>() == null) return;
        new GameObject("ExposureTimeOfDay (자동)").AddComponent<ExposureTimeOfDay>();
    }

    void Awake()
    {
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        color = profile.Add<ColorAdjustments>(true);
        color.postExposure.overrideState = true;
        color.postExposure.value = 0f;
        // 나머지 항목은 덮어쓰지 않음 (기존 프로필 값 유지)
        color.contrast.overrideState = false;
        color.colorFilter.overrideState = false;
        color.hueShift.overrideState = false;
        color.saturation.overrideState = false;

        lgg = profile.Add<LiftGammaGain>(false);
        lgg.gamma.overrideState = true;
        lgg.gamma.value = new Vector4(1f, 1f, 1f, 0f);

        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.sharedProfile = profile;
    }

    void OnEnable()  { HomeEvents.OnHourChanged += Apply; }
    void OnDisable() { HomeEvents.OnHourChanged -= Apply; }
    void OnDestroy() { if (profile != null) Destroy(profile); }

    void Start()
    {
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            if (mb is ITimeOfDay t) { Apply(t.CurrentHour >= 0 ? t.CurrentHour : 12); return; }
        Apply(12);
    }

    void Apply(int hour)
    {
        if (color != null) color.postExposure.value = Lookup(Exposure, hour);
        if (lgg != null) lgg.gamma.value = new Vector4(1f, 1f, 1f, Lookup(Midtone, hour));
    }

    static float Lookup(Dictionary<int, float> table, int hour)
    {
        if (table.TryGetValue(hour, out float v)) return v;
        int best = 12, diff = int.MaxValue;
        foreach (int h in table.Keys) { int d = Mathf.Abs(h - hour); if (d < diff) { diff = d; best = h; } }
        return table[best];
    }
}
