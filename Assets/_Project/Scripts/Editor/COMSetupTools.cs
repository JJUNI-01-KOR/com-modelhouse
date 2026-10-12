using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10/8 기본 씬 세팅 (게임 빌드에는 안 들어감)
/// 메뉴는 COM → 전체 세팅·점검 하나로 합침 (COMSetupAll.cs). 여기 단계들은 씬에 없을 때만 실행된다.
///   구역(ZoneData + ZoneVolume), 플레이어(Player + SpawnPoint), 바닥·벽 충돌체,
///   옵션 UI(메뉴·옵션 패널·구역 정보 창), 상판·소파·러그 대상
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
    internal static void SetupZones()
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
    internal static void SetupPlayer()
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

    internal static void AddStructureColliders()
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
    internal static void AddFurnitureOptions()
    {
        // 10/12: 옵션 목록은 OptionCatalog에 있으므로 여기서는 클릭·색 변경 대상(OptionPart)만 만든다
        EnsureColorPart("Option_Countertop", PartType.Countertop, "m_countertop", null);
        EnsureColorPart("Option_Sofa", PartType.SofaFabric, "m_white_fabric", "sm_coach");
        EnsureColorPart("Option_Rug", PartType.Rug, "m_carpet", "sm_carpet");
        Debug.Log("[COM] 상판·소파·러그 대상 준비 완료. Console의 '제외한 것' 목록 확인!");
    }

    internal static void BuildUI()
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
        var btnReset  = UIFactory.CreateButton(menu.transform, "Btn_Reset", "위치 초기화", 22);
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

        // 8) 옵션 패널 연결. 옵션 목록·가격은 OptionCatalog(부록 A-7), 선택 상태는 ConfigurationState에서 읽는다 (10/12)
        var o = new SerializedObject(optionPanel);
        o.FindProperty("tabBar").objectReferenceValue = tabBar;
        o.FindProperty("listRoot").objectReferenceValue = list;
        o.FindProperty("closeButton").objectReferenceValue = optClose;
        o.FindProperty("config").objectReferenceValue = Object.FindAnyObjectByType<ConfigurationState>();
        o.ApplyModifiedProperties();

        Selection.activeGameObject = canvasGo;
        Debug.Log("[COM] UI 생성 완료: 좌측 상단 메뉴, 옵션 패널(벽지·바닥재), 기록 패널(자리만), 구역 정보 창");
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
            if (!MatchPrefix(r.name, namePrefix)) { skipped.Add(r.name); continue; }
            targets.Add(r);
            if (layer >= 0) { Undo.RecordObject(r.gameObject, "COM UI"); r.gameObject.layer = layer; }
            // 클릭(레이캐스트)되려면 충돌체가 필요
            if (r.GetComponent<Collider>() == null && r.GetComponent<MeshFilter>() != null)
                Undo.AddComponent<MeshCollider>(r.gameObject);
        }
        SerializedProperty arr = so.FindProperty("renderers");
        arr.arraySize = targets.Count;
        for (int i = 0; i < targets.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
        so.ApplyModifiedProperties();

        Debug.Log($"[COM] {objName}: 대상 {targets.Count}개 / {matName}을 같이 쓰지만 제외한 것: {string.Join(", ", skipped)}");
        return part;
    }

    private static bool MatchPrefix(string name, string prefixes)
    {
        if (string.IsNullOrEmpty(prefixes)) return true;
        foreach (string p in prefixes.Split('|'))
            if (name.StartsWith(p)) return true;
        return false;
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
