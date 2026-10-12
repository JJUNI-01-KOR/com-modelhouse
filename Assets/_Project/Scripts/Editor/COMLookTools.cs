using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 외형 다듬기 (시연 화면 품질, NFR-10)
//   Tools → COM → 로봇팔 색을 집 분위기에 맞추기   : RobotArm.prefab 재질을 웜 화이트·차콜·브라스로 교체
//   Tools → COM → 커피 머신 새 디자인으로 바꾸기    : 씬의 CoffeeMachine 겉모습을 에스프레소 머신으로 다시 만듦
//                                                    (CoffeeMachine·CoffeeBrewEffect 컴포넌트와 CupHolder·Steam 연결은 그대로)
// 둘 다 Ctrl+Z로 되돌릴 수 있고(로봇팔은 프리팹 파일이라 git으로 되돌림), 끝나면 Ctrl+S로 씬 저장.
public static class COMLookTools
{
    const string RobotMatFolder = "Assets/_Project/Materials/Robot";
    const string MachineMatFolder = "Assets/_Project/Materials/CoffeeMachine";
    const string RobotPrefabPath = "Assets/_Project/Prefabs/RobotArm.prefab";

    // ───────────────────────── 로봇팔 ─────────────────────────

    [MenuItem("Tools/COM/로봇팔 색을 집 분위기에 맞추기")]
    public static void RecolorRobot()
    {
        EnsureFolder(RobotMatFolder);
        // 공간 에셋: 흰 벽·회색 패브릭·원목 바닥·브라스 수전 → 웜 화이트 몸체, 차콜 관절, 브라스 포인트
        Material shell = Lit(RobotMatFolder, "robot_shell", Hex("#ECE8E1"), 0f, 0.55f);
        Material joint = Lit(RobotMatFolder, "robot_joint", Hex("#2C2C2E"), 0.2f, 0.6f);
        Material accent = Lit(RobotMatFolder, "robot_accent", Hex("#B08D57"), 1f, 0.7f);

        if (AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath) == null)
        {
            EditorUtility.DisplayDialog("로봇팔 색", $"{RobotPrefabPath} 가 없어요.", "확인");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(RobotPrefabPath);
        var report = new Dictionary<string, string>();
        try
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || m == shell || m == joint || m == accent) continue;
                    Material to = Classify(m, shell, joint, accent);
                    report[m.name] = to.name;
                    mats[i] = to;
                }
                r.sharedMaterials = mats;
            }
            PrefabUtility.SaveAsPrefabAsset(root, RobotPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var sb = new System.Text.StringBuilder("[COMLook] 로봇팔 재질 교체: ");
        foreach (var kv in report) sb.Append($"{kv.Key}→{kv.Value}  ");
        Debug.Log(sb.ToString());
    }

    // 원래 색으로 역할을 나눈다: 어두우면 관절(차콜), 파랗거나 빨갛게 튀는 색이면 포인트(브라스), 나머지는 몸체(웜 화이트)
    static Material Classify(Material m, Material shell, Material joint, Material accent)
    {
        Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : (m.HasProperty("_Color") ? m.color : Color.white);
        float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        Color.RGBToHSV(c, out _, out float sat, out _);
        if (lum < 0.25f) return joint;
        if (sat > 0.35f) return accent;
        return shell;
    }

    // ───────────────────────── 커피 머신 ─────────────────────────

    [MenuItem("Tools/COM/커피 머신 새 디자인으로 바꾸기")]
    public static void RebuildCoffeeMachine()
    {
        CoffeeMachine cm = Object.FindAnyObjectByType<CoffeeMachine>();
        if (cm == null) { EditorUtility.DisplayDialog("커피 머신", "씬에 CoffeeMachine이 없어요. 먼저 커피 스테이션을 만드세요.", "확인"); return; }
        Transform machine = cm.transform;

        EnsureFolder(MachineMatFolder);
        Material steel  = Lit(MachineMatFolder, "cm_steel",  Hex("#C4C7CB"), 1f, 0.72f);
        Material chrome = Lit(MachineMatFolder, "cm_chrome", Hex("#E8EAEC"), 1f, 0.93f);
        Material gloss  = Lit(MachineMatFolder, "cm_black_gloss", Hex("#141416"), 0.1f, 0.88f);
        Material matte  = Lit(MachineMatFolder, "cm_black_matte", Hex("#1E1E20"), 0f, 0.3f);
        Material wood   = Lit(MachineMatFolder, "cm_walnut", Hex("#5A3B26"), 0f, 0.45f);
        Material face   = Lit(MachineMatFolder, "cm_gauge_face", Hex("#F4F1EA"), 0f, 0.6f, Hex("#F4F1EA") * 0.25f);
        Material led    = Lit(MachineMatFolder, "cm_led", Hex("#FFC77A"), 0f, 0.8f, Hex("#FFB24D") * 2.5f);

        Undo.RegisterFullObjectHierarchyUndo(machine.gameObject, "커피 머신 새 디자인");

        // 기준 자리: 예전 몸체(Body)를 옮겨 둔 자리·방향을 그대로 쓴다 (사용자가 배치한 위치 유지)
        Transform oldBody = machine.Find("Body");
        Vector3 anchorPos = oldBody != null ? new Vector3(oldBody.localPosition.x, 0f, oldBody.localPosition.z) : Vector3.zero;
        Quaternion anchorRot = oldBody != null ? Quaternion.Euler(0f, oldBody.localEulerAngles.y, 0f) : Quaternion.identity;

        // 예전 겉모습 지우기 (CupHolder·Steam·Spout 이외 자식, 이전에 만든 Model)
        var keep = new HashSet<string> { "CupHolder", "Steam" };
        var remove = new List<GameObject>();
        foreach (Transform ch in machine) if (!keep.Contains(ch.name)) remove.Add(ch.gameObject);
        foreach (GameObject go in remove) Undo.DestroyObjectImmediate(go);

        var model = new GameObject("Model").transform;
        Undo.RegisterCreatedObjectUndo(model.gameObject, "커피 머신 새 디자인");
        model.SetParent(machine, false);
        model.localPosition = anchorPos;
        model.localRotation = anchorRot;

        // 크기(m): 폭 0.26 · 깊이 0.24 · 높이 0.33, 앞면(+Z)이 로봇 쪽
        // 몸체
        P(model, "Body", PrimitiveType.Cube, new Vector3(0f, 0.17f, 0f), new Vector3(0.26f, 0.30f, 0.24f), steel, collider: true);
        P(model, "Base", PrimitiveType.Cube, new Vector3(0f, 0.012f, 0.02f), new Vector3(0.27f, 0.024f, 0.28f), matte);
        P(model, "TopPlate", PrimitiveType.Cube, new Vector3(0f, 0.323f, 0f), new Vector3(0.262f, 0.006f, 0.242f), chrome);
        // 둥근 앞 모서리 기둥
        P(model, "PillarL", PrimitiveType.Cylinder, new Vector3(-0.13f, 0.17f, 0.12f), new Vector3(0.03f, 0.15f, 0.03f), chrome);
        P(model, "PillarR", PrimitiveType.Cylinder, new Vector3(0.13f, 0.17f, 0.12f), new Vector3(0.03f, 0.15f, 0.03f), chrome);
        // 앞 패널
        P(model, "FrontPanel", PrimitiveType.Cube, new Vector3(0f, 0.235f, 0.1215f), new Vector3(0.22f, 0.15f, 0.004f), gloss);
        // 컵 워머 난간
        for (int i = 0; i < 2; i++)
            P(model, "Rail" + i, PrimitiveType.Cylinder, new Vector3(0f, 0.338f, -0.08f + i * 0.16f), new Vector3(0.006f, 0.115f, 0.006f), chrome, rot: new Vector3(0f, 0f, 90f));
        for (int i = 0; i < 2; i++)
            P(model, "RailPost" + i, PrimitiveType.Cylinder, new Vector3(-0.115f + i * 0.23f, 0.332f, 0f), new Vector3(0.006f, 0.008f, 0.006f), chrome);

        // 압력 게이지
        P(model, "GaugeRim", PrimitiveType.Cylinder, new Vector3(0f, 0.275f, 0.124f), new Vector3(0.056f, 0.004f, 0.056f), chrome, rot: new Vector3(90f, 0f, 0f));
        P(model, "GaugeFace", PrimitiveType.Cylinder, new Vector3(0f, 0.275f, 0.1265f), new Vector3(0.046f, 0.002f, 0.046f), face, rot: new Vector3(90f, 0f, 0f));
        P(model, "GaugeNeedle", PrimitiveType.Cube, new Vector3(0.006f, 0.279f, 0.1285f), new Vector3(0.016f, 0.0025f, 0.001f), matte, rot: new Vector3(0f, 0f, 35f));
        // 버튼 3개 + 표시등
        for (int i = 0; i < 3; i++)
        {
            float x = -0.065f + i * 0.065f;
            if (i == 1) continue;   // 가운데는 게이지
            P(model, "Button" + i, PrimitiveType.Cylinder, new Vector3(x, 0.275f, 0.125f), new Vector3(0.022f, 0.004f, 0.022f), chrome, rot: new Vector3(90f, 0f, 0f));
            P(model, "ButtonLed" + i, PrimitiveType.Cylinder, new Vector3(x, 0.275f, 0.1275f), new Vector3(0.012f, 0.002f, 0.012f), led, rot: new Vector3(90f, 0f, 0f));
        }

        // 그룹헤드 + 포터필터
        const float headZ = 0.165f;
        P(model, "GroupNeck", PrimitiveType.Cube, new Vector3(0f, 0.2f, 0.14f), new Vector3(0.06f, 0.04f, 0.05f), chrome);
        P(model, "GroupHead", PrimitiveType.Cylinder, new Vector3(0f, 0.188f, headZ), new Vector3(0.078f, 0.014f, 0.078f), chrome);
        P(model, "Portafilter", PrimitiveType.Cylinder, new Vector3(0f, 0.165f, headZ), new Vector3(0.068f, 0.012f, 0.068f), steel);
        // 손잡이는 왼쪽 앞 45°로 (로봇이 정면에서 컵을 넣을 때 걸리지 않게)
        Vector3 handleDir = Quaternion.Euler(0f, -45f, 0f) * Vector3.forward;
        Vector3 headC = new Vector3(0f, 0f, headZ);
        P(model, "PortafilterHandleNeck", PrimitiveType.Cylinder, headC + handleDir * 0.048f + Vector3.up * 0.163f, new Vector3(0.016f, 0.02f, 0.016f), chrome, rot: new Vector3(80f, -45f, 0f));
        P(model, "PortafilterHandle", PrimitiveType.Capsule, headC + handleDir * 0.1f + Vector3.up * 0.155f, new Vector3(0.024f, 0.04f, 0.024f), wood, rot: new Vector3(80f, -45f, 0f));
        // 추출구 두 개
        P(model, "SpoutL", PrimitiveType.Cylinder, new Vector3(-0.012f, 0.148f, headZ), new Vector3(0.01f, 0.008f, 0.01f), chrome);
        P(model, "SpoutR", PrimitiveType.Cylinder, new Vector3(0.012f, 0.148f, headZ), new Vector3(0.01f, 0.008f, 0.01f), chrome);

        // 스팀 완드 + 노브
        P(model, "SteamWand", PrimitiveType.Cylinder, new Vector3(0.115f, 0.17f, 0.15f), new Vector3(0.008f, 0.06f, 0.008f), chrome, rot: new Vector3(0f, 0f, 12f));
        P(model, "SteamKnob", PrimitiveType.Cylinder, new Vector3(0.115f, 0.255f, 0.135f), new Vector3(0.026f, 0.012f, 0.026f), wood, rot: new Vector3(90f, 0f, 0f));

        // 물받이 + 그릴
        P(model, "DripTray", PrimitiveType.Cube, new Vector3(0f, 0.03f, 0.18f), new Vector3(0.2f, 0.018f, 0.12f), steel);
        for (int i = 0; i < 6; i++)
            P(model, "Grille" + i, PrimitiveType.Cube, new Vector3(0f, 0.04f, 0.13f + i * 0.02f), new Vector3(0.18f, 0.003f, 0.008f), chrome);
        // 발
        for (int i = 0; i < 4; i++)
            P(model, "Foot" + i, PrimitiveType.Cylinder, new Vector3(i % 2 == 0 ? -0.11f : 0.11f, 0.004f, i < 2 ? -0.1f : 0.14f), new Vector3(0.025f, 0.004f, 0.025f), matte);

        // ── 동작용 기준점 (CoffeeMachine이 찾는 이름) ──
        // Spout: 추출구 바로 아래 기준점. 크기를 작게 둬서 CoffeeMachine.AlignHolderToSpout가 앞으로 밀지 않게 함
        var spout = new GameObject("Spout").transform;
        Undo.RegisterCreatedObjectUndo(spout.gameObject, "커피 머신 새 디자인");
        spout.SetParent(machine, false);
        spout.localPosition = anchorPos + anchorRot * new Vector3(0f, 0.144f, headZ);
        spout.localRotation = anchorRot;
        spout.localScale = new Vector3(0.05f, 0.05f, 0.05f);

        Transform holder = machine.Find("CupHolder");
        if (holder != null)
        {
            Undo.RecordObject(holder, "커피 머신 새 디자인");
            holder.localPosition = anchorPos + anchorRot * new Vector3(0f, 0.041f, headZ);   // 물받이 그릴 위
            holder.localRotation = anchorRot;
        }
        Transform steam = machine.Find("Steam");
        if (steam != null)
        {
            Undo.RecordObject(steam, "커피 머신 새 디자인");
            steam.localPosition = anchorPos + anchorRot * new Vector3(0f, 0.13f, headZ);
        }

        EditorSceneManager.MarkSceneDirty(machine.gameObject.scene);
        Selection.activeObject = model.gameObject;
        Debug.Log("[COMLook] 커피 머신 새 디자인 완료 (컵 받는 자리·김 위치도 새 추출구에 맞춤). Ctrl+S로 저장", model);
    }

    // ───────────────────────── 공통 ─────────────────────────

    static GameObject P(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat,
                        Vector3 rot = default, bool collider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Undo.RegisterCreatedObjectUndo(go, "커피 머신 새 디자인");
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(rot);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Material Lit(string folder, string name, Color color, float metallic, float smoothness, Color? emission = null)
    {
        string path = $"{folder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = m == null;
        if (isNew)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (emission.HasValue && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        if (isNew) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
        return m;
    }

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out Color c); return c; }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
