using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 메뉴 Tools → COM → 로봇팔 사용 안내판 만들기
// 로봇팔 앞 식탁 위에 사용 방법을 적은 안내판(월드 공간 글자)을 눕혀 놓는다.
// 위치: 로봇 밑동에서 컵 보관대·받침대·머신·싱크대의 반대쪽(빈 자리)으로 30cm. 글자 위쪽이 로봇을 향한다.
// 만든 뒤 Scene 뷰에서 옮기거나 돌려서 맞추면 된다 (다시 실행하면 같은 이름이면 위치는 두고 글자만 새로 씀).
internal static class RobotGuideBoardTool
{
    const string BoardName = "RobotGuideBoard";
    const string Guide =
        "<b>커피 로봇팔</b>\n" +
        "C  =  동작\n" +
        "X  =  일시정지 및 이어서 진행\n" +
        "Z  =  정지";

    [MenuItem("Tools/COM/로봇팔 사용 안내판 만들기")]
    static void Create()
    {
        RobotArm arm = Object.FindAnyObjectByType<RobotArm>();
        if (arm == null) { EditorUtility.DisplayDialog("안내판", "씬에 RobotArm이 없어요.", "확인"); return; }

        GameObject board = GameObject.Find(BoardName);
        bool isNew = board == null;
        if (isNew)
        {
            board = new GameObject(BoardName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(board, "로봇팔 안내판");
            var canvas = board.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)board.transform;
            rt.sizeDelta = new Vector2(360f, 120f);           // 0.001배 → 36cm × 12cm
            rt.localScale = Vector3.one * 0.001f;
            Place(arm, board.transform);
        }
        else Undo.RecordObject(board, "로봇팔 안내판");

        Transform old = board.transform.Find("Text");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        Transform bgOld = board.transform.Find("Background");
        if (bgOld != null) Undo.DestroyObjectImmediate(bgOld.gameObject);

        Image bg = UIFactory.CreatePanel(board.transform, "Background", new Color(0.08f, 0.09f, 0.11f, 0.55f));
        UIFactory.Stretch(bg.rectTransform);
        bg.raycastTarget = false;
        Text t = UIFactory.CreateText(board.transform, "Text", Guide, 22, TextAnchor.MiddleCenter);
        UIFactory.Stretch(t.rectTransform, 8f);
        t.lineSpacing = 1.1f;
        Undo.RegisterCreatedObjectUndo(bg.gameObject, "안내판 배경");
        Undo.RegisterCreatedObjectUndo(t.gameObject, "안내판 글자");

        Selection.activeGameObject = board;
        EditorGUIUtility.PingObject(board);
        Debug.Log($"[안내판] {(isNew ? "만들었어요" : "글자를 새로 썼어요")}: {board.transform.position} — Scene 뷰에서 위치·방향을 맞춘 뒤 저장(Ctrl+S)");
    }

    static void Place(RobotArm arm, Transform board)
    {
        Vector3 b = arm.transform.position;
        // 작업 물건들의 평균 위치 반대쪽 = 비어 있는 식탁 앞쪽
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (string key in new[] { "CupRack", "Saucer", "CoffeeMachine", "sm_sink" })
        {
            GameObject g = GameObject.Find(key);
            if (g == null) continue;
            Vector3 d = g.transform.position - b; d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) continue;
            sum += d.normalized; n++;
        }
        Vector3 dir = n > 0 && sum.sqrMagnitude > 1e-4f ? -sum.normalized : -arm.transform.forward;
        dir.y = 0f; dir.Normalize();
        board.position = b + dir * 0.30f + Vector3.up * 0.003f;
        // 식탁에 눕히고, 글자 위쪽이 로봇을 향하게 (앞에 선 사람이 바로 읽음)
        board.rotation = Quaternion.LookRotation(Vector3.down, -dir);
    }
}
