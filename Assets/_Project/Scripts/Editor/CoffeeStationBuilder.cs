using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 Tools → COM → 커피 스테이션 만들기 (WBS 2.08)
// 씬의 RobotArm 옆에 시험용 머그컵과 커피 머신을 기본 도형으로 만들고, CoffeeTask까지 연결한다.
// - 컵: 피벗이 바닥 가운데, 지름 8cm·높이 9cm, 자식 Coffee(꺼짐), Rigidbody(Kinematic)
// - 머신: 앞면(추출구)이 로봇을 향함, 자식 CupHolder(추출구 아래 컵 바닥 자리), Steam(김)
// - 위치: 로봇 베이스 높이(=식탁 면)에서 로봇의 +X 방향 앞쪽 30cm 정도. 만든 뒤 마음대로 옮겨도 된다.
// 다시 실행하면 기존 CoffeeStation을 지우고 새로 만든다 (Ctrl+Z로 되돌릴 수 있음).
public static class CoffeeStationBuilder
{
    const string MatFolder = "Assets/_Project/Materials/Coffee";

    [MenuItem("Tools/COM/커피 스테이션 만들기 (컵·머신)")]
    public static void Build()
    {
        RobotArm arm = Object.FindAnyObjectByType<RobotArm>();
        if (arm == null)
        {
            EditorUtility.DisplayDialog("커피 스테이션", "씬에 RobotArm이 없어요. UR3에 RobotArm 컴포넌트를 먼저 붙이세요.", "확인");
            return;
        }

        GameObject old = GameObject.Find("CoffeeStation");
        if (old != null)
        {
            if (!EditorUtility.DisplayDialog("커피 스테이션", "이미 CoffeeStation이 있어요. 지우고 새로 만들까요?", "새로 만들기", "취소")) return;
            Undo.DestroyObjectImmediate(old);
        }

        EnsureFolder(MatFolder);
        Material mugMat = MakeMat("mug_white", new Color(0.95f, 0.95f, 0.93f), 0.6f, 0f);
        Material coffeeMat = MakeMat("coffee", new Color(0.23f, 0.12f, 0.05f), 0.85f, 0f);
        Material bodyMat = MakeMat("machine_body", new Color(0.12f, 0.12f, 0.13f), 0.5f, 0f);
        Material metalMat = MakeMat("machine_metal", new Color(0.72f, 0.72f, 0.74f), 0.85f, 1f);

        Transform robot = arm.transform;
        Vector3 basePos = robot.position;                              // 로봇이 놓인 면 = 식탁 높이
        Quaternion yaw = Quaternion.Euler(0f, robot.eulerAngles.y, 0f);
        Vector3 cupPos = basePos + yaw * new Vector3(0.33f, 0f, -0.15f);
        Vector3 holderPos = basePos + yaw * new Vector3(0.30f, 0f, 0.18f);

        var station = new GameObject("CoffeeStation");
        Undo.RegisterCreatedObjectUndo(station, "커피 스테이션 만들기");

        // ── 머그컵 ──
        var cup = new GameObject("Cup");
        cup.transform.SetParent(station.transform, false);
        cup.transform.SetPositionAndRotation(cupPos, yaw);
        cup.AddComponent<Rigidbody>().isKinematic = true;
        Part(PrimitiveType.Cylinder, "Body", cup.transform, new Vector3(0f, 0.045f, 0f), new Vector3(0.08f, 0.045f, 0.08f), mugMat, true);
        // 손잡이는 로봇 반대쪽 (그리퍼가 옆에서 잡을 때 걸리지 않게)
        Part(PrimitiveType.Cube, "Handle", cup.transform, new Vector3(0.048f, 0.05f, 0f), new Vector3(0.02f, 0.05f, 0.012f), mugMat, false);
        GameObject coffee = Part(PrimitiveType.Cylinder, "Coffee", cup.transform, new Vector3(0f, 0.08f, 0f), new Vector3(0.072f, 0.004f, 0.072f), coffeeMat, false);
        coffee.SetActive(false);

        var cupSpot = new GameObject("CupSpot");
        cupSpot.transform.SetParent(station.transform, false);
        cupSpot.transform.SetPositionAndRotation(cupPos, yaw);

        // ── 커피 머신 (앞면 = 로컬 +Z가 로봇을 향함) ──
        Vector3 dir = holderPos - basePos;
        dir.y = 0f;
        dir.Normalize();
        var machine = new GameObject("CoffeeMachine");
        machine.transform.SetParent(station.transform, false);
        machine.transform.SetPositionAndRotation(holderPos + dir * 0.15f, Quaternion.LookRotation(-dir, Vector3.up));
        Part(PrimitiveType.Cube, "Body", machine.transform, new Vector3(0f, 0.16f, 0f), new Vector3(0.2f, 0.32f, 0.18f), bodyMat, true);
        Part(PrimitiveType.Cube, "DripTray", machine.transform, new Vector3(0f, 0.006f, 0.15f), new Vector3(0.14f, 0.012f, 0.12f), metalMat, true);
        Part(PrimitiveType.Cube, "Spout", machine.transform, new Vector3(0f, 0.215f, 0.13f), new Vector3(0.05f, 0.03f, 0.09f), metalMat, true);

        var holder = new GameObject("CupHolder");
        holder.transform.SetParent(machine.transform, false);
        holder.transform.localPosition = new Vector3(0f, 0.012f, 0.15f);     // 받침 위 컵 바닥 자리

        var steamGo = new GameObject("Steam");
        steamGo.transform.SetParent(machine.transform, false);
        steamGo.transform.localPosition = new Vector3(0f, 0.11f, 0.15f);
        steamGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);    // 위로 뿜기
        ParticleSystem steam = MakeSteam(steamGo);

        var cm = machine.AddComponent<CoffeeMachine>();
        var cmSo = new SerializedObject(cm);
        cmSo.FindProperty("cupHolder").objectReferenceValue = holder.transform;
        cmSo.FindProperty("steam").objectReferenceValue = steam;
        cmSo.ApplyModifiedPropertiesWithoutUndo();

        // ── 작업 연결 ──
        var system = new GameObject("CoffeeSystem");
        system.transform.SetParent(station.transform, false);
        var task = system.AddComponent<CoffeeTask>();
        var so = new SerializedObject(task);
        so.FindProperty("arm").objectReferenceValue = arm;
        so.FindProperty("machine").objectReferenceValue = cm;
        so.FindProperty("cup").objectReferenceValue = cup.transform;
        so.FindProperty("cupSpot").objectReferenceValue = cupSpot.transform;
        so.FindProperty("graspHeight").floatValue = 0.045f;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (Object.FindAnyObjectByType<ScenarioManager>() == null)
        {
            var systems = new GameObject("GameSystems");
            Undo.RegisterCreatedObjectUndo(systems, "커피 스테이션 만들기");
            systems.AddComponent<ScenarioManager>();
        }

        EditorSceneManager.MarkSceneDirty(station.scene);
        Selection.activeGameObject = station;
        Debug.Log("[CoffeeStation] 컵·커피 머신·CoffeeTask를 만들었어요. Play → C 키로 시험, F5로 초기화.");
    }

    static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static ParticleSystem MakeSteam(GameObject go)
    {
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = 1.4f;
        main.startSpeed = 0.1f;
        main.startSize = 0.05f;
        main.startColor = new Color(1f, 1f, 1f, 0.3f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.02f;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 25f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.02f;

        Material particleMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat");
        if (particleMat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null) particleMat = MakeAsset(new Material(shader), "steam");
        }
        if (particleMat != null) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMat;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    static Material MakeMat(string name, Color color, float smoothness, float metallic)
    {
        string path = $"{MatFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        return MakeAsset(mat, name);
    }

    static Material MakeAsset(Material mat, string name)
    {
        string path = $"{MatFolder}/{name}.mat";
        mat.name = name;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
