using UnityEditor;
using UnityEngine;

// 메뉴 Tools → COM → QA 자동 점검 (Play 중)
// QA 체크리스트 중 자동으로 확인할 수 있는 TC를 같은 절차로 실행한다 (리팩토링 뒤 회귀 테스트용).
// 결과: Console + C:\Unity\COM_Evidence\..._QA자동점검.txt
public static class QARunnerMenu
{
    [MenuItem("Tools/COM/QA 자동 점검 (Play 중)")]
    static void Run()
    {
        if (!Application.isPlaying) { EditorUtility.DisplayDialog("QA 자동 점검", "Play를 누른 뒤 실행하세요. (약 4분, 그동안 조작하지 않기)", "확인"); return; }
        if (Object.FindAnyObjectByType<QARunner>() != null) { Debug.LogWarning("[QA] 이미 실행 중"); return; }
        QARunner.Run();
    }
}
