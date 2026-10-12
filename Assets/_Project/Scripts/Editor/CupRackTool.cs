using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 Tools → COM → 컵 보관대 만들기
// 로봇 둘레(팔이 닿는 거리)에 원목 트레이 + 머그컵 3개를 부채꼴로 놓고, 찻잔 받침대도 팔이 닿는 자리로 옮긴다.
// 클릭할 때마다: 보관대에서 다음 컵을 집음 → 추출구 아래에서 커피 → 받침대에 놓음 → 3초 뒤 컵을 비워 보관대로 돌려놓음
// - 자리는 "로봇 → 커피 머신" 방향을 기준으로 정하고, 식탁 위가 아니면 반대쪽으로 놓는다
// - 다시 실행하면 보관대를 새로 만든다. Ctrl+Z로 되돌릴 수 있다.
public static class CupRackTool
{
    const int CupCount = 3;
    const float Radius = 0.36f;          // 로봇 밑동 중심에서 컵까지 수평 거리 (m)
    const float SaucerAngle = 60f;       // 머신 방향에서 받침대까지 각도
    const float RackStart = 100f;        // 첫 컵 각도
    const float RackStep = 26f;          // 컵 사이 각도 (지름 8cm 컵 + 손잡이 간격)
    const float TrayTop = 0.014f;

    [MenuItem("Tools/COM/컵 보관대 만들기")]
    public static void Build()
    {
        CoffeeTask task = Object.FindAnyObjectByType<CoffeeTask>();
        RobotArm arm = Object.FindAnyObjectByType<RobotArm>();
        CoffeeMachine machine = Object.FindAnyObjectByType<CoffeeMachine>();
        if (task == null || arm == null || machine == null)
        {
            EditorUtility.DisplayDialog("컵 보관대", "씬에 CoffeeTask·RobotArm·CoffeeMachine이 모두 있어야 해요.", "확인");
            return;
        }
        var so = new SerializedObject(task);
        Transform firstCup = so.FindProperty("cup").objectReferenceValue as Transform;
        if (firstCup == null && so.FindProperty("rackCups").arraySize > 0)
            firstCup = so.FindProperty("rackCups").GetArrayElementAtIndex(0).objectReferenceValue as Transform;
        if (firstCup == null) { EditorUtility.DisplayDialog("컵 보관대", "CoffeeTask에 컵이 연결돼 있지 않아요.", "확인"); return; }

        // 받침대가 없으면 먼저 만든다
        if (GameObject.Find("Saucer") == null) CoffeeSaucerTool.Build();
        Transform saucer = GameObject.Find("Saucer").transform;
        Transform station = task.transform.parent;

        Physics.SyncTransforms();
        Vector3 basePos = arm.transform.position;           // 로봇 밑면 = 식탁 높이 (로봇팔을 식탁 위에 올리기 메뉴 기준)
        Transform holder = machine.GetCupHolder();
        Vector3 toMachine = holder.position - basePos; toMachine.y = 0f;
        toMachine = toMachine.sqrMagnitude > 1e-6f ? toMachine.normalized : arm.transform.forward;

        // 왼쪽(+1)·오른쪽(-1) 중 식탁 위에 다 놓이는 쪽을 고른다
        float side = PickSide(basePos, toMachine, arm, machine);

        Vector3 Pos(float angle) => basePos + Quaternion.Euler(0f, side * angle, 0f) * toMachine * Radius;

        // ── 기존 보관대 정리 ──
        GameObject oldRack = GameObject.Find("CupRack");
        var keepCup = firstCup;
        if (oldRack != null)
        {
            // 예전 보관대 컵 중 첫 컵은 살리고 나머지는 지운다
            var rackProp = so.FindProperty("rackCups");
            for (int i = 0; i < rackProp.arraySize; i++)
            {
                var c = rackProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (c != null && c != keepCup) Undo.DestroyObjectImmediate(c.gameObject);
            }
            Undo.DestroyObjectImmediate(oldRack);
        }

        // ── 받침대: 머신 옆, 팔이 닿는 자리 ──
        Undo.RecordObject(saucer, "컵 보관대");
        Vector3 sp = Pos(SaucerAngle); sp.y = basePos.y;
        saucer.position = sp;
        saucer.rotation = Quaternion.LookRotation(toMachine, Vector3.up);

        // ── 원목 트레이 ──
        Material walnut = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/CoffeeMachine/cm_walnut.mat")
                          ?? MakeMat("rack_walnut", new Color(0.35f, 0.23f, 0.15f), 0.45f);
        var rack = new GameObject("CupRack");
        Undo.RegisterCreatedObjectUndo(rack, "컵 보관대");
        if (station != null) rack.transform.SetParent(station, true);
        float midAngle = RackStart + RackStep * (CupCount - 1) * 0.5f;
        Vector3 mid = Pos(midAngle); mid.y = basePos.y;
        Vector3 radial = (mid - basePos); radial.y = 0f; radial.Normalize();
        rack.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(radial, Vector3.up));
        float chord = 2f * Radius * Mathf.Sin(Mathf.Deg2Rad * RackStep * (CupCount - 1) * 0.5f) + 0.12f;
        Box(rack.transform, "Tray", new Vector3(0f, TrayTop * 0.5f, 0f), new Vector3(chord, TrayTop, 0.13f), walnut);
        Box(rack.transform, "LipFront", new Vector3(0f, TrayTop + 0.004f, 0.06f), new Vector3(chord, 0.008f, 0.008f), walnut);
        Box(rack.transform, "LipBack", new Vector3(0f, TrayTop + 0.004f, -0.06f), new Vector3(chord, 0.008f, 0.008f), walnut);

        // ── 컵 3개 ──
        var cups = new List<Transform>();
        for (int i = 0; i < CupCount; i++)
        {
            Transform c;
            if (i == 0) c = keepCup;
            else
            {
                GameObject dup = Object.Instantiate(keepCup.gameObject, keepCup.parent);
                Undo.RegisterCreatedObjectUndo(dup, "컵 보관대");
                c = dup.transform;
            }
            Undo.RecordObject(c, "컵 보관대");
            c.name = $"Cup_{i + 1}";
            Vector3 p = Pos(RackStart + RackStep * i);
            p.y = basePos.y + TrayTop;
            Vector3 outward = p - basePos; outward.y = 0f; outward.Normalize();
            // 손잡이(컵 로컬 +X)를 로봇 반대쪽으로 → 그리퍼가 옆에서 잡을 때 걸리지 않음
            c.SetPositionAndRotation(p, Quaternion.LookRotation(outward, Vector3.up) * Quaternion.Euler(0f, -90f, 0f));
            Transform liquid = c.Find("CoffeeLiquid");
            if (liquid != null) liquid.gameObject.SetActive(false);
            cups.Add(c);
        }

        // ── CoffeeTask 연결 ──
        Undo.RecordObject(task, "컵 보관대");
        var arr = so.FindProperty("rackCups");
        arr.arraySize = cups.Count;
        for (int i = 0; i < cups.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = cups[i];
        so.FindProperty("cup").objectReferenceValue = cups[0];
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(rack.scene);
        Selection.activeObject = rack;
        Debug.Log($"[CupRack] 컵 보관대 완료: 컵 {cups.Count}개, 받침대는 머신 옆 {Radius:F2}m. 클릭마다 다음 컵 → 커피 → 받침대 → 3초 뒤 보관대로. Ctrl+S로 저장", rack);
    }

    // 받침대와 컵 자리가 모두 식탁 면 위에 있는 쪽을 고른다 (둘 다 아니면 +1)
    static float PickSide(Vector3 basePos, Vector3 toMachine, RobotArm arm, CoffeeMachine machine)
    {
        foreach (float s in new[] { 1f, -1f })
        {
            bool ok = true;
            float[] angles = { SaucerAngle, RackStart, RackStart + RackStep, RackStart + RackStep * (CupCount - 1) };
            foreach (float a in angles)
            {
                Vector3 p = basePos + Quaternion.Euler(0f, s * a, 0f) * toMachine * Radius;
                if (!OnTable(p, basePos.y, arm.transform, machine.transform)) { ok = false; break; }
            }
            if (ok) return s;
        }
        Debug.LogWarning("[CupRack] 양쪽 다 식탁 끝에 걸려요. 놓인 뒤 CupRack·Saucer를 직접 옮겨 주세요 (로봇에서 0.5m 안)");
        return 1f;
    }

    static bool OnTable(Vector3 p, float tableY, Transform robot, Transform machine)
    {
        RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 1f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(robot) || t.IsChildOf(machine)) return false;     // 로봇·머신과 겹침
            GameObject stationGo = GameObject.Find("CoffeeStation");
            if (t.name.StartsWith("Cup") || t.name == "Saucer" || (stationGo != null && t.IsChildOf(stationGo.transform))) continue;
            return Mathf.Abs(h.point.y - tableY) < 0.03f;
        }
        return false;
    }

    static void Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(go, "컵 보관대");
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    static Material MakeMat(string name, Color color, float smoothness)
    {
        string path = $"Assets/_Project/Materials/Coffee/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
