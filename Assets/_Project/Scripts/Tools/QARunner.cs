using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// QA 자동 점검 (회귀 테스트용). Play 중 메뉴 Tools → COM → QA 자동 점검 으로 실행.
/// 사람이 하던 QA 체크리스트 일부를 같은 절차로 자동 실행하고, 결과를 콘솔과
/// C:\Unity\COM_Evidence\..._QA자동점검.txt 로 남긴다 (리팩토링 전후 같은 결과인지 비교).
/// 대상: TC-54, TC-04, TC-38, TC-05, TC-06, TC-07, TC-02, TC-14, TC-16-U, TC-48, TC-47, TC-49, TC-34, TC-16, TC-33, TC-16-P, TC-16-S, TC-18, TC-39, TC-52
/// 참고: 점검 중 에디터 FPS(TC-37은 빌드에서 따로 측정)
/// 플레이어는 구역 사이를 순간이동(CharacterController 유지 + Physics.SyncTransforms)해서 실제 트리거로 판정한다.
/// </summary>
public class QARunner : MonoBehaviour
{
    struct Result { public string tc, name, detail; public bool pass; }
    readonly List<Result> results = new List<Result>();
    public bool Running { get; private set; }

    PlayerController player;
    ScenarioManager scenario;
    ConfigurationState config;
    CoffeeTask task;
    RobotArm arm;
    CoffeeMachine machine;
    ITimeOfDay tod;
    ZoneVolume entrance, living;
    ZoneLight livingLight, entranceLight;

    public static QARunner Run()
    {
        var go = new GameObject("[QARunner]");
        var r = go.AddComponent<QARunner>();
        r.StartCoroutine(r.All());
        return r;
    }

    void Add(string tc, string name, bool pass, string detail)
    {
        results.Add(new Result { tc = tc, name = name, pass = pass, detail = detail });
        string line = $"[QA] {(pass ? "통과" : "실패")}  {tc} {name} — {detail}";
        if (pass) Debug.Log(line); else Debug.LogWarning(line);
    }

    IEnumerator All()
    {
        Running = true;
        HomeEvents.Log("QA", "QA 자동 점검 시작");
        if (!Find()) { Finish(); yield break; }

        StartCoroutine(SampleFps());
        yield return Safe(Zones());
        yield return Safe(Ui());
        yield return Safe(TimeOfDay());
        yield return Safe(Options());
        yield return Safe(ResetKeepsOptions());
        yield return Safe(Coffee());

        Static();
        scenario.ResetScenario();
        yield return new WaitForSeconds(1f);
        Finish();
    }

    // 점검하는 동안 프레임 시간 (에디터 참고값)
    float fpsSum; int fpsN; float worstDt;
    IEnumerator SampleFps()
    {
        yield return new WaitForSeconds(1f);
        while (Running) { float dt = Time.unscaledDeltaTime; fpsSum += dt; fpsN++; worstDt = Mathf.Max(worstDt, dt); yield return null; }
    }

    // ── UI (FR-02, FR-11, FR-13 조작 패널) ──────

    static T Field<T>(object o, string name) where T : class =>
        o?.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o) as T;

    IEnumerator Ui()
    {
        // TC-02: 들어가면 그 구역 정보 창 3초
        ZoneInfoUI info = FindAnyObjectByType<ZoneInfoUI>();
        if (info == null) Add("TC-02", "구역 정보 창", false, "ZoneInfoUI 없음");
        else
        {
            CanvasGroup g = info.GetComponent<CanvasGroup>();
            Teleport(PointIn(entrance));
            yield return new WaitForSeconds(4f);
            Teleport(PointIn(living));
            yield return new WaitForSeconds(0.3f);
            bool shown = g.alpha > 0.99f;
            bool name = info.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains(living.Data.displayName));
            yield return new WaitForSeconds(2.4f);
            bool still = g.alpha > 0.99f;
            yield return new WaitForSeconds(0.6f);
            bool hidden = g.alpha < 0.01f;
            Add("TC-02", "구역 정보 창 3초 표시", shown && name && still && hidden, $"표시 {shown}, '{living.Data.displayName}' 글자 {name}, 2.7초 유지 {still}, 3.3초 사라짐 {hidden}");
        }

        // TC-14: 메뉴 패널 0.5초 이내 열림, 다른 패널 닫힘, 열린 동안 시간 정지 없음
        HUDController hud = FindAnyObjectByType<HUDController>();
        PanelUI opt = Field<PanelUI>(hud, "optionPanel"), log = Field<PanelUI>(hud, "logPanel");
        if (hud == null || opt == null || log == null) Add("TC-14", "메뉴·패널", false, "HUDController/패널 연결 없음");
        else
        {
            hud.CloseAllPanels(); yield return null;
            float t0 = Time.realtimeSinceStartup;
            hud.TogglePanel(opt);
            bool optOpen = opt.IsOpen(); float ms = (Time.realtimeSinceStartup - t0) * 1000f;
            yield return null;
            hud.TogglePanel(log);
            bool swap = log.IsOpen() && !opt.IsOpen();
            bool time = Time.timeScale == 1f;
            bool move = player.enabled;   // 패널이 열려도 이동 스크립트가 꺼지지 않음
            hud.TogglePanel(log);
            bool toggleClose = !log.IsOpen();
            Add("TC-14", "메뉴 패널 열기·전환·이동 가능", optOpen && ms <= 500f && swap && time && move && toggleClose, $"옵션 열림 {optOpen}({ms:F0}ms), 기록으로 전환 시 옵션 닫힘 {swap}, timeScale 1 {time}, 열린 채 이동 스크립트 켜짐 {move}, 다시 누르면 닫힘 {toggleClose}");
            hud.CloseAllPanels();
        }

        // TC-16-U: 로봇팔 2.5m 안에서만 조작 패널, 버튼 3개, 식탁 안내판
        CoffeeControlUI cui = FindAnyObjectByType<CoffeeControlUI>();
        if (cui == null || arm == null) Add("TC-16-U", "조작 패널·안내판", false, "CoffeeControlUI 없음");
        else
        {
            CanvasGroup cg = cui.GetComponent<CanvasGroup>();
            Teleport(PointIn(entrance));
            yield return new WaitForSeconds(0.8f);
            float far = cg != null ? cg.alpha : -1f;
            Vector3 b = arm.BasePosition; Vector3 toLiving = PointIn(living) - b; toLiving.y = 0f;
            Vector3 near = b + toLiving.normalized * 1.5f; near.y = floorY;
            Teleport(near);
            yield return new WaitForSeconds(0.8f);
            float close = cg != null ? cg.alpha : -1f;
            string[] labels = cui.GetComponentsInChildren<Button>(true).Select(x => x.GetComponentInChildren<Text>(true)?.text ?? "").ToArray();
            bool buttons = labels.Any(l => l.Contains("동작")) && labels.Any(l => l.Contains("일시정지") || l.Contains("이어서")) && labels.Any(l => l.Contains("정지"));
            GameObject board = GameObject.Find("RobotGuideBoard");
            string boardText = board != null ? string.Join(" ", board.GetComponentsInChildren<Text>(true).Select(t => t.text)) : "";
            bool boardOk = boardText.Contains("Z") && boardText.Contains("X") && boardText.Contains("C");
            Add("TC-16-U", "조작 패널(2.5m)·버튼·안내판", far < 0.05f && close > 0.95f && buttons && boardOk, $"멀리 {far:F2} / 1.5m {close:F2}, 버튼 [{string.Join("·", labels)}], 안내판 {(board != null ? (boardOk ? "Z·X·C 있음" : "글자 없음") : "없음")}");
            Teleport(PointIn(entrance));
            yield return new WaitForSeconds(0.5f);
        }
    }

    // ── 정적 검사 (NFR-03, NFR-10) ──────────────

    void Static()
    {
        // TC-39: 문 높이 약 2.5m, 공간 에셋 배율 1
        var doors = FindObjectsByType<Renderer>().Where(r => r.name.StartsWith("sm_door") && !r.name.Contains("frame") && !r.name.Contains("closet")).ToList();
        string dh = string.Join(", ", doors.Take(4).Select(r => $"{r.bounds.size.y:F2}m"));
        bool doorOk = doors.Count > 0 && doors.All(r => r.bounds.size.y > 2.2f && r.bounds.size.y < 2.8f);
        // 배율은 공간 에셋 최상위(문이 들어 있는 루트) 기준. 소품 일부는 에셋 원본 배치에 크기·좌우 반전이 들어 있어 제외
        var roots = doors.Select(r => r.transform.root).Distinct().ToList();
        var scaled = roots.Where(t => (t.localScale - Vector3.one).sqrMagnitude > 1e-4f).Select(t => $"{t.name} {t.localScale}").ToList();
        Add("TC-39", "공간 배율 1·문 높이 약 2.5m", doorOk && scaled.Count == 0, $"문 {doors.Count}개 높이 {dh}, 공간 루트 {string.Join(", ", roots.Select(t => t.name))} 배율 {(scaled.Count == 0 ? "1" : string.Join(", ", scaled))}");

        // TC-52: Forward+, 간접광 굽기(라이트맵), 반사 프로브, 후처리 Volume
        var bad = new List<string>(); var ok = new List<string>();
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        object list = rp?.GetType().GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(rp);
        object data = list is Array arr && arr.Length > 0 ? arr.GetValue(0) : null;
        string mode = data?.GetType().GetProperty("renderingMode")?.GetValue(data)?.ToString() ?? "?";
        if (mode.Contains("Forward") && mode.Contains("Plus")) ok.Add("Forward+"); else bad.Add($"렌더링 경로 {mode}");
        int maps = LightmapSettings.lightmaps.Length;
        if (maps > 0) ok.Add($"라이트맵 {maps}장"); else bad.Add("구운 라이트맵 없음");
        var probes = FindObjectsByType<ReflectionProbe>();
        int realtime = probes.Count(p => p.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime);
        if (probes.Length >= 4) ok.Add($"반사 프로브 {probes.Length}개(실시간 {realtime})"); else bad.Add($"반사 프로브 {probes.Length}개");
        var vols = FindObjectsByType<UnityEngine.Rendering.Volume>().Where(v => v.isGlobal && v.sharedProfile != null).ToList();
        var comps = vols.SelectMany(v => v.sharedProfile.components.Where(c => c.active).Select(c => c.GetType().Name)).Distinct().ToList();
        if (comps.Contains("Tonemapping") && comps.Contains("Bloom")) ok.Add("Volume " + string.Join("·", comps)); else bad.Add("Global Volume 후처리 부족: " + string.Join("·", comps));
        Add("TC-52", "렌더링 설정 (Baked Indirect 모드는 에디터 Lighting 창에서 확인)", bad.Count == 0, bad.Count == 0 ? string.Join(", ", ok) : string.Join(" / ", bad));

        if (fpsN > 0)
            Add("참고", "점검 중 에디터 FPS (TC-37은 빌드로 측정)", true, $"평균 {fpsN / fpsSum:F0} FPS, 가장 느린 프레임 {worstDt * 1000f:F0}ms ({1f / worstDt:F0} FPS) — 에디터·Scene 창 포함");
    }

    // 하나가 예외로 죽어도 나머지는 계속
    IEnumerator Safe(IEnumerator e)
    {
        while (true)
        {
            object cur;
            try { if (!e.MoveNext()) yield break; cur = e.Current; }
            catch (Exception ex) { Add("ERR", "점검 중 예외", false, ex.Message); Debug.LogException(ex); yield break; }
            yield return cur;
        }
    }

    bool Find()
    {
        player = FindAnyObjectByType<PlayerController>();
        scenario = FindAnyObjectByType<ScenarioManager>();
        config = FindAnyObjectByType<ConfigurationState>();
        task = FindAnyObjectByType<CoffeeTask>();
        arm = FindAnyObjectByType<RobotArm>();
        machine = FindAnyObjectByType<CoffeeMachine>();
        tod = FindObjectsByType<MonoBehaviour>().OfType<ITimeOfDay>().FirstOrDefault();
        foreach (ZoneVolume z in FindObjectsByType<ZoneVolume>())
        {
            if (z.IsSensorOnly || z.Data == null) continue;
            if (z.Data.displayName.Contains("현관")) entrance = z;
            if (z.Data.displayName.Contains("거실")) living = z;
        }
        foreach (ZoneLight l in FindObjectsByType<ZoneLight>())
        {
            if (l.Zone == living) livingLight = l;
            if (l.Zone == entrance) entranceLight = l;
        }
        var missing = new List<string>();
        if (player == null) missing.Add("PlayerController");
        if (scenario == null) missing.Add("ScenarioManager");
        if (config == null) missing.Add("ConfigurationState");
        if (task == null) missing.Add("CoffeeTask");
        if (tod == null) missing.Add("ITimeOfDay");
        if (living == null || entrance == null) missing.Add("현관/거실 ZoneVolume");
        if (livingLight == null) missing.Add("거실 ZoneLight");
        if (missing.Count > 0) { Add("준비", "대상 찾기", false, "없음: " + string.Join(", ", missing)); return false; }
        return true;
    }

    // ── 구역·조명 ───────────────────────────────

    Vector3 PointIn(ZoneVolume z)
    {
        Collider best = null; float vol = -1f;
        foreach (Collider c in z.GetComponentsInChildren<Collider>())
        {
            if (!c.isTrigger) continue;
            Vector3 s = c.bounds.size; float v = s.x * s.y * s.z;
            if (v > vol) { vol = v; best = c; }
        }
        Vector3 p = best != null ? best.bounds.center : z.transform.position;
        return new Vector3(p.x, floorY, p.z);
    }
    float floorY;

    void Teleport(Vector3 p)
    {
        player.transform.position = p;
        Physics.SyncTransforms();
    }

    IEnumerator Zones()
    {
        scenario.ResetScenario();
        yield return new WaitForSeconds(2f);
        floorY = player.transform.position.y;

        // TC-54: 시작·초기화 후 현관 입실, 현관 조명 켜짐
        bool entOk = entrance.IsOccupied && !living.IsOccupied && (entranceLight == null || entranceLight.Brightness >= 0.99f);
        Add("TC-54", "초기화 후 현관 재실", entOk, $"현관 재실 {entrance.IsOccupied}, 거실 재실 {living.IsOccupied}, 현관 조명 {(entranceLight != null ? entranceLight.Brightness.ToString("F2") : "없음")}");

        Vector3 inLiving = PointIn(living), inEntrance = PointIn(entrance);

        // TC-04: 들어가고 1초 이내 100% / TC-38: 방 진입 → 조명 켜기 시작 0.5초 이내
        DateTime mark = DateTime.Now;
        float t0 = Time.time;
        Teleport(inLiving);
        float tFull = -1f;
        while (Time.time - t0 < 3f)
        {
            yield return null;
            if (livingLight.Brightness >= 0.999f) { tFull = Time.time - t0; break; }
        }
        Add("TC-04", "입실 후 1초 이내 밝기 100%", tFull >= 0f && tFull <= 1.1f, tFull >= 0f ? $"{tFull:F2}초" : "3초 안에 100% 안 됨");
        double gap = Gap(mark, living.DisplayName, "방 진입", "조명 켜기 시작");
        Add("TC-38", "방 진입 → 조명 켜기 시작", gap >= 0 && gap <= 500, gap >= 0 ? $"{gap:F0}ms" : "기록 없음");

        // TC-05: 나가고 3초 후 어두워지기 시작, 4초에 0%
        yield return new WaitForSeconds(0.5f);
        Teleport(inEntrance);
        t0 = Time.time;
        float tStart = -1f, tZero = -1f;
        while (Time.time - t0 < 6f)
        {
            yield return null;
            float b = livingLight.Brightness, el = Time.time - t0;
            if (tStart < 0f && b < 0.999f) tStart = el;
            if (b <= 0f) { tZero = el; break; }
        }
        Add("TC-05", "퇴실 3초 후 감광 시작, 4초에 0%", Mathf.Abs(tStart - 3f) <= 0.2f && Mathf.Abs(tZero - 4f) <= 0.25f, $"감광 시작 {tStart:F2}초, 0% {tZero:F2}초");

        // TC-06: 나가고 2초 뒤 재진입 → 계속 켜짐
        Teleport(inLiving);
        yield return new WaitForSeconds(1.5f);
        Teleport(inEntrance);
        yield return new WaitForSeconds(2f);
        Teleport(inLiving);
        float min = 1f; t0 = Time.time;
        while (Time.time - t0 < 3f) { yield return null; min = Mathf.Min(min, livingLight.Brightness); }
        Add("TC-06", "2초 뒤 재진입 시 켜진 채 유지", min >= 0.999f, $"재진입 후 3초 동안 최저 밝기 {min:F2}");

        // TC-07: 3.5초 뒤(감광 중) 재진입 → 1초 이내 100%
        Teleport(inEntrance);
        yield return new WaitForSeconds(3.5f);
        float mid = livingLight.Brightness;
        Teleport(inLiving);
        t0 = Time.time; tFull = -1f;
        while (Time.time - t0 < 3f)
        {
            yield return null;
            if (livingLight.Brightness >= 0.999f) { tFull = Time.time - t0; break; }
        }
        Add("TC-07", "감광 중 재진입 시 1초 이내 100%", tFull >= 0f && tFull <= 1.1f && mid < 0.999f, $"재진입 때 밝기 {mid:F2} → 100%까지 {tFull:F2}초");
    }

    // 같은 대상의 기록 a 다음 b까지 ms
    double Gap(DateTime since, string target, string a, string b)
    {
        DateTime ta = DateTime.MinValue;
        foreach (EventLogger.Entry e in EventLogger.Entries)
        {
            if (e.time < since || e.target != target) continue;
            if (ta == DateTime.MinValue && e.message.Contains(a)) ta = e.time;
            else if (ta != DateTime.MinValue && e.message.Contains(b)) return (e.time - ta).TotalMilliseconds;
        }
        return -1;
    }

    // ── 시간대 (부록 A-8) ───────────────────────

    IEnumerator TimeOfDay()
    {
        // 시각, 햇빛 세기(12시=1), 고도, 방위, 창 쪽 조도
        var a8 = new (int h, float si, float el, float az, float lux)[]
        { (6, 0.1f, 5f, 90f, 100f), (9, 0.6f, 35f, 120f, 400f), (12, 1f, 60f, 180f, 800f), (18, 0.1f, 5f, 270f, 100f), (22, 0f, -10f, 0f, 0f) };
        Light sun = RenderSettings.sun;
        tod.SetHour(12);
        yield return null;
        float baseI = sun != null && sun.intensity > 0f ? sun.intensity : 1f;
        float ambient12 = RenderSettings.ambientIntensity;
        var bad = new List<string>(); var info = new List<string>();
        foreach (var e in a8)
        {
            float t0 = Time.realtimeSinceStartup;
            tod.SetHour(e.h);
            float ms = (Time.realtimeSinceStartup - t0) * 1000f;
            yield return null;
            float lux = tod.GetZoneLux(living);
            float ceil = livingLight.Brightness * 300f;
            float factor = living.Data.luxFactor;
            if (tod.CurrentHour != e.h) bad.Add($"{e.h}시 CurrentHour {tod.CurrentHour}");
            if (sun != null)
            {
                float si = sun.enabled ? sun.intensity / baseI : 0f;
                if (Mathf.Abs(si - e.si) > 0.01f) bad.Add($"{e.h}시 햇빛 {si:F2} (기대 {e.si})");
                if (e.si > 0f)
                {
                    Vector3 eu = sun.transform.eulerAngles;
                    if (Mathf.Abs(Mathf.DeltaAngle(eu.x, Mathf.Max(e.el, 1f))) > 0.5f) bad.Add($"{e.h}시 고도 {eu.x:F1}° (기대 {e.el})");
                }
            }
            if (Mathf.Abs(lux - (e.lux * factor + ceil)) > 1f) bad.Add($"{e.h}시 거실 조도 {lux:F0}lx (기대 {e.lux * factor + ceil:F0})");
            if (ms > 1000f) bad.Add($"{e.h}시 적용 {ms:F0}ms");
            if (Mathf.Abs(RenderSettings.ambientIntensity - ambient12) > 0.001f) bad.Add($"{e.h}시 실내 환경광 세기 변함 (로봇팔 색 고정 깨짐)");
            info.Add($"{e.h:00}시 {ms:F0}ms·{lux:F0}lx");
        }
        tod.SetHour(12);
        Add("TC-48", "시각 5개 A-8 값·1초 이내·조도 계산", bad.Count == 0, bad.Count == 0 ? string.Join(", ", info) : string.Join(" / ", bad));
    }

    // ── 옵션 (FR-29, FR-31) ─────────────────────

    List<OptionPart> PartsOf(PartType p) => FindObjectsByType<OptionPart>().Where(x => x.part == p).ToList();

    static Material SlotMaterial(OptionPart p)
    {
        FieldInfo f = typeof(OptionPart).GetField("slots", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null || !(f.GetValue(p) is System.Collections.IList list) || list.Count == 0) return null;
        object s = list[0];
        var r = s.GetType().GetField("renderer").GetValue(s) as Renderer;
        int i = (int)s.GetType().GetField("index").GetValue(s);
        return r != null && i < r.sharedMaterials.Length ? r.sharedMaterials[i] : null;
    }

    int ExpectedCost()
    {
        int sum = 0;
        foreach (PartType p in Enum.GetValues(typeof(PartType))) { OptionItem s = config.GetSelected(p); if (s != null) sum += s.extraCost; }
        return sum;
    }

    IEnumerator Options()
    {
        config.ResetToDefaults();
        yield return null;
        var defaultMat = new Dictionary<OptionPart, Material>();
        foreach (OptionPart op in FindObjectsByType<OptionPart>()) defaultMat[op] = SlotMaterial(op);

        int n = 1;
        foreach (PartType part in Enum.GetValues(typeof(PartType)))
        {
            List<OptionItem> opts = config.Catalog.GetOptions(part);
            if (opts == null || opts.Count == 0) continue;
            List<OptionPart> parts = PartsOf(part);
            var bad = new List<string>();
            if (parts.Count == 0) bad.Add("씬에 OptionPart 없음");
            OptionItem other = config.GetSelected(part == PartType.Wall ? PartType.Floor : PartType.Wall);
            foreach (OptionItem it in opts)
            {
                float t0 = Time.realtimeSinceStartup;
                config.Select(it);
                float ms = (Time.realtimeSinceStartup - t0) * 1000f;
                yield return null;
                if (config.GetSelected(part) != it) bad.Add($"{it.displayName} 선택 안 됨");
                foreach (OptionPart op in parts)
                {
                    // 기본 옵션은 처음부터 적용된 상태라 Current가 비어 있을 수 있음 → 기본이면 통과
                    if (op.Current != it && !(op.Current == null && it.isDefault)) bad.Add($"{op.name} 적용 안 됨({it.displayName})");
                    Material m = SlotMaterial(op);
                    if (!it.useColor && it.material != null && m != it.material) bad.Add($"{op.name} 재질 {m?.name} (기대 {it.material.name})");
                    if (it.useColor && it.material == null && m != null && opts.Any(o => o.material == m)) bad.Add($"{op.name} {it.displayName}인데 재질형 재질({m.name})이 남음");
                }
                if (config.GetTotalCost() != ExpectedCost()) bad.Add($"합계 {config.GetTotalCost()} ≠ {ExpectedCost()}");
                if (ms > 1000f) bad.Add($"{it.displayName} {ms:F0}ms");
            }
            OptionItem otherNow = config.GetSelected(part == PartType.Wall ? PartType.Floor : PartType.Wall);
            if (other != otherNow) bad.Add("다른 파트 선택이 바뀜");
            string id = $"TC-47-{n:00}";
            Add(id, $"{part.ToKorean()} 선택지 {opts.Count}개 적용", bad.Count == 0, bad.Count == 0 ? "반영·이전 해제·다른 파트 유지·비용 갱신" : string.Join(" / ", bad.Distinct().Take(4)));
            n++;
        }

        // TC-49: 여러 파트를 바꾼 합계 = A-7 추가 비용 합, 모두 기본이면 0
        foreach (string id in new[] { "floor_navy", "wall_greige", "counter_darkstone", "sofa_navy", "rug_beige" }) config.Select(id);
        yield return null;
        int expected = new[] { "floor_navy", "wall_greige", "counter_darkstone", "sofa_navy", "rug_beige" }.Sum(id => config.Catalog.Find(id)?.extraCost ?? 0)
                       + ExpectedCostExcept(new[] { PartType.Floor, PartType.Wall, PartType.Countertop, PartType.SofaFabric, PartType.Rug });
        int mixed = config.GetTotalCost();
        config.ResetToDefaults();
        yield return null;
        int zero = config.GetTotalCost();
        var leftover = new List<string>();
        foreach (var kv in defaultMat)
            if (kv.Key != null && kv.Value != null)
            {
                Material now = SlotMaterial(kv.Key);
                bool isOptionMat = config.Catalog.Items.Any(o => o.material != null && o.material == now);
                if (isOptionMat) leftover.Add($"{kv.Key.name}({now.name})");
            }
        Add("TC-49", "추가 비용 합계", mixed == expected && zero == 0, $"5개 변경 {mixed:N0}원 (기대 {expected:N0}), 모두 기본 {zero:N0}원");
        Add("TC-34b", "[옵션 초기화] 모든 파트 기본·0원·원래 재질", zero == 0 && leftover.Count == 0 && AllDefault(), leftover.Count == 0 ? "모두 기본, 재질형 옵션 재질 남지 않음" : "남은 재질: " + string.Join(", ", leftover));
    }

    int ExpectedCostExcept(PartType[] changed)
    {
        int sum = 0;
        foreach (PartType p in Enum.GetValues(typeof(PartType)))
            if (!changed.Contains(p)) { OptionItem s = config.GetSelected(p); if (s != null) sum += s.extraCost; }
        return sum;
    }

    bool AllDefault()
    {
        foreach (PartType p in Enum.GetValues(typeof(PartType)))
        {
            OptionItem s = config.GetSelected(p);
            if (s != null && !s.isDefault) return false;
        }
        return true;
    }

    // ── 위치 초기화 (FR-26) ─────────────────────

    IEnumerator ResetKeepsOptions()
    {
        config.Select("wall_greige"); config.Select("sofa_navy");
        int cost = config.GetTotalCost();
        tod.SetHour(18);
        Teleport(PointIn(living));
        yield return new WaitForSeconds(0.5f);
        scenario.ResetScenario();
        yield return new WaitForSeconds(1.5f);
        bool keep = config.GetSelected(PartType.Wall)?.optionId == "wall_greige" && config.GetSelected(PartType.SofaFabric)?.optionId == "sofa_navy" && config.GetTotalCost() == cost;
        bool hour = tod.CurrentHour == 12;
        bool atEntrance = entrance.IsOccupied && !living.IsOccupied;
        bool idle = !task.IsRunning && task.Step == CoffeeStep.Idle;
        Add("TC-34a", "[위치 초기화] 옵션 유지, 나머지 처음 상태", keep && hour && atEntrance && idle, $"옵션 유지 {keep}, 12시 {hour}, 현관 {atEntrance}, 커피 대기 {idle}");
        config.ResetToDefaults();
        yield return null;
    }

    // ── 커피 로봇 (FR-13, FR-15, FR-25) ─────────

    Transform[] RackCups() => (typeof(CoffeeTask).GetField("rackCups", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(task) as Transform[]) ?? new Transform[0];

    IEnumerator WaitIdle(float timeout)
    {
        float t0 = Time.time;
        while (task.IsRunning && Time.time - t0 < timeout) yield return null;
    }

    bool Logged(DateTime since, string text) => EventLogger.Entries.Any(e => e.time >= since && e.message.Contains(text));

    IEnumerator Coffee()
    {
        scenario.ResetScenario();
        yield return new WaitForSeconds(2f);
        Transform[] cups = RackCups();
        var home = cups.Where(c => c != null).ToDictionary(c => c, c => c.position);

        // TC-16 / TC-33: 빈 받침대에서 1회
        DateTime mark = DateTime.Now; float t0 = Time.time;
        task.Run();
        yield return null;
        if (!task.IsRunning) { Add("TC-16", "커피 1회", false, "시작 안 됨 (CanStart false)"); yield break; }
        yield return WaitIdle(90f);
        float first = Time.time - t0;
        var seq = EventLogger.Entries.Where(e => e.time >= mark).Select(e => e.message).ToList();
        int iStart = seq.FindIndex(m => m.Contains("커피 만들기 시작"));
        int iState = seq.FindIndex(m => m.Contains("로봇팔 상태 변경"));
        int iDone = seq.FindIndex(m => m.Contains("커피 완료"));
        Add("TC-33", "커피 기록 순서", iStart >= 0 && iState > iStart && iDone > iState, $"시작 {iStart}, 상태 변경 {iState}, 완료 {iDone}번째 기록");

        // 두 번째: 지난 잔 정리 포함
        mark = DateTime.Now; t0 = Time.time;
        task.Run();
        yield return null;
        yield return WaitIdle(120f);
        float second = Time.time - t0;
        bool sink = Logged(mark, "지난 잔을 싱크대에 넣음") && Logged(mark, "커피 완료");
        Add("TC-16", "커피 시연 시간·싱크대", first <= 30f && second <= 60f && sink, $"빈 받침대 {first:F1}초(≤30), 정리 포함 {second:F1}초(≤60), 싱크대 {sink}");

        // TC-16-P: 추출구에 도착한 순간 일시정지 → 3초 동안 진행 안 됨 → 이어서
        mark = DateTime.Now;
        task.Run();
        float tw = Time.time;
        while (task.Step != CoffeeStep.WaitingBrew && task.IsRunning && Time.time - tw < 60f) yield return null;
        task.SetPaused(true);
        DateTime pausedAt = DateTime.Now;
        CoffeeStep stepAtPause = task.Step;
        Vector3 tip = arm.Tip.position;
        yield return new WaitForSeconds(3f);
        bool frozen = task.Step == stepAtPause && Vector3.Distance(tip, arm.Tip.position) < 0.01f && !Logged(pausedAt, "커피 추출 끝");
        task.SetPaused(false);
        yield return WaitIdle(90f);
        bool resumed = Logged(mark, "커피 작업 이어서 진행") && Logged(mark, "커피 완료");
        Add("TC-16-P", "일시정지(추출 직전)·이어서", stepAtPause == CoffeeStep.WaitingBrew && frozen && resumed, $"멈춘 단계 {stepAtPause.ToKorean()}, 3초 동안 멈춤 {frozen}, 이어서 완료 {resumed}");

        // TC-16-S: 컵을 든 채 정지 → 집은 자리에 내려놓고 홈
        mark = DateTime.Now;
        task.Run();
        tw = Time.time;
        while (!(arm.IsHolding && task.Step == CoffeeStep.Carrying) && task.IsRunning && Time.time - tw < 60f) yield return null;
        // 이번에 든 컵 = 보관대 컵 중 집은 것(받침대 잔이면 싱크대로 가는 중)
        yield return new WaitForSeconds(0.8f);
        task.Stop();
        yield return WaitIdle(60f);
        bool placed = Logged(mark, "들고 있던 컵을 제자리에 내려놓음");
        bool floating = cups.Any(c => c != null && c.position.y > home[c].y + 0.05f && Vector3.Distance(c.position, arm.Tip.position) < 0.15f);
        Add("TC-16-S", "컵을 든 채 정지", placed && !arm.IsHolding && task.Step == CoffeeStep.Idle && !floating, $"제자리 기록 {placed}, 들고 있음 {arm.IsHolding}, 단계 {task.Step.ToKorean()}, 공중 컵 {floating}");

        // TC-18: 팔이 안 닿는 컵 → 작업 실패 → 보관대·홈
        scenario.ResetScenario();
        yield return new WaitForSeconds(2f);
        mark = DateTime.Now;
        MethodInfo test = typeof(CoffeeTask).GetMethod("TestUnreachable", BindingFlags.NonPublic | BindingFlags.Instance);
        if (test == null) { Add("TC-18", "도달 불가", false, "TestUnreachable 없음"); yield break; }
        test.Invoke(task, null);
        yield return null;
        yield return WaitIdle(60f);
        bool failed = Logged(mark, "작업 실패");
        bool back = cups.All(c => c == null || Vector3.Distance(c.position, home[c]) < 0.01f);
        Add("TC-18", "팔이 안 닿는 컵 → 작업 실패", failed && back && task.Step == CoffeeStep.Idle && !arm.IsHolding, $"작업 실패 기록 {failed}, 컵 보관대 복귀 {back}, 단계 {task.Step.ToKorean()}");
    }

    // ── 결과 ───────────────────────────────────

    void Finish()
    {
        int pass = results.Count(r => r.pass);
        var sb = new StringBuilder();
        sb.AppendLine($"QA 자동 점검 {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ({pass}/{results.Count} 통과)");
        sb.AppendLine($"Unity {Application.unityVersion} / {SystemInfo.operatingSystem} / {(Application.isEditor ? "에디터" : "빌드")}");
        sb.AppendLine();
        foreach (Result r in results) sb.AppendLine($"{(r.pass ? "통과" : "실패")}\t{r.tc}\t{r.name}\t{r.detail}");
        string path = "";
        try
        {
            Directory.CreateDirectory(EvidenceCapture.Folder);
            path = Path.Combine(EvidenceCapture.Folder, $"{DateTime.Now:yyyyMMdd_HHmmss}_QA자동점검.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex) { Debug.LogWarning("[QA] 결과 파일 저장 실패: " + ex.Message); }
        Debug.Log($"[QA] 끝: {pass}/{results.Count} 통과 → {path}\n{sb}");
        HomeEvents.Log("QA", $"QA 자동 점검 끝 ({pass}/{results.Count} 통과)");
        Running = false;
        Destroy(gameObject, 1f);
    }
}
