using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 Tools → COM → 찻잔 받침대 만들기
// - 컵이 처음 놓인 자리(CupSpot)에 도자기 받침대(Saucer)를 만들고 컵을 그 위에 올린다
// - 받침대 윗면에 TableSpot을 두고 CoffeeTask의 "커피를 다 받은 컵을 놓을 곳"으로 연결한다
//   → 로봇이 받침대 위 컵을 집어 커피를 받고, 다시 받침대 위에 놓는다
// - 받침대를 다른 곳으로 옮기면 커피 받은 컵은 옮긴 받침대 위에 놓인다 (팔이 닿는 범위 안에서)
// 다시 실행하면 기존 Saucer를 지우고 새로 만든다. Ctrl+Z로 되돌릴 수 있다.
public static class CoffeeSaucerTool
{
    const float SaucerTop = 0.011f;   // 받침대 윗면 높이 (m)

    [MenuItem("Tools/COM/찻잔 받침대 만들기")]
    public static void Build()
    {
        CoffeeTask task = Object.FindAnyObjectByType<CoffeeTask>();
        if (task == null) { EditorUtility.DisplayDialog("찻잔 받침대", "씬에 CoffeeTask가 없어요. 먼저 커피 스테이션을 만드세요.", "확인"); return; }

        var so = new SerializedObject(task);
        var cup = so.FindProperty("cup").objectReferenceValue as Transform;
        var cupSpot = so.FindProperty("cupSpot").objectReferenceValue as Transform;
        if (cup == null) { EditorUtility.DisplayDialog("찻잔 받침대", "CoffeeTask에 컵이 연결돼 있지 않아요.", "확인"); return; }

        Transform station = task.transform.parent != null ? task.transform.parent : null;

        // 기존 받침대 정리 (컵·CupSpot 높이를 원래대로 내린 뒤 다시 만든다)
        GameObject old = GameObject.Find("Saucer");
        if (old != null)
        {
            Undo.RecordObject(cup, "찻잔 받침대");
            cup.position -= Vector3.up * SaucerTop;
            if (cupSpot != null) { Undo.RecordObject(cupSpot, "찻잔 받침대"); cupSpot.position -= Vector3.up * SaucerTop; }
            Undo.DestroyObjectImmediate(old);
        }

        Vector3 basePos = cupSpot != null ? cupSpot.position : cup.position;
        Quaternion baseRot = cupSpot != null ? cupSpot.rotation : cup.rotation;

        Material ceramic = MakeMat("saucer_ceramic", new Color(0.96f, 0.95f, 0.92f), 0.82f);
        Material rim = MakeMat("saucer_rim", new Color(0.69f, 0.55f, 0.34f), 0.75f, 1f);   // 브라스 테두리 (로봇 포인트색과 맞춤)

        var saucer = new GameObject("Saucer");
        Undo.RegisterCreatedObjectUndo(saucer, "찻잔 받침대");
        if (station != null) saucer.transform.SetParent(station, true);
        saucer.transform.SetPositionAndRotation(basePos, baseRot);

        // 지름 14cm 접시: 바닥 굽 → 넓은 몸통 → 브라스 테두리 → 가운데 컵 자리
        Part(saucer.transform, "Foot", new Vector3(0f, 0.0015f, 0f), new Vector3(0.08f, 0.0015f, 0.08f), ceramic);
        Part(saucer.transform, "Plate", new Vector3(0f, 0.0055f, 0f), new Vector3(0.14f, 0.0025f, 0.14f), ceramic);
        Part(saucer.transform, "Rim", new Vector3(0f, 0.0085f, 0f), new Vector3(0.142f, 0.0006f, 0.142f), rim);
        Part(saucer.transform, "Well", new Vector3(0f, 0.0098f, 0f), new Vector3(0.124f, 0.0012f, 0.124f), ceramic);

        // 커피 받은 컵을 놓을 곳 = 받침대 윗면 가운데
        var spot = new GameObject("TableSpot");
        Undo.RegisterCreatedObjectUndo(spot, "찻잔 받침대");
        spot.transform.SetParent(saucer.transform, false);
        spot.transform.localPosition = new Vector3(0f, SaucerTop, 0f);

        // 처음 컵도 받침대 위로
        Undo.RecordObject(cup, "찻잔 받침대");
        cup.SetPositionAndRotation(basePos + Vector3.up * SaucerTop, baseRot);
        if (cupSpot != null)
        {
            Undo.RecordObject(cupSpot, "찻잔 받침대");
            cupSpot.position = basePos + Vector3.up * SaucerTop;   // 초기화 때도 받침대 위로 돌아오게
        }

        Undo.RecordObject(task, "찻잔 받침대");
        so.FindProperty("tableSpot").objectReferenceValue = spot.transform;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(saucer.scene);
        Selection.activeObject = saucer;
        Debug.Log("[Saucer] 찻잔 받침대 완료: 컵을 받침대 위에서 집어 커피를 받고 다시 받침대 위에 놓음. 받침대를 옮기면 놓는 자리도 따라감. Ctrl+S로 저장", saucer);
    }

    static void Part(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Undo.RegisterCreatedObjectUndo(go, "찻잔 받침대");
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    static Material MakeMat(string name, Color color, float smoothness, float metallic = 0f)
    {
        const string folder = "Assets/_Project/Materials/Coffee";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials")) AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            AssetDatabase.CreateFolder("Assets/_Project/Materials", "Coffee");
        }
        string path = $"{folder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = m == null;
        if (isNew) m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
        return m;
    }
}
