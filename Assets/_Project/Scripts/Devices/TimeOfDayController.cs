using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시간대 처리 (FR-30, WBS 4.09·4.10, SRS v2.5 부록 A-8) — ITimeOfDay 구현
/// - 시각 버튼(ClockUI, 권오민)이 SetHour(h)를 부르면 햇빛(방향·색·세기)·하늘·환경광을 그 시각 값으로 바로 바꾼다 (보간 없음)
/// - 하늘은 skybox로 바꾼다: Procedural skybox의 해 위치·색·노출·대기를 시각마다 바꿔 창밖 하늘이 달라진다
/// - 실내 밝기는 환경광 세기로 바꾸고, 환경광 색은 하늘색을 조금만(ambientTint) 따라가게 한다
///   (하늘색을 그대로 쓰면 노을·밤에 로봇팔 같은 물체 색이 바뀌어 보임)
///   (창유리 발광은 보조로만 사용, dimWindowGlass로 끌 수 있음)
/// - 프레임 하락 원인 확인: 에디터에서 시각을 바꿀 때마다 적용 시간과 앞뒤 프레임 시간을 콘솔에 남긴다 (logPerformance)
/// - GetZoneLux(zone) = 창 쪽 햇빛 조도 × 구역 계수(ZoneData.luxFactor) + 300lx × 그 구역 천장 조명 밝기(0~1)
/// - 이벤트 기록("시각 변경 18:00")은 ClockUI가 남기므로 여기서는 Log 하지 않고 RaiseHourChanged만 보낸다
/// - 초기화(FR-26 8번): 12:00으로 되돌리고 RaiseHourChanged(12)
/// 붙이는 법: 메뉴 Tools → COM → 시간대 컨트롤러 붙이기 (COM_System에 붙고 Directional Light를 자동 연결)
/// </summary>
public class TimeOfDayController : MonoBehaviour, ITimeOfDay
{
    [System.Serializable]
    public struct HourPreset
    {
        public int hour;
        [Tooltip("해 고도 (도) = 햇빛 X 회전")] public float elevation;
        [Tooltip("해 방위 (도), 0 = +Z(북), 90 = 동, 180 = 남, 270 = 서")] public float azimuth;
        [Range(0f, 1f)] public float sunIntensity;      // 12:00 = 1
        public Color sunColor;
        public Color skyColor;
        [Range(0f, 1f)] public float ambient;           // 12:00 = 1
        public float windowLux;                         // 창 쪽 햇빛 조도 (lx)
        [Range(0f, 1f), Tooltip("창유리 밝기 (12:00 = 1)")] public float windowGlow;
        [Range(0f, 1f), Tooltip("창유리 색을 하늘색 쪽으로 물들이는 정도")] public float windowTint;
    }

    [Tooltip("햇빛 역할 Directional Light. 비우면 씬에서 찾는다")]
    [SerializeField] Light sun;
    [Tooltip("집 방향 보정: 공간 에셋의 +Z가 실제 북쪽과 다르면 여기서 돌린다 (도)")]
    [SerializeField] float northOffset = 0f;
    [Tooltip("천장 조명이 100%일 때 더하는 조도 (lx)")]
    [SerializeField] float ceilingLux = 300f;
    [SerializeField] int startHour = 12;
    [Tooltip("창유리 재질 (공간 에셋 m_glass는 스스로 빛나는 재질이라 시각에 맞춰 어둡게 해야 함). 비우면 이름으로 찾는다")]
    [SerializeField] Material windowGlass;
    [SerializeField] string windowGlassName = "m_glass";
    [Tooltip("창유리 발광도 시각에 맞춰 어둡게 (skybox만으로 충분하면 끈다)")]
    [SerializeField] bool dimWindowGlass = true;

    [Header("하늘 (skybox)")]
    [Tooltip("하늘 재질. 비우면 씬의 Skybox 재질(기본 Procedural)을 복사해서 쓴다")]
    [SerializeField] Material skyboxMaterial;
    [Tooltip("환경광 갱신 때 같이 다시 그리는 기본 반사 큐브맵 해상도. 1024면 시각을 바꿀 때 프레임이 튄다 (실내 반사는 구워 둔 반사 프로브를 씀)")]
    [SerializeField] int environmentReflectionSize = 128;
    [Tooltip("실내 물체(로봇팔·컵 등)에 비치는 환경광이 하늘색을 얼마나 따라갈지 (0 = 회색 그대로 밝기만, 1 = 하늘색 그대로). 크면 노을·밤에 로봇팔 색이 변해 보임")]
    [Range(0f, 1f)]
    [SerializeField] float ambientTint = 0.15f;
    [Tooltip("실내 물체(로봇팔·컵)의 환경광 밝기를 시각과 상관없이 고정. 집 벽·바닥은 빛을 구워 둬서 시각이 바뀌어도 그대로인데, 움직이는 물체만 어두워지면 검게 변해 보이므로 맞춘다")]
    [SerializeField] bool keepIndoorAmbient = true;

    [Header("성능 확인 (에디터에서만)")]
    [Tooltip("시각을 바꿀 때 적용 시간(ms)과 바꾸기 전·후 30프레임 평균을 콘솔에 남긴다")]
    [SerializeField] bool logPerformance = true;

    // SRS v2.5 부록 A-8 (시뮬레이션 초기값)
    [SerializeField] HourPreset[] presets =
    {
        P(6,  5f,  90f, 0.10f, "#FFA148", "#F2A66B", 0.30f, 100f, 0.25f, 0.8f),
        P(9,  35f, 120f, 0.60f, "#FFE4CE", "#8EC5F0", 0.70f, 400f, 0.70f, 0.0f),
        P(12, 60f, 180f, 1.00f, "#FFF9FD", "#6FB4F0", 1.00f, 800f, 1.00f, 0.0f),
        P(18, 5f,  270f, 0.10f, "#FFA148", "#E07A5F", 0.30f, 100f, 0.25f, 0.8f),
        P(22, -10f, 0f, 0.00f, "#000000", "#0B1026", 0.05f, 0f,   0.03f, 1.0f),
    };

    public int CurrentHour { get; private set; } = -1;

    float baseSunIntensity = 1f;
    float baseAmbient = 1f;
    float baseExposure = 1.3f;
    Material skyMat;
    ZoneLight[] zoneLights;
    Material glassRuntime;
    Color glassBaseEmission;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    static HourPreset P(int h, float el, float az, float si, string sc, string sky, float amb, float lux, float glow, float tint)
    {
        ColorUtility.TryParseHtmlString(sc, out Color c1);
        ColorUtility.TryParseHtmlString(sky, out Color c2);
        return new HourPreset { hour = h, elevation = el, azimuth = az, sunIntensity = si, sunColor = c1, skyColor = c2, ambient = amb, windowLux = lux, windowGlow = glow, windowTint = tint };
    }

    void Awake()
    {
        if (sun == null) sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null) Debug.LogWarning("[TimeOfDay] Directional Light(햇빛)를 못 찾음", this);
        else
        {
            baseSunIntensity = sun.intensity > 0f ? sun.intensity : 1f;
            sun.useColorTemperature = false;
        }

        baseAmbient = RenderSettings.ambientIntensity > 0f ? RenderSettings.ambientIntensity : 1f;
        Material skySource = skyboxMaterial != null ? skyboxMaterial : RenderSettings.skybox;
        if (skySource != null)
        {
            skyMat = new Material(skySource) { name = skySource.name + " (Runtime)" };
            if (skyMat.HasProperty("_Exposure")) baseExposure = skyMat.GetFloat("_Exposure");
            RenderSettings.skybox = skyMat;   // 원본 재질 에셋은 건드리지 않음
        }
        else Debug.LogWarning("[TimeOfDay] Skybox 재질이 없음 → Lighting 창의 Skybox Material 지정 (실내 밝기 변화가 약해짐)", this);

        // 하늘이 햇빛 방향을 따라가고, 환경광·기본 반사를 하늘에서 계산하게 한다 (Play 중에만, 씬 설정은 그대로)
        if (sun != null) RenderSettings.sun = sun;
        // 환경광은 하늘에서 직접 계산하지 않고(하늘색이 그대로 물체 색을 바꿈) 하늘 밝기를 따라가는 회색 계열로 둔다
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = Mathf.Max(16, environmentReflectionSize);

        SetupWindowGlass();
        Apply(startHour);
    }

    // 창유리를 쓰는 렌더러의 재질을 실행 중 복사본 하나로 바꿔 끼운다 (원본 에셋은 그대로)
    void SetupWindowGlass()
    {
        int count = 0;
        foreach (Renderer r in FindObjectsByType<Renderer>())
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null) continue;
                bool isGlass = windowGlass != null ? m == windowGlass : m.name == windowGlassName;
                if (!isGlass) continue;
                if (glassRuntime == null)
                {
                    glassRuntime = new Material(m) { name = m.name + " (Runtime)" };
                    glassBaseEmission = glassRuntime.HasProperty(EmissionId) ? glassRuntime.GetColor(EmissionId) : Color.white;
                }
                mats[i] = glassRuntime;
                changed = true;
            }
            if (changed) { r.sharedMaterials = mats; count++; }
        }
        if (glassRuntime == null) Debug.Log($"[TimeOfDay] 창유리 재질 '{windowGlassName}'을 쓰는 렌더러가 없음 (창 밝기는 안 바뀜)");
        else Debug.Log($"[TimeOfDay] 창유리 {count}개를 시각에 맞춰 밝기 조절");
    }

    void Start() => zoneLights = FindObjectsByType<ZoneLight>();

    // 씬에 붙이는 걸 잊어도 시각 버튼(ClockUI)이 있는 씬이면 Play 때 자동으로 하나 만든다
    // (씬 로드 뒤·Start 전에 실행 → ClockUI.Start가 찾아서 연결)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindAnyObjectByType<TimeOfDayController>() != null) return;
        if (FindAnyObjectByType<ClockUI>() == null) return;
        new GameObject("TimeOfDayController (자동)").AddComponent<TimeOfDayController>();
        Debug.Log("[TimeOfDay] 씬에 없어서 자동으로 만듦 (Tools → COM → 시간대 컨트롤러 붙이기로 씬에 고정 가능)");
    }

    void OnEnable() => HomeEvents.OnScenarioReset += HandleReset;
    void OnDisable() => HomeEvents.OnScenarioReset -= HandleReset;

    void OnDestroy()
    {
        if (skyMat != null) Destroy(skyMat);
        if (glassRuntime != null) Destroy(glassRuntime);
    }

    // ── ITimeOfDay ──────────────────────────────

    public void SetHour(int hour)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Apply(hour);
        sw.Stop();
        HomeEvents.RaiseHourChanged(CurrentHour);
        if (logPerformance && Application.isEditor) StartCoroutine(MeasureFrames(CurrentHour, sw.Elapsed.TotalMilliseconds));
    }

    // 프레임 시간 기록 (최근 30프레임을 늘 모아 두고, 시각을 바꾼 뒤 30프레임과 비교)
    readonly Queue<float> recentFrames = new Queue<float>();
    void Update()
    {
        if (!logPerformance) return;
        recentFrames.Enqueue(Time.unscaledDeltaTime);
        if (recentFrames.Count > 30) recentFrames.Dequeue();
    }

    System.Collections.IEnumerator MeasureFrames(int hour, double applyMs)
    {
        float before = 0f; foreach (float f in recentFrames) before += f;
        before = recentFrames.Count > 0 ? before / recentFrames.Count * 1000f : 0f;
        float worst = 0f, sum = 0f;
        for (int i = 0; i < 30; i++)
        {
            yield return null;
            float ms = Time.unscaledDeltaTime * 1000f;
            sum += ms; worst = Mathf.Max(worst, ms);
        }
        Debug.Log($"[TimeOfDay] {hour:00}:00 적용 {applyMs:F1}ms, 바꾼 뒤 30프레임 평균 {sum / 30f:F1}ms (가장 긴 프레임 {worst:F1}ms), 바꾸기 전 평균 {before:F1}ms", this);
    }

    public float GetZoneLux(ZoneVolume zone)
    {
        if (zone == null) return 0f;
        HourPreset p = Find(CurrentHour);
        float factor = zone.Data != null ? zone.Data.luxFactor : 1f;
        float ceiling = 0f;
        if (zoneLights == null) zoneLights = FindObjectsByType<ZoneLight>();
        foreach (ZoneLight zl in zoneLights)
            if (zl != null && zl.Zone == zone) ceiling = Mathf.Max(ceiling, zl.Brightness);
        return p.windowLux * factor + ceilingLux * ceiling;
    }

    // ── 내부 ────────────────────────────────────

    void HandleReset()
    {
        Apply(startHour);
        HomeEvents.RaiseHourChanged(CurrentHour);
    }

    void Apply(int hour)
    {
        HourPreset p = Find(hour);
        CurrentHour = p.hour;

        if (sun != null)
        {
            // 해가 방위 az에 있으면 빛은 그 반대쪽으로 비춘다
            sun.transform.rotation = Quaternion.Euler(Mathf.Max(p.elevation, 1f), p.azimuth + 180f + northOffset, 0f);
            sun.color = p.sunColor;
            sun.intensity = baseSunIntensity * p.sunIntensity;
            sun.enabled = p.sunIntensity > 0f;
        }

        RenderSettings.ambientIntensity = baseAmbient * (keepIndoorAmbient ? 1f : p.ambient);
        // 실내 밝기는 시각을 따라가되 색은 거의 회색: 로봇팔·컵 같은 물체 색이 시각마다 바뀌어 보이지 않게
        Color tint = Color.Lerp(Color.white, p.skyColor, ambientTint);
        float lum = keepIndoorAmbient ? 1f : Mathf.Max(p.ambient, 0.05f);
        RenderSettings.ambientSkyColor = tint * (0.85f * lum);
        RenderSettings.ambientEquatorColor = tint * (0.55f * lum);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.2f) * lum;
        if (skyMat != null)
        {
            // 하늘 자체 색은 시각에 맞게 (창밖으로 보이는 하늘)
            if (skyMat.HasProperty("_SkyTint")) skyMat.SetColor("_SkyTint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), p.skyColor, 0.6f));
            if (skyMat.HasProperty("_Tint")) skyMat.SetColor("_Tint", p.skyColor);
            if (skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", baseExposure * Mathf.Max(p.ambient, 0.05f));
            // 해가 낮으면 대기를 두껍게(노을빛), 밤에는 얇게(어두운 하늘)
            if (skyMat.HasProperty("_AtmosphereThickness"))
                skyMat.SetFloat("_AtmosphereThickness", p.sunIntensity <= 0f ? 0.4f : p.elevation < 15f ? 1.3f : 1f);
        }
        if (dimWindowGlass && glassRuntime != null && glassRuntime.HasProperty(EmissionId))
        {
            float mag = Mathf.Max(glassBaseEmission.r, glassBaseEmission.g, glassBaseEmission.b);
            Color tinted = p.skyColor.linear * mag;
            Color c = Color.Lerp(glassBaseEmission, tinted, p.windowTint) * p.windowGlow;
            glassRuntime.SetColor(EmissionId, c);
        }
        DynamicGI.UpdateEnvironment();
    }

    // 정확히 같은 시각이 없으면 가장 가까운 버튼 시각을 쓴다
    HourPreset Find(int hour)
    {
        HourPreset best = presets[0];
        int bestDiff = int.MaxValue;
        foreach (HourPreset p in presets)
        {
            int d = Mathf.Abs(p.hour - hour);
            if (d < bestDiff) { bestDiff = d; best = p; }
        }
        return best;
    }
}
