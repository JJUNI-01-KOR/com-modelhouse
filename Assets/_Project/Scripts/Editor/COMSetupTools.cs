using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 씬 세팅을 메뉴 한 번으로 해 주는 에디터 도구 (게임 빌드에는 안 들어감)
///   상단 메뉴 COM → 1. 구역 세팅   : ZoneData 4개 생성 + Zone_* 에 ZoneVolume 연결 (WBS 2.05)
///   상단 메뉴 COM → 2. 플레이어 세팅 : Player + SpawnPoint 생성, Main Camera를 자식으로 (WBS 3.01~3.03, 3.08)
/// 전부 Ctrl+Z로 되돌릴 수 있음.
/// </summary>
public static class COMSetupTools
{
    private const string DataFolder = "Assets/_Project/Data";
    private const string InteractableLayer = "Interactable";

    // 오브젝트 이름 뒤쪽 → (표시 이름, 조도 계수)  SRS 부록 A-8
    private static readonly (string key, string display, float lux)[] ZoneTable =
    {
        ("Entrance", "현관", 0.3f),
        ("Living",   "거실", 1.0f),
        ("Kitchen",  "주방", 1.0f),
        ("Room",     "침실", 0.6f),
        ("Bedroom",  "침실", 0.6f),
    };

    // ------------------------------------------------------------
    [MenuItem("COM/1. 구역 세팅 (ZoneData + ZoneVolume)")]
    private static void SetupZones()
    {
        GameObject root = GameObject.Find("Zone");
        if (root == null)
        {
            EditorUtility.DisplayDialog("COM", "Hierarchy에 'Zone' 오브젝트가 없어.", "확인");
            return;
        }

        EnsureFolder(DataFolder);
        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        int count = 0;

        foreach (Transform zoneTf in root.transform)
        {
            if (!zoneTf.name.StartsWith("Zone_")) continue;
            string key = zoneTf.name.Substring("Zone_".Length);

            // 1) ZoneData 에셋 (이미 있으면 재사용)
            string assetPath = $"{DataFolder}/ZoneData_{key}.asset";
            ZoneData data = AssetDatabase.LoadAssetAtPath<ZoneData>(assetPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ZoneData>();
                data.zoneId = key.ToLower();
                data.displayName = key;
                data.luxFactor = 1f;
                foreach (var row in ZoneTable)
                    if (row.key == key) { data.displayName = row.display; data.luxFactor = row.lux; }
                data.direction = "(입력)";
                data.feature = "(입력)";
                AssetDatabase.CreateAsset(data, assetPath);
            }

            // 2) ZoneVolume 붙이고 data 연결
            ZoneVolume vol = zoneTf.GetComponent<ZoneVolume>();
            if (vol == null) vol = Undo.AddComponent<ZoneVolume>(zoneTf.gameObject);
            var so = new SerializedObject(vol);
            so.FindProperty("data").objectReferenceValue = data;
            so.ApplyModifiedProperties();

            // 3) 부모 자체 콜라이더는 끄고, 자식 박스는 트리거 + Ignore Raycast
            foreach (Collider c in zoneTf.GetComponentsInChildren<Collider>(true))
            {
                Undo.RecordObject(c, "COM Zone Setup");
                Undo.RecordObject(c.gameObject, "COM Zone Setup");
                if (c.transform == zoneTf)
                {
                    Debug.LogWarning($"[COM] {zoneTf.name} 부모에 콜라이더가 있음 → 박스는 자식으로 옮기는 걸 추천", c);
                }
                c.isTrigger = true;
                c.gameObject.layer = ignoreRaycast;
            }
            count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[COM] 구역 {count}개 세팅 완료. {DataFolder}의 ZoneData에서 면적·방향·특징을 채워 줘.");
    }

    // ------------------------------------------------------------
    [MenuItem("COM/2. 플레이어 세팅 (Player + SpawnPoint)")]
    private static void SetupPlayer()
    {
        if (Object.FindAnyObjectByType<PlayerController>() != null)
        {
            EditorUtility.DisplayDialog("COM", "이미 PlayerController가 씬에 있어.", "확인");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            EditorUtility.DisplayDialog("COM", "MainCamera 태그가 달린 카메라가 없어.", "확인");
            return;
        }

        // 바닥 위치 찾기: 카메라 아래로 레이 → 못 찾으면 y=0
        Vector3 camPos = cam.transform.position;
        Vector3 feet = new Vector3(camPos.x, 0f, camPos.z);
        if (Physics.Raycast(camPos + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 20f,
                            ~0, QueryTriggerInteraction.Ignore))
            feet = hit.point;

        Quaternion yaw = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);

        // SpawnPoint
        var spawn = new GameObject("SpawnPoint");
        Undo.RegisterCreatedObjectUndo(spawn, "COM Player Setup");
        spawn.transform.SetPositionAndRotation(feet + Vector3.up * 0.05f, yaw);

        // Player
        var player = new GameObject("Player");
        Undo.RegisterCreatedObjectUndo(player, "COM Player Setup");
        player.transform.SetPositionAndRotation(spawn.transform.position, yaw);
        Undo.AddComponent<CharacterController>(player);
        PlayerController pc = Undo.AddComponent<PlayerController>(player);

        // 카메라를 Player 자식으로, 눈높이 1.6m
        Undo.SetTransformParent(cam.transform, player.transform, "COM Player Setup");
        cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        cam.transform.localRotation = Quaternion.identity;

        var pso = new SerializedObject(pc);
        pso.FindProperty("cameraPivot").objectReferenceValue = cam.transform;
        pso.FindProperty("spawnPoint").objectReferenceValue = spawn.transform;
        pso.ApplyModifiedProperties();

        // CursorPicker + Interactable 레이어
        int layer = EnsureLayer(InteractableLayer);
        CursorPicker picker = cam.GetComponent<CursorPicker>();
        if (picker == null) picker = Undo.AddComponent<CursorPicker>(cam.gameObject);
        if (layer >= 0)
        {
            var cso = new SerializedObject(picker);
            cso.FindProperty("interactableMask").intValue = 1 << layer;
            cso.ApplyModifiedProperties();
        }

        Selection.activeGameObject = player;
        Debug.Log("[COM] Player 세팅 완료. SpawnPoint를 현관 시작 위치로 옮기고 방향(파란 화살표)을 집 안쪽으로 맞춰 줘.");
    }

    // ------------------------------------------------------------
    // 바닥·벽에 충돌체가 빠져 있으면 플레이어가 떨어지거나 벽을 뚫음 (WBS 2.04)
    private static readonly string[] StructurePrefixes = { "sm_floor", "sm_walls" };

    [MenuItem("COM/3. 바닥·벽 충돌체 채우기")]
    private static void AddStructureColliders()
    {
        int added = 0, already = 0;
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>())
        {
            string n = mf.gameObject.name;
            bool match = false;
            foreach (string p in StructurePrefixes) if (n.StartsWith(p)) match = true;
            if (!match) continue;

            if (mf.GetComponent<Collider>() != null) { already++; continue; }
            MeshCollider mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
            added++;
            Debug.Log($"[COM] MeshCollider 추가: {n}", mf.gameObject);
        }
        Debug.Log($"[COM] 바닥·벽 충돌체: 새로 {added}개 추가, 이미 있던 것 {already}개");
    }

    // ------------------------------------------------------------
    // 벽지 색상 4종 (SRS 부록 A-7 이름·가격, 색은 팀 결정으로 색상만)
    private static readonly (string id, string name, string hex, int cost, bool isDefault)[] WallColors =
    {
        ("wall_white",  "화이트 페인트",       "#FFFFFF", 0,      true),
        ("wall_greige", "그레이지 실크 벽지",   "#BDB5A6", 400000, false),
        ("wall_sage",   "세이지 그린 실크 벽지", "#A3B18A", 400000, false),
        ("wall_fabric", "패브릭 질감 벽지",     "#D9CFC1", 700000, false),
    };

    [MenuItem("COM/4. 벽지 테스트 세팅 (색상)")]
    private static void SetupWallpaperTest()
    {
        Material walls = FindMaterial("m_walls");
        if (walls == null)
        {
            EditorUtility.DisplayDialog("COM", "m_walls 재질을 못 찾았어.", "확인");
            return;
        }

        GameObject go = GameObject.Find("Option_Wall");
        if (go == null)
        {
            go = new GameObject("Option_Wall");
            Undo.RegisterCreatedObjectUndo(go, "COM Wallpaper Setup");
        }

        // OptionPart
        OptionPart part = go.GetComponent<OptionPart>();
        if (part == null) part = Undo.AddComponent<OptionPart>(go);
        var pso = new SerializedObject(part);
        pso.FindProperty("part").enumValueIndex = (int)PartType.Wall;
        pso.FindProperty("sourceMaterial").objectReferenceValue = walls;
        pso.ApplyModifiedProperties();

        // OptionApplyTester (숫자 키 테스트)
        OptionApplyTester tester = go.GetComponent<OptionApplyTester>();
        if (tester == null) tester = Undo.AddComponent<OptionApplyTester>(go);
        var tso = new SerializedObject(tester);
        tso.FindProperty("target").objectReferenceValue = part;
        SerializedProperty arr = tso.FindProperty("options");
        arr.arraySize = WallColors.Length;
        for (int i = 0; i < WallColors.Length; i++)
        {
            var w = WallColors[i];
            SerializedProperty e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("optionId").stringValue = w.id;
            e.FindPropertyRelative("part").enumValueIndex = (int)PartType.Wall;
            e.FindPropertyRelative("displayName").stringValue = w.name;
            e.FindPropertyRelative("useColor").boolValue = true;
            ColorUtility.TryParseHtmlString(w.hex, out Color c);
            e.FindPropertyRelative("color").colorValue = c;
            e.FindPropertyRelative("extraCost").intValue = w.cost;
            e.FindPropertyRelative("isDefault").boolValue = w.isDefault;
        }
        tso.ApplyModifiedProperties();

        // m_walls를 쓰는 벽 메쉬 → Interactable 레이어 (클릭·강조용)
        int layer = EnsureLayer(InteractableLayer);
        int count = 0;
        foreach (Renderer r in Object.FindObjectsByType<Renderer>())
        {
            if (System.Array.IndexOf(r.sharedMaterials, walls) < 0) continue;
            if (layer >= 0)
            {
                Undo.RecordObject(r.gameObject, "COM Wallpaper Setup");
                r.gameObject.layer = layer;
            }
            count++;
        }

        Selection.activeGameObject = go;
        Debug.Log($"[COM] 벽지 세팅 완료. m_walls 쓰는 메쉬 {count}개. Play 후 숫자 1~4로 색 변경.");
    }

    // ------------------------------------------------------------
    // 바닥 색상 4종 (EC0000 포함). 금액은 예시 값 → SRS A-7과 맞춰서 바꿀 것
    private static readonly (string id, string name, string hex, int cost, bool isDefault)[] FloorColors =
    {
        ("floor_wood",   "원목 마루 (기본)", "#FFFFFF", 0,       true),
        ("floor_kang",   "강마루",           "#E48120", 1500000, false),
        ("floor_navy",   "네이비 마루",      "#1747BE", 3200000, false),
        ("floor_red",    "레드 포인트",      "#EC0000", 1000000, false),
    };

    [MenuItem("COM/5. 옵션 UI 만들기 (메뉴·옵션 패널·구역 정보 창)")]
    private static void BuildUI()
    {
        // 1) 옵션 대상: 벽, 바닥
        // m_walls는 천장·아일랜드·몰딩도, m_floor는 의자·수납함도 같이 써서 이름으로 대상만 골라냄 (WBS 2.06)
        OptionPart wall = EnsureColorPart("Option_Wall", PartType.Wall, "m_walls", "sm_walls");
        OptionPart floor = EnsureColorPart("Option_Floor", PartType.Floor, "m_floor", "sm_floor");
        if (wall == null || floor == null) return;

        // 숫자 키 테스터는 패널과 선택 상태가 어긋나므로 제거
        foreach (var t in Object.FindObjectsByType<OptionApplyTester>())
            Undo.DestroyObjectImmediate(t);

        // 2) 예전에 만든 UI가 있으면 지우고 다시 생성
        GameObject old = GameObject.Find("COM_UI");
        if (old != null) Undo.DestroyObjectImmediate(old);

        EnsureEventSystem();

        var canvasGo = new GameObject("COM_UI", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(canvasGo, "COM UI");
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        Transform root = canvasGo.transform;

        // 3) 좌측 상단 메뉴 (UI-04)
        var menu = UIFactory.CreatePanel(root, "Menu", UIFactory.PanelColor);
        UIFactory.PlaceTopLeft(menu.rectTransform, new Vector2(20, -20), new Vector2(200, 330));
        UIFactory.AddVertical(menu.gameObject, 8, 10);
        var btnOption = UIFactory.CreateButton(menu.transform, "Btn_Option", "옵션", 22);
        var btnLog    = UIFactory.CreateButton(menu.transform, "Btn_Log", "기록", 22);
        var btnReset  = UIFactory.CreateButton(menu.transform, "Btn_Reset", "초기화", 22);
        foreach (var b in new[] { btnOption, btnLog, btnReset }) UIFactory.SetLayoutSize(b, height: 56);
        var cost = UIFactory.CreateText(menu.transform, "CostText", "추가 비용 합계\n+0원", 18, TextAnchor.MiddleCenter);
        UIFactory.SetLayoutSize(cost, height: 110);

        // 4) 옵션 패널 (UI-03) — 메뉴 오른쪽
        var optPanel = UIFactory.CreatePanel(root, "OptionPanel", UIFactory.PanelColor);
        UIFactory.PlaceTopLeft(optPanel.rectTransform, new Vector2(240, -20), new Vector2(460, 520));
        var optTitle = UIFactory.CreateText(optPanel.transform, "Title", "옵션", 24);
        UIFactory.PlaceTopLeft(optTitle.rectTransform, new Vector2(16, -10), new Vector2(300, 40));
        var optClose = UIFactory.CreateButton(optPanel.transform, "Btn_Close", "닫기", 18);
        PlaceTopRight(optClose, new Vector2(-10, -10), new Vector2(80, 40));

        var tabBar = UIFactory.CreateRect(optPanel.transform, "Tabs");
        UIFactory.PlaceTopLeft(tabBar, new Vector2(10, -60), new Vector2(440, 50));
        UIFactory.AddHorizontal(tabBar.gameObject, 6, 0);

        var list = UIFactory.CreateRect(optPanel.transform, "List");
        UIFactory.PlaceTopLeft(list, new Vector2(10, -120), new Vector2(440, 390));
        UIFactory.AddVertical(list.gameObject, 6, 0);

        var optionPanel = optPanel.gameObject.AddComponent<OptionPanelUI>();

        // 5) 기록 패널 (UI-07) — 자리만
        var logPanelImg = UIFactory.CreatePanel(root, "LogPanel", UIFactory.PanelColor);
        UIFactory.PlaceTopLeft(logPanelImg.rectTransform, new Vector2(240, -20), new Vector2(460, 520));
        var logTitle = UIFactory.CreateText(logPanelImg.transform, "Title", "이벤트 기록", 24);
        UIFactory.PlaceTopLeft(logTitle.rectTransform, new Vector2(16, -10), new Vector2(300, 40));
        var logBody = UIFactory.CreateText(logPanelImg.transform, "Body", "(EventLogger 연결 예정 — WBS 4.13)", 18, TextAnchor.UpperLeft);
        UIFactory.PlaceTopLeft(logBody.rectTransform, new Vector2(16, -60), new Vector2(430, 440));
        var logClose = UIFactory.CreateButton(logPanelImg.transform, "Btn_Close", "닫기", 18);
        PlaceTopRight(logClose, new Vector2(-10, -10), new Vector2(80, 40));
        var logPanel = logPanelImg.gameObject.AddComponent<PanelUI>();

        // 6) 구역 정보 창 (UI-02, FR-02) — 화면 위 가운데
        var zoneImg = UIFactory.CreatePanel(root, "ZoneInfoPanel", UIFactory.PanelColor);
        var zrt = zoneImg.rectTransform;
        zrt.anchorMin = zrt.anchorMax = zrt.pivot = new Vector2(0.5f, 1f);
        zrt.anchoredPosition = new Vector2(0, -20);
        zrt.sizeDelta = new Vector2(560, 150);
        zoneImg.raycastTarget = false;
        UIFactory.AddVertical(zoneImg.gameObject, 2, 12);
        var zName = UIFactory.CreateText(zoneImg.transform, "Name", "구역", 30, TextAnchor.MiddleCenter);
        var zArea = UIFactory.CreateText(zoneImg.transform, "Area", "면적", 18, TextAnchor.MiddleCenter);
        var zDir  = UIFactory.CreateText(zoneImg.transform, "Direction", "방향", 18, TextAnchor.MiddleCenter);
        var zFeat = UIFactory.CreateText(zoneImg.transform, "Feature", "특징", 18, TextAnchor.MiddleCenter);
        zoneImg.gameObject.AddComponent<CanvasGroup>();
        var zoneUI = zoneImg.gameObject.AddComponent<ZoneInfoUI>();

        // 7) HUD 연결
        var hud = canvasGo.AddComponent<HUDController>();
        var h = new SerializedObject(hud);
        h.FindProperty("optionButton").objectReferenceValue = btnOption;
        h.FindProperty("logButton").objectReferenceValue = btnLog;
        h.FindProperty("resetButton").objectReferenceValue = btnReset;
        h.FindProperty("optionPanel").objectReferenceValue = optionPanel;
        h.FindProperty("logPanel").objectReferenceValue = logPanel;
        h.FindProperty("costText").objectReferenceValue = cost;
        h.ApplyModifiedProperties();

        SetRef(logPanel, "closeButton", logClose);
        SetRef(optionPanel, "closeButton", optClose);

        var z = new SerializedObject(zoneUI);
        z.FindProperty("nameText").objectReferenceValue = zName;
        z.FindProperty("areaText").objectReferenceValue = zArea;
        z.FindProperty("directionText").objectReferenceValue = zDir;
        z.FindProperty("featureText").objectReferenceValue = zFeat;
        z.ApplyModifiedProperties();

        // 8) 옵션 패널 데이터 (벽지, 바닥재)
        var o = new SerializedObject(optionPanel);
        o.FindProperty("tabBar").objectReferenceValue = tabBar;
        o.FindProperty("listRoot").objectReferenceValue = list;
        o.FindProperty("hud").objectReferenceValue = hud;
        o.FindProperty("closeButton").objectReferenceValue = optClose;
        SerializedProperty entries = o.FindProperty("entries");
        entries.arraySize = 2;
        FillEntry(entries.GetArrayElementAtIndex(0), "벽지", PartType.Wall, wall, WallColors);
        FillEntry(entries.GetArrayElementAtIndex(1), "바닥재", PartType.Floor, floor, FloorColors);
        o.ApplyModifiedProperties();

        Selection.activeGameObject = canvasGo;
        Debug.Log("[COM] UI 생성 완료: 좌측 상단 메뉴, 옵션 패널(벽지·바닥재), 기록 패널(자리만), 구역 정보 창");
    }

    // 씬의 옵션 패널을 다시 만들지 않고 목록(색·이름·가격)만 코드 값으로 갱신
    // → UI 위치·크기를 손으로 바꿔 둔 게 있어도 그대로 유지됨
    [MenuItem("COM/6. 옵션 목록만 갱신 (UI 유지)")]
    private static void RefreshOptionLists()
    {
        OptionPanelUI panel = Object.FindAnyObjectByType<OptionPanelUI>(FindObjectsInactive.Include);
        if (panel == null)
        {
            EditorUtility.DisplayDialog("COM", "OptionPanelUI가 씬에 없어. 먼저 5번으로 UI를 만들어 줘.", "확인");
            return;
        }
        Undo.RecordObject(panel, "COM Refresh Options");
        var o = new SerializedObject(panel);
        SerializedProperty entries = o.FindProperty("entries");
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            var type = (PartType)e.FindPropertyRelative("part").enumValueIndex;
            var target = (OptionPart)e.FindPropertyRelative("target").objectReferenceValue;
            string label = e.FindPropertyRelative("label").stringValue;
            if (type == PartType.Wall) FillEntry(e, label, type, target, WallColors);
            else if (type == PartType.Floor) FillEntry(e, label, type, target, FloorColors);
        }
        o.ApplyModifiedProperties();
        EditorUtility.SetDirty(panel);
        Debug.Log("[COM] 옵션 목록 갱신 완료 (벽지·바닥재)");
    }

    // NFR-03 검사: 문·문틀 실제 높이(월드 기준 m)와 모델 스케일을 Console에 출력
    [MenuItem("COM/7. 문 높이·스케일 재기 (NFR-03)")]
    private static void MeasureDoors()
    {
        GameObject apt = GameObject.Find("open_concept_apartment_unity");
        if (apt != null)
        {
            Debug.Log($"[COM] 아파트 Transform Scale = {apt.transform.lossyScale}");
            Object src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(apt);
            string path = src != null ? AssetDatabase.GetAssetPath(src) : "";
            if (AssetImporter.GetAtPath(path) is ModelImporter mi)
                Debug.Log($"[COM] FBX Scale Factor = {mi.globalScale}, Convert Units(useFileScale) = {mi.useFileScale}, 파일 단위 배율 = {mi.fileScale}");
        }

        foreach (Renderer r in Object.FindObjectsByType<Renderer>())
        {
            string n = r.name.ToLower();
            if (!n.StartsWith("sm_door") && !n.StartsWith("sm_closet_door")) continue;
            Vector3 s = r.bounds.size;   // 월드 기준 크기(m)
            Debug.Log($"[COM] {r.name}: 높이 {s.y:0.00}m, 폭 {Mathf.Max(s.x, s.z):0.00}m, 두께 {Mathf.Min(s.x, s.z):0.00}m (Scale {r.transform.lossyScale})", r);
        }

        Camera cam = Camera.main;
        if (cam != null) Debug.Log($"[COM] 비교용: 카메라(눈) 높이 y = {cam.transform.position.y:0.00}m");
    }

    private static OptionPart EnsureColorPart(string objName, PartType type, string matName, string namePrefix)
    {
        Material mat = FindMaterial(matName);
        if (mat == null)
        {
            EditorUtility.DisplayDialog("COM", $"{matName} 재질을 못 찾았어.", "확인");
            return null;
        }

        GameObject go = GameObject.Find(objName);
        if (go == null)
        {
            go = new GameObject(objName);
            Undo.RegisterCreatedObjectUndo(go, "COM UI");
        }
        OptionPart part = go.GetComponent<OptionPart>();
        if (part == null) part = Undo.AddComponent<OptionPart>(go);
        var so = new SerializedObject(part);
        so.FindProperty("part").enumValueIndex = (int)type;
        so.FindProperty("sourceMaterial").objectReferenceValue = mat;
        so.ApplyModifiedProperties();

        // 재질을 쓰면서 이름이 namePrefix로 시작하는 메쉬만 대상 → renderers에 직접 등록
        int layer = EnsureLayer(InteractableLayer);
        var targets = new System.Collections.Generic.List<Renderer>();
        var skipped = new System.Collections.Generic.List<string>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>())
        {
            if (System.Array.IndexOf(r.sharedMaterials, mat) < 0) continue;
            if (!r.name.StartsWith(namePrefix)) { skipped.Add(r.name); continue; }
            targets.Add(r);
            if (layer >= 0) { Undo.RecordObject(r.gameObject, "COM UI"); r.gameObject.layer = layer; }
        }
        SerializedProperty arr = so.FindProperty("renderers");
        arr.arraySize = targets.Count;
        for (int i = 0; i < targets.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
        so.ApplyModifiedProperties();

        Debug.Log($"[COM] {objName}: 대상 {targets.Count}개 / {matName}을 같이 쓰지만 제외한 것: {string.Join(", ", skipped)}");
        return part;
    }

    private static void FillEntry(SerializedProperty e, string label, PartType type, OptionPart target,
                                  (string id, string name, string hex, int cost, bool isDefault)[] colors)
    {
        e.FindPropertyRelative("label").stringValue = label;
        e.FindPropertyRelative("part").enumValueIndex = (int)type;
        e.FindPropertyRelative("target").objectReferenceValue = target;
        SerializedProperty opts = e.FindPropertyRelative("options");
        opts.arraySize = colors.Length;
        for (int i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            SerializedProperty it = opts.GetArrayElementAtIndex(i);
            it.FindPropertyRelative("optionId").stringValue = c.id;
            it.FindPropertyRelative("part").enumValueIndex = (int)type;
            it.FindPropertyRelative("displayName").stringValue = c.name;
            it.FindPropertyRelative("useColor").boolValue = true;
            ColorUtility.TryParseHtmlString(c.hex, out Color col);
            it.FindPropertyRelative("color").colorValue = col;
            it.FindPropertyRelative("extraCost").intValue = c.cost;
            it.FindPropertyRelative("isDefault").boolValue = c.isDefault;
        }
    }

    private static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }

    private static void PlaceTopRight(Component c, Vector2 pos, Vector2 size)
    {
        var rt = (RectTransform)c.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void EnsureEventSystem()
    {
        var es = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(go, "COM UI");
            es = go.AddComponent<UnityEngine.EventSystems.EventSystem>();
        }
        // Input System 전용 프로젝트라 옛날 StandaloneInputModule은 에러 → 교체
        var old = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        if (old != null) Undo.DestroyObjectImmediate(old);
        if (es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
            Undo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(es.gameObject);
    }

    private static Material FindMaterial(string exactName)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{exactName} t:Material"))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null && m.name == exactName) return m;
        }
        return null;
    }

    // ------------------------------------------------------------
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    /// <summary>레이어가 없으면 빈 칸(8번부터)에 추가하고 번호를 돌려줌</summary>
    private static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty sp = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = name;
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[COM] '{name}' 레이어를 {i}번에 추가함");
                return i;
            }
        }
        Debug.LogWarning("[COM] 빈 레이어 칸이 없음");
        return -1;
    }
}
