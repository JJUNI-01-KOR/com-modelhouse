using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 10/13 권오민 작업 (WBS 5.01, 5.04, 5.08) — COM → 전체 세팅·점검에 포함
///   씬 연결 점검 (5.01)   : 회귀 점검 전에 Inspector 연결이 빠진 곳을 한 번에 찾는다 (씬은 안 바꿈)
///   화면 품질 세팅 (5.04) : NFR-10 Forward+, Baked Indirect, 구역별 반사 프로브, Tonemapping·Bloom·SSAO
///   UI 점검 (5.08)        : 1920×1080 겹침, 화면 문구 목록, 3클릭 경로
/// 결과는 Console과 C:\Unity\COM_Evidence\ 아래 txt로 남는다 (5.10 증거로 사용)
/// </summary>
internal static class COMDay13
{
    // ================================================================
    // 공통: 결과 모아서 Console + 파일로
    // ================================================================
    internal class Report
    {
        readonly string title;
        readonly StringBuilder sb = new StringBuilder();
        public int Ok, Fail, Info;

        public Report(string title)
        {
            this.title = title;
            sb.AppendLine($"[{title}] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
        }

        public void Section(string name) { sb.AppendLine(); sb.AppendLine($"■ {name}"); }
        public void Check(bool ok, string msg) { sb.AppendLine((ok ? "  ✅ " : "  ❌ ") + msg); if (ok) Ok++; else Fail++; }
        public void Note(string msg) { sb.AppendLine("  · " + msg); Info++; }

        public string Finish(string fileTag)
        {
            sb.AppendLine();
            sb.AppendLine($"합계: 통과 {Ok} / 문제 {Fail}");
            string text = sb.ToString();

            string path = null;
            try
            {
                string dir = EvidenceDir();
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{fileTag}.txt");
                File.WriteAllText(path, text, new UTF8Encoding(true));
            }
            catch (Exception e) { Debug.LogWarning($"[COM] 결과 파일 저장 실패: {e.Message}"); }

            if (Fail > 0) Debug.LogWarning(text); else Debug.Log(text);
            return path;
        }
    }

    /// <summary>프로젝트 폴더 옆 COM_Evidence (git 밖이라 커밋·압축에 안 섞임)</summary>
    public static string EvidenceDir()
        => Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "COM_Evidence");

    static T Ref<T>(UnityEngine.Object target, string field) where T : UnityEngine.Object
    {
        if (target == null) return null;
        SerializedProperty p = new SerializedObject(target).FindProperty(field);
        return p != null ? p.objectReferenceValue as T : null;
    }

    static string PathOf(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    // ================================================================
    // 10. 씬 연결 점검 (5.01)
    // ================================================================
    internal static void CheckScene(Report r)
    {
        int interactable = LayerMask.NameToLayer("Interactable");

        r.Section("플레이어 (FR-01)");
        PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        r.Check(player != null, "PlayerController 있음");
        if (player != null)
        {
            r.Check(player.GetComponent<CharacterController>() != null, "CharacterController 있음");
            r.Check(Ref<Transform>(player, "cameraPivot") != null, "Camera Pivot 연결");
            Transform spawn = Ref<Transform>(player, "spawnPoint");
            r.Check(spawn != null, "Spawn Point 연결");
        }

        r.Section("클릭 (FR-03)");
        CursorPicker picker = UnityEngine.Object.FindAnyObjectByType<CursorPicker>();
        r.Check(picker != null, "CursorPicker 있음 (Main Camera)");
        if (picker != null)
        {
            var mask = new SerializedObject(picker).FindProperty("interactableMask");
            r.Check(interactable >= 0 && mask != null && (mask.intValue & (1 << interactable)) != 0, "Interactable Mask에 Interactable 레이어 체크");
        }

        r.Section("UI 입력");
        var es = UnityEngine.EventSystems.EventSystem.current != null
            ? UnityEngine.EventSystems.EventSystem.current
            : UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
        r.Check(es != null, "EventSystem 있음");
        if (es != null)
        {
            bool newModule = false, oldModule = false;
            foreach (var c in es.GetComponents<MonoBehaviour>())
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                if (n == "InputSystemUIInputModule") newModule = true;
                if (n == "StandaloneInputModule") oldModule = true;
            }
            r.Check(newModule && !oldModule, "Input System UI Input Module 사용 (Standalone 없음)");
        }

        r.Section("구역 (FR-02, FR-04, FR-05)");
        ZoneVolume[] zones = UnityEngine.Object.FindObjectsByType<ZoneVolume>();
        int realZones = 0;
        foreach (ZoneVolume z in zones)
        {
            if (z.IsSensorOnly) { r.Note($"{z.name}: 감지 전용 영역"); continue; }
            realZones++;
            int boxes = 0;
            foreach (Collider c in z.GetComponentsInChildren<Collider>(true)) if (c.isTrigger) boxes++;
            r.Check(z.Data != null && boxes > 0, $"{z.name}: ZoneData {(z.Data != null ? z.Data.displayName : "없음")}, 트리거 박스 {boxes}개");
            if (z.Data != null)
                r.Check(!string.IsNullOrEmpty(z.Data.direction) && !string.IsNullOrEmpty(z.Data.feature) && z.Data.area > 0,
                        $"{z.Data.displayName}: 면적·방향·특징 입력됨 (FR-02)");
        }
        r.Check(realZones == 4, $"구역 4개 (현재 {realZones}개)");

        r.Section("옵션 (FR-03, FR-29, FR-31)");
        var parts = new Dictionary<PartType, OptionPart>();
        foreach (OptionPart p in UnityEngine.Object.FindObjectsByType<OptionPart>()) parts[p.part] = p;
        ConfigurationState config = UnityEngine.Object.FindAnyObjectByType<ConfigurationState>();
        OptionCatalog catalog = Ref<OptionCatalog>(config, "catalog");
        r.Check(config != null, "ConfigurationState 있음");
        r.Check(catalog != null, "OptionCatalog 연결");
        foreach (PartType pt in Enum.GetValues(typeof(PartType)))
        {
            if (!parts.TryGetValue(pt, out OptionPart op)) { r.Check(false, $"{pt.ToKorean()}: OptionPart 없음"); continue; }
            var so = new SerializedObject(op);
            Material src = so.FindProperty("sourceMaterial").objectReferenceValue as Material;
            SerializedProperty rs = so.FindProperty("renderers");
            int count = 0, clickable = 0;
            for (int i = 0; i < rs.arraySize; i++)
            {
                Renderer rr = rs.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
                if (rr == null) continue;
                count++;
                if (rr.gameObject.layer == interactable && rr.GetComponent<Collider>() != null) clickable++;
            }
            bool auto = rs.arraySize == 0 && src != null;   // 비우면 씬 전체에서 재질로 찾음
            r.Check(src != null && (count > 0 || auto), $"{pt.ToKorean()}: 재질 {(src != null ? src.name : "없음")}, 대상 메쉬 {(auto ? "자동" : count.ToString())}");
            if (!auto) r.Check(clickable == count && count > 0, $"{pt.ToKorean()}: 클릭 가능(Interactable 레이어 + 충돌체) {clickable}/{count}");
            if (catalog != null)
            {
                var opts = catalog.GetOptions(pt);
                bool hasDefault = opts.Exists(o => o.isDefault);
                r.Check(hasDefault && opts.Count >= 2, $"{pt.ToKorean()}: 카탈로그 옵션 {opts.Count}개 (기본 포함 {hasDefault})");
            }
        }

        r.Section("화면 (FR-11, FR-25, FR-26, FR-30)");
        HUDController hud = UnityEngine.Object.FindAnyObjectByType<HUDController>(FindObjectsInactive.Include);
        r.Check(hud != null, "HUDController 있음");
        if (hud != null)
        {
            foreach (string f in new[] { "optionButton", "logButton", "resetButton", "optionPanel", "logPanel", "costText", "config", "scenario" })
                r.Check(Ref<UnityEngine.Object>(hud, f) != null, $"HUD.{f} 연결");
        }
        OptionPanelUI opPanel = UnityEngine.Object.FindAnyObjectByType<OptionPanelUI>(FindObjectsInactive.Include);
        r.Check(opPanel != null && Ref<UnityEngine.Object>(opPanel, "config") != null && Ref<UnityEngine.Object>(opPanel, "tabBar") != null && Ref<UnityEngine.Object>(opPanel, "listRoot") != null,
                "OptionPanelUI: config·tabBar·listRoot 연결");
        LogPanelUI logPanel = UnityEngine.Object.FindAnyObjectByType<LogPanelUI>(FindObjectsInactive.Include);
        r.Check(logPanel != null && Ref<UnityEngine.Object>(logPanel, "body") != null, "LogPanelUI: body 연결");
        ClockUI clock = UnityEngine.Object.FindAnyObjectByType<ClockUI>(FindObjectsInactive.Include);
        RectTransform row = Ref<RectTransform>(clock, "buttonRow");
        r.Check(clock != null && row != null && row.childCount == ClockUI.DefaultPresets.Length,
                $"ClockUI: 시각 버튼 {(row != null ? row.childCount : 0)}/{ClockUI.DefaultPresets.Length}개");
        ZoneInfoUI zoneInfo = UnityEngine.Object.FindAnyObjectByType<ZoneInfoUI>(FindObjectsInactive.Include);
        r.Check(zoneInfo != null && zoneInfo.GetComponent<CanvasGroup>() != null, "ZoneInfoUI + CanvasGroup");
        r.Check(UnityEngine.Object.FindAnyObjectByType<ScenarioManager>() != null, "ScenarioManager 있음");

        r.Section("다른 담당 (참고)");
        r.Note($"RobotArm {UnityEngine.Object.FindObjectsByType<RobotArm>().Length}개, ITimeOfDay 구현 {CountTimeSources()}개 (0이면 시각 버튼 눌러도 햇빛 그대로)");
    }

    static int CountTimeSources()
    {
        int n = 0;
        foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>())
            if (mb is ITimeOfDay) n++;
        return n;
    }

    // ================================================================
    // 11. 화면 품질 세팅 (5.04, NFR-10, TC-52)
    // ================================================================
    const string SettingsFolder = "Assets/_Project/Settings";
    const string LightingPath = SettingsFolder + "/MainScene.lighting";

    internal static void SetupQuality(Report r)
    {
        Undo.SetCurrentGroupName("COM 5.04 화면 품질");
        int group = Undo.GetCurrentGroup();

        // 1) 렌더링 경로 Forward+ / SSAO
        r.Section("렌더러 (Forward+, SSAO)");
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/Settings")) continue;   // 패키지 샘플 등은 건너뜀
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) continue;
            if (data.renderingMode != UnityEngine.Rendering.Universal.RenderingMode.ForwardPlus)
            {
                Undo.RecordObject(data, "COM 5.04");
                data.renderingMode = UnityEngine.Rendering.Universal.RenderingMode.ForwardPlus;
                EditorUtility.SetDirty(data);
                r.Note($"{data.name}: 렌더링 경로를 Forward+로 변경");
            }
            r.Check(data.renderingMode == UnityEngine.Rendering.Universal.RenderingMode.ForwardPlus, $"{data.name}: Forward+");

            ScriptableRendererFeature ssao = null;
            foreach (var f in data.rendererFeatures)
                if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") ssao = f;
            if (ssao != null && !ssao.isActive)
            {
                Undo.RecordObject(ssao, "COM 5.04");
                ssao.SetActive(true);
                EditorUtility.SetDirty(ssao);
                r.Note($"{data.name}: SSAO 켬");
            }
            r.Check(ssao != null && ssao.isActive, $"{data.name}: SSAO (Renderer Feature)" + (ssao == null ? " — 없음: PC_Renderer → Add Renderer Feature → Screen Space Ambient Occlusion" : ""));
        }

        // 2) 후처리 Volume: Tonemapping, Bloom
        r.Section("후처리 (Tonemapping, Bloom)");
        Volume global = null;
        foreach (Volume v in UnityEngine.Object.FindObjectsByType<Volume>())
            if (v.isGlobal) { global = v; break; }
        r.Check(global != null && global.sharedProfile != null, $"Global Volume + 프로필 {(global != null && global.sharedProfile != null ? global.sharedProfile.name : "없음")}");
        if (global != null && global.sharedProfile != null)
        {
            VolumeProfile prof = global.sharedProfile;
            Undo.RecordObject(prof, "COM 5.04");

            if (!prof.TryGet(out Tonemapping tone)) { tone = prof.Add<Tonemapping>(true); AssetDatabase.AddObjectToAsset(tone, prof); r.Note("Tonemapping 추가"); }
            tone.active = true;
            if (!tone.mode.overrideState || tone.mode.value == TonemappingMode.None) tone.mode.Override(TonemappingMode.ACES);
            r.Check(tone.active && tone.mode.value != TonemappingMode.None, $"Tonemapping {tone.mode.value}");

            if (!prof.TryGet(out Bloom bloom)) { bloom = prof.Add<Bloom>(true); AssetDatabase.AddObjectToAsset(bloom, prof); r.Note("Bloom 추가"); }
            bloom.active = true;
            if (!bloom.intensity.overrideState || bloom.intensity.value <= 0f) bloom.intensity.Override(0.3f);
            if (!bloom.threshold.overrideState) bloom.threshold.Override(1f);
            r.Check(bloom.active && bloom.intensity.value > 0f, $"Bloom 세기 {bloom.intensity.value:0.##}, 임계값 {bloom.threshold.value:0.##}");

            EditorUtility.SetDirty(prof);
        }
        Camera cam = Camera.main;
        if (cam != null)
        {
            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (camData != null && !camData.renderPostProcessing)
            {
                Undo.RecordObject(camData, "COM 5.04");
                camData.renderPostProcessing = true;
                EditorUtility.SetDirty(camData);
                r.Note("Main Camera: Post Processing 체크 켬");
            }
            r.Check(camData != null && camData.renderPostProcessing, "Main Camera: Post Processing 켜짐");
        }

        // 3) 조명: Baked Indirect + 햇빛 Mixed (시각 버튼으로 바뀌어야 하므로 실시간 성분 유지)
        r.Section("조명 굽기 (Mixed 조명, Baked Indirect)");
        EnsureFolder(SettingsFolder);
        LightingSettings ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
        if (ls == null)
        {
            // 지금 씬은 에셋 데모 씬의 설정을 같이 쓰고 있어서, MainScene 전용으로 복사본을 만든다
            ls = Lightmapping.TryGetLightingSettings(out LightingSettings current) && current != null
                ? UnityEngine.Object.Instantiate(current)
                : new LightingSettings();
            ls.name = "MainScene";
            AssetDatabase.CreateAsset(ls, LightingPath);
            r.Note($"MainScene 전용 라이팅 설정 생성: {LightingPath}");
        }
        Undo.RecordObject(ls, "COM 5.04");
        ls.bakedGI = true;
        ls.realtimeGI = false;
        ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
        EditorUtility.SetDirty(ls);
        Lightmapping.lightingSettings = ls;
        r.Check(ls.mixedBakeMode == MixedLightingMode.IndirectOnly, "Mixed Lighting Mode = Baked Indirect");

        foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>())
        {
            if (l.type == LightType.Directional && l.lightmapBakeType != LightmapBakeType.Mixed)
            {
                Undo.RecordObject(l, "COM 5.04");
                l.lightmapBakeType = LightmapBakeType.Mixed;
                EditorUtility.SetDirty(l);
                r.Note($"{l.name}: Mixed로 변경");
            }
            r.Note($"조명 {l.name}: {l.type}, {l.lightmapBakeType}");
        }

        // 4) 반사 프로브: 구역마다 1개 이상, Baked (Realtime은 매 프레임 다시 찍어 FPS를 많이 먹음)
        r.Section("반사 프로브 (구역마다 1개 이상)");
        ReflectionProbe[] probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>();
        foreach (ReflectionProbe p in probes)
        {
            // 거울 전용 프로브(COMMirrorProbes)는 직접 찍은 Custom 텍스처를 쓰므로 건드리지 않는다
            if (p.mode == ReflectionProbeMode.Custom)
            {
                r.Note($"{p.name}: Custom(거울 전용) → 그대로 둠");
                continue;
            }
            if (p.mode != ReflectionProbeMode.Baked)
            {
                Undo.RecordObject(p, "COM 5.04");
                p.mode = ReflectionProbeMode.Baked;
                EditorUtility.SetDirty(p);
                r.Note($"{p.name}: Realtime → Baked (NFR-01 FPS)");
            }
        }
        foreach (ZoneVolume z in UnityEngine.Object.FindObjectsByType<ZoneVolume>())
        {
            if (z.IsSensorOnly) continue;
            var inside = new List<string>();
            foreach (ReflectionProbe p in probes)
                foreach (Collider c in z.GetComponentsInChildren<Collider>(true))
                    if (c.isTrigger && c.bounds.Contains(p.transform.position)) { inside.Add(p.name); break; }
            r.Check(inside.Count > 0, $"{z.DisplayName}: 프로브 {inside.Count}개 ({string.Join(", ", inside)})");
        }

        Undo.CollapseUndoOperations(group);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ================================================================
    // 12. UI 점검 (5.08, NFR-07)
    // ================================================================
    static readonly Vector2 RefSize = new Vector2(1920f, 1080f);

    internal static void CheckUI(Report r)
    {
        GameObject ui = GameObject.Find("COM_UI");
        if (ui == null) { r.Check(false, "COM_UI 없음"); return; }

        // 1) 해상도: 1920×1080 기준으로 최상위 패널끼리 겹치는지
        r.Section("1920×1080 겹침");
        var scaler = ui.GetComponent<CanvasScaler>();
        r.Check(scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize && scaler.referenceResolution == RefSize,
                "Canvas Scaler: Scale With Screen Size 1920×1080");

        FixZoneInfoGap(ui.transform, r);

        var rects = new List<(string name, Rect rect)>();
        foreach (Transform child in ui.transform)
        {
            var rt = child as RectTransform;
            if (rt == null || rt.anchorMin != rt.anchorMax) continue;   // 화면 전체로 늘어나는 것은 제외
            rects.Add((child.name, ScreenRect(rt)));
        }
        foreach (var (name, rect) in rects)
            r.Check(rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= RefSize.x && rect.yMax <= RefSize.y,
                    $"{name}: 화면 안 ({rect.xMin:0},{rect.yMin:0}) ~ ({rect.xMax:0},{rect.yMax:0})");
        // 옵션 패널과 기록 패널은 같은 자리에 번갈아 열리므로 둘끼리는 겹쳐도 됨
        var together = new HashSet<string> { "OptionPanel", "LogPanel" };
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                if (together.Contains(rects[i].name) && together.Contains(rects[j].name)) continue;
                bool overlap = rects[i].rect.Overlaps(rects[j].rect);
                if (overlap) r.Check(false, $"{rects[i].name} ↔ {rects[j].name} 겹침");
            }
        r.Note("겹침 줄이 없으면 통과. Game 창 해상도를 1920x1080으로 놓고 눈으로도 한 번 확인");

        // 2) 화면 문구 목록 (SRS와 대조용)
        r.Section("화면 문구 (SRS 4.1 UI 표·FR-31 문구와 대조)");
        foreach (Text t in ui.GetComponentsInChildren<Text>(true))
        {
            string s = t.text.Replace("\n", " / ");
            if (!string.IsNullOrWhiteSpace(s)) r.Note($"{PathOf(t.transform).Replace("COM_UI/", "")}: \"{s}\"");
        }
        r.Note("탭 이름(실행 중 생성): " + string.Join(", ", Array.ConvertAll((PartType[])Enum.GetValues(typeof(PartType)), p => p.ToKorean())));
        var catalog = AssetDatabase.LoadAssetAtPath<OptionCatalog>("Assets/_Project/Data/OptionCatalog.asset");
        if (catalog != null)
            foreach (PartType p in Enum.GetValues(typeof(PartType)))
                r.Note($"{p.ToKorean()} 옵션: " + string.Join(", ", catalog.GetOptions(p).ConvertAll(o => $"{o.displayName}({o.CostLabel()})")));

        // 3) 3클릭 경로 (NFR-07: 1인칭 화면에서 클릭 3번 이내)
        r.Section("3클릭 경로 (NFR-07, TC-43)");
        r.Check(true, "옵션 변경 ①: 벽·소파 등 클릭(1) → 옵션 줄 클릭(2) = 2번");
        r.Check(true, "옵션 변경 ②: [옵션](1) → 파트 탭(2) → 옵션 줄(3) = 3번");
        r.Check(UnityEngine.Object.FindAnyObjectByType<RobotArm>() != null, "커피 만들기: 커피 로봇팔 클릭(1) = 1번");
        r.Check(true, "시각 바꾸기: 시각 버튼(1) = 1번");
        r.Check(true, "기록 보기: [기록](1) = 1번 / 초기화: [초기화](1) = 1번");
        r.Note("위 경로를 Play에서 실제로 눌러 보고 TC-43 결과에 적기");
    }

    /// <summary>점 앵커 RectTransform의 1920×1080 화면 좌표 (왼쪽 아래 원점)</summary>
    static Rect ScreenRect(RectTransform rt)
    {
        Vector2 anchor = new Vector2(rt.anchorMin.x * RefSize.x, rt.anchorMin.y * RefSize.y);
        Vector2 pivotPos = anchor + rt.anchoredPosition;
        Vector2 min = pivotPos - Vector2.Scale(rt.pivot, rt.sizeDelta);
        return new Rect(min, rt.sizeDelta);
    }

    /// <summary>구역 정보 창(위 가운데)이 옵션 패널(왼쪽)과 시각 패널(오른쪽) 사이 빈 곳에 오도록</summary>
    static void FixZoneInfoGap(Transform ui, Report r)
    {
        var zone = ui.Find("ZoneInfoPanel") as RectTransform;
        var option = ui.Find("OptionPanel") as RectTransform;
        var clock = ui.Find("Clock") as RectTransform;
        if (zone == null || option == null || clock == null) return;

        Rect o = ScreenRect(option), c = ScreenRect(clock), z = ScreenRect(zone);
        if (!z.Overlaps(o) && !z.Overlaps(c)) { r.Note("구역 정보 창: 이미 빈 곳에 있음"); return; }

        float left = o.xMax + 10f, right = c.xMin - 10f;
        float width = Mathf.Min(560f, right - left);
        if (width < 360f) { r.Check(false, "구역 정보 창: 옵션 패널과 시각 패널 사이가 너무 좁음 → 수동 배치 필요"); return; }

        Undo.RecordObject(zone, "COM 5.08");
        zone.anchorMin = zone.anchorMax = new Vector2(0f, 1f);
        zone.pivot = new Vector2(0.5f, 1f);
        zone.anchoredPosition = new Vector2((left + right) * 0.5f, -20f);
        zone.sizeDelta = new Vector2(width, zone.sizeDelta.y);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
        r.Note($"구역 정보 창: 옵션 패널과 겹쳐서 가운데 빈 곳으로 이동 (폭 {width:0}) — 씬 저장 필요");
    }
}
