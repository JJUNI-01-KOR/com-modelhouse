using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10/12 작업(권오민) 씬 세팅 — COM → 전체 세팅·점검에 포함 (Ctrl+Z로 되돌리기 가능)
///   4.05 그림 액자, 4.06 화분  : Option_Painting / Option_Plant (색상형 OptionPart)
///   4.07 옵션 목록·가격·선택   : COM_System(ConfigurationState + ScenarioManager) 만들고 패널·HUD 연결
///   4.08, 4.11 시각 버튼       : 화면 우측 상단 시각 버튼 5개 (ClockUI, SRS v2.4)
///   4.13 기록 패널             : LogPanel을 LogPanelUI + 스크롤 목록으로 교체
///   4.18 초기화                : HUD [초기화] → ScenarioManager
/// </summary>
public static class COMSetup1012
{
    const string CatalogPath = "Assets/_Project/Data/OptionCatalog.asset";
    const string InteractableLayer = "Interactable";
    const float PanelWidth = 600f;   // 탭 7개가 들어가도록

    /// <summary>COM → 전체 세팅·점검에서 호출. 몇 번 실행해도 결과가 같다 (이미 있는 패널은 그대로 둠).</summary>
    internal static void Setup()
    {
        GameObject ui = GameObject.Find("COM_UI");
        if (ui == null)
        {
            Debug.LogWarning("[COM] COM_UI가 씬에 없어서 10/12 세팅을 건너뜀");
            return;
        }
        Undo.SetCurrentGroupName("COM 10-12 세팅");
        int undoGroup = Undo.GetCurrentGroup();

        // 1) 옵션 카탈로그 (부록 A-7)
        OptionCatalog catalog = AssetDatabase.LoadAssetAtPath<OptionCatalog>(CatalogPath);
        if (catalog == null || catalog.Items.Count == 0)
        {
            OptionCatalogBuilder.Build();
            catalog = AssetDatabase.LoadAssetAtPath<OptionCatalog>(CatalogPath);
        }

        // 2) 그림 액자·화분 대상 (FR-29-06, 07)
        // 에셋 재질 구성 (Prefabs 확인): sm_painting = m_black(틀) + m_details(그림) + m_white
        //                               sm_plant_pot / sm_plant_fern = m_details(흙·잎) + m_white(화분 몸통)
        // 검은 틀은 색을 곱해도 검정이라 그림(m_details)의 색감을, 화분은 흰 몸통(m_white)을 바꾼다.
        EnsureMeshColorPart("Option_Painting", PartType.Painting, "sm_painting", "m_details");
        // 화분: sm_plant_pot·sm_plant_fern·sm_pot 몸통 = m_white (FBX 분석: 흙 윗면 m_details, 잎 m_fern은 안 바뀜)
        EnsureMeshColorPart("Option_Plant", PartType.Plant, "sm_plant_|sm_pot", "m_white", findAtRuntime: true);

        // 3) 시스템 오브젝트
        GameObject sys = GameObject.Find("COM_System");
        if (sys == null)
        {
            sys = new GameObject("COM_System");
            Undo.RegisterCreatedObjectUndo(sys, "COM 10-12");
        }
        ConfigurationState config = GetOrAdd<ConfigurationState>(sys);
        SetRef(config, "catalog", catalog);
        ScenarioManager scenario = Object.FindAnyObjectByType<ScenarioManager>();
        if (scenario == null) scenario = GetOrAdd<ScenarioManager>(sys);

        // 4) 옵션 패널 (탭 7개 → 폭 넓히기) + HUD 연결
        OptionPanelUI optionPanel = Object.FindAnyObjectByType<OptionPanelUI>(FindObjectsInactive.Include);
        HUDController hud = Object.FindAnyObjectByType<HUDController>(FindObjectsInactive.Include);
        if (optionPanel != null)
        {
            SetRef(optionPanel, "config", config);
            Widen(optionPanel.transform, "Tabs", "List");
        }

        // 5) 기록 패널
        LogPanelUI logPanel = BuildLogPanel(ui.transform);

        // 6) 시각 버튼
        BuildClock(ui.transform);

        if (hud != null)
        {
            SetRef(hud, "config", config);
            SetRef(hud, "scenario", scenario);
            if (logPanel != null) SetRef(hud, "logPanel", logPanel);
        }

        Undo.CollapseUndoOperations(undoGroup);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sys.scene);
        Debug.Log("[COM] 10/12 세팅 완료: 액자·화분 대상, COM_System, 옵션 패널·HUD 연결, 기록 패널, 시각 버튼");
    }

    // ------------------------------------------------------------
    // 메쉬 이름으로 대상을 찾고, 그 메쉬들 중 matName 재질을 쓰는 칸만 색을 바꾼다.
    // (같은 m_white를 쓰는 다른 가구는 renderers에 안 들어가므로 안 바뀜)
    static void EnsureMeshColorPart(string objName, PartType type, string meshPrefix, string matName, bool findAtRuntime = false)
    {
        string[] prefixes = meshPrefix.Split('|');
        var meshes = new List<Renderer>();
        var mats = new List<Material>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>())
        {
            if (!System.Array.Exists(prefixes, p => r.name.StartsWith(p))) continue;
            meshes.Add(r);
            foreach (Material m in r.sharedMaterials)
                if (m != null && !mats.Contains(m)) mats.Add(m);
        }
        if (meshes.Count == 0)
        {
            Debug.LogWarning($"[COM] {objName}: 이름이 '{meshPrefix}'로 시작하는 메쉬가 씬에 없음 → 건너뜀");
            return;
        }

        Material chosen = mats.Find(m => m.name == matName);
        if (chosen == null)
        {
            var names = new List<string>();
            foreach (Material m in mats) names.Add(m.name);
            Debug.LogWarning($"[COM] {objName}: '{matName}' 재질이 '{meshPrefix}' 메쉬에 없음 (있는 것: {string.Join(", ", names)}) → 건너뜀");
            return;
        }

        GameObject go = GameObject.Find(objName);
        if (go == null)
        {
            go = new GameObject(objName);
            Undo.RegisterCreatedObjectUndo(go, "COM 10-12");
        }
        OptionPart part = GetOrAdd<OptionPart>(go);
        var so = new SerializedObject(part);

        int layer = LayerMask.NameToLayer(InteractableLayer);
        var targets = new List<Renderer>();
        foreach (Renderer r in meshes)
        {
            if (System.Array.IndexOf(r.sharedMaterials, chosen) < 0) continue;
            targets.Add(r);
            if (layer >= 0) { Undo.RecordObject(r.gameObject, "COM 10-12"); r.gameObject.layer = layer; }
            if (r.GetComponent<Collider>() == null && r.GetComponent<MeshFilter>() != null)
                Undo.AddComponent<MeshCollider>(r.gameObject);
        }

        so.FindProperty("part").enumValueIndex = (int)type;
        so.FindProperty("sourceMaterial").objectReferenceValue = chosen;
        so.FindProperty("clickWholeRenderer").boolValue = true;   // 그림·흙 부분을 눌러도 열림
        SerializedProperty arr = so.FindProperty("renderers");
        SerializedProperty pre = so.FindProperty("namePrefixes");
        if (findAtRuntime)
        {
            arr.arraySize = 0;                 // 목록 대신 Play 시작 때 이름으로 찾음
            pre.stringValue = meshPrefix;
        }
        else
        {
            arr.arraySize = targets.Count;
            for (int i = 0; i < targets.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            pre.stringValue = "";
        }
        so.ApplyModifiedProperties();

        var meshNames = new List<string>();
        foreach (Renderer r in targets) meshNames.Add(r.name);
        Debug.Log($"[COM] {objName}: 색 바꿀 재질 = {chosen.name}, 메쉬 {targets.Count}개 ({string.Join(", ", meshNames)})", go);
    }

    // ------------------------------------------------------------
    static LogPanelUI BuildLogPanel(Transform canvas)
    {
        Transform t = canvas.Find("LogPanel");
        if (t == null) { Debug.LogWarning("[COM] LogPanel이 없음 → 건너뜀"); return null; }
        GameObject go = t.gameObject;

        // 이미 세팅돼 있으면 그대로 (손으로 바꾼 위치·크기 유지)
        LogPanelUI existing = go.GetComponent<LogPanelUI>();
        if (existing != null && new SerializedObject(existing).FindProperty("body").objectReferenceValue != null)
            return existing;

        // 닫기 버튼은 살린다
        Button close = t.Find("Btn_Close") != null ? t.Find("Btn_Close").GetComponent<Button>() : null;

        // PanelUI → LogPanelUI 교체
        foreach (PanelUI p in go.GetComponents<PanelUI>())
            if (!(p is LogPanelUI)) Undo.DestroyObjectImmediate(p);
        LogPanelUI log = GetOrAdd<LogPanelUI>(go);

        // 예전 자리 표시 글자와 이전 스크롤 지우기
        foreach (string n in new[] { "Body", "Scroll" })
        {
            Transform old = t.Find(n);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        var rt = (RectTransform)t;
        rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.x, PanelWidth), rt.sizeDelta.y);

        // ScrollRect ─ Viewport(Mask) ─ Content(Text + ContentSizeFitter)
        Image scrollBg = UIFactory.CreatePanel(t, "Scroll", new Color(0f, 0f, 0f, 0.25f));
        Undo.RegisterCreatedObjectUndo(scrollBg.gameObject, "COM 10-12");
        RectTransform srt = scrollBg.rectTransform;
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = new Vector2(10f, 10f); srt.offsetMax = new Vector2(-10f, -60f);
        var scroll = scrollBg.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        Image viewport = UIFactory.CreatePanel(scrollBg.transform, "Viewport", Color.white);
        UIFactory.Stretch(viewport.rectTransform, 6f);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        Text body = UIFactory.CreateText(viewport.transform, "Body", "", 16, TextAnchor.UpperLeft);
        RectTransform brt = body.rectTransform;
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(0f, 0f);
        body.lineSpacing = 1.1f;
        var fitter = body.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport.rectTransform;
        scroll.content = brt;

        var so = new SerializedObject(log);
        so.FindProperty("body").objectReferenceValue = body;
        so.FindProperty("scroll").objectReferenceValue = scroll;
        so.FindProperty("closeButton").objectReferenceValue = close;
        so.ApplyModifiedProperties();
        return log;
    }

    // ------------------------------------------------------------
    static void BuildClock(Transform canvas)
    {
        Transform old = canvas.Find("Clock");
        if (old != null)
        {
            // 이미 시각 버튼 5개가 있으면 그대로 (손으로 바꾼 위치·크기 유지)
            ClockUI cu = old.GetComponent<ClockUI>();
            var oldRow = cu != null ? new SerializedObject(cu).FindProperty("buttonRow").objectReferenceValue as RectTransform : null;
            if (oldRow != null && oldRow.childCount == ClockUI.DefaultPresets.Length) return;
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        // 바탕 (우측 상단, UI-09): 제목 + 시각 버튼 5개 한 줄 + 조도
        Image bg = UIFactory.CreatePanel(canvas, "Clock", UIFactory.PanelColor);
        Undo.RegisterCreatedObjectUndo(bg.gameObject, "COM 10-12");
        bg.raycastTarget = false;
        RectTransform root = bg.rectTransform;
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);
        root.anchoredPosition = new Vector2(-20f, -20f);
        root.sizeDelta = new Vector2(520f, 150f);

        Text title = UIFactory.CreateText(root, "Title", "시간대", 18);
        UIFactory.PlaceTopLeft(title.rectTransform, new Vector2(14f, -8f), new Vector2(200f, 28f));

        RectTransform row = UIFactory.CreateRect(root, "Buttons");
        UIFactory.PlaceTopLeft(row, new Vector2(10f, -40f), new Vector2(500f, 64f));
        UIFactory.AddHorizontal(row.gameObject, 6f, 0);

        Text lux = UIFactory.CreateText(root, "Lux", "현재 구역 조도 - lx", 17, TextAnchor.MiddleCenter);
        RectTransform lrt = lux.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 0f); lrt.pivot = new Vector2(0.5f, 0f);
        lrt.anchoredPosition = new Vector2(0f, 8f);
        lrt.sizeDelta = new Vector2(-16f, 34f);

        // 시각 버튼 5개를 미리 만들어 둔다 (편집 화면에서도 보임). Play 때 ClockUI가 이름(Btn_06 …)으로 찾아 연결
        foreach (ClockUI.Preset p in ClockUI.DefaultPresets)
            ClockUI.CreateButton(row, p);

        ClockUI clock = bg.gameObject.AddComponent<ClockUI>();
        var so = new SerializedObject(clock);
        so.FindProperty("buttonRow").objectReferenceValue = row;
        so.FindProperty("luxText").objectReferenceValue = lux;
        so.ApplyModifiedProperties();
    }

    // ------------------------------------------------------------
    static void Widen(Transform panel, params string[] children)
    {
        var prt = (RectTransform)panel;
        if (prt.sizeDelta.x >= PanelWidth) return;
        Undo.RecordObject(prt, "COM 10-12");
        prt.sizeDelta = new Vector2(PanelWidth, prt.sizeDelta.y);
        foreach (string n in children)
        {
            var c = panel.Find(n) as RectTransform;
            if (c == null) continue;
            Undo.RecordObject(c, "COM 10-12");
            c.sizeDelta = new Vector2(PanelWidth - 20f, c.sizeDelta.y);
        }
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[COM] {target.GetType().Name}.{field} 필드 없음", target); return; }
        p.objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
}
