#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// 로봇팔 데모가 쓰는 Input Manager 축(Base, Shoulder, ...)을 프로젝트에 자동 추가한다.
// 이미 있는 축은 건드리지 않으므로 여러 번 실행돼도 안전하다.
// 수동 실행: 상단 메뉴 Tools > ArmRobot > Add Input Axes
[InitializeOnLoad]
public static class ArmRobotInputSetup
{
    // (축 이름, 음수 방향 키, 양수 방향 키) — 원본 데모 설정과 동일
    static readonly (string name, string neg, string pos)[] Axes =
    {
        ("Base",            "a", "d"),
        ("Shoulder",        "s", "w"),
        ("Elbow",           "q", "e"),
        ("Wrist1",          "o", "p"),
        ("Wrist2",          "k", "l"),
        ("Wrist3",          "n", "m"),
        ("Hand",            "v", "b"),
        ("Fingers",         "z", "x"),   // X: 닫기, Z: 열기
        ("BigHandVertical", "g", "h"),   // GripperScene용
    };

    static ArmRobotInputSetup()
    {
        EditorApplication.delayCall += AddMissingAxes;
    }

    [MenuItem("Tools/ArmRobot/Add Input Axes")]
    public static void AddMissingAxes()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning("[ArmRobot] InputManager.asset을 찾지 못했습니다.");
            return;
        }

        var so = new SerializedObject(assets[0]);
        SerializedProperty axesProp = so.FindProperty("m_Axes");
        int added = 0;

        foreach (var a in Axes)
        {
            if (HasAxis(axesProp, a.name)) continue;

            axesProp.arraySize++;
            SerializedProperty p = axesProp.GetArrayElementAtIndex(axesProp.arraySize - 1);

            p.FindPropertyRelative("m_Name").stringValue = a.name;
            p.FindPropertyRelative("descriptiveName").stringValue = "";
            p.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
            p.FindPropertyRelative("negativeButton").stringValue = "";
            p.FindPropertyRelative("positiveButton").stringValue = "";
            p.FindPropertyRelative("altNegativeButton").stringValue = a.neg;
            p.FindPropertyRelative("altPositiveButton").stringValue = a.pos;
            p.FindPropertyRelative("gravity").floatValue = 3f;
            p.FindPropertyRelative("dead").floatValue = 0.19f;
            p.FindPropertyRelative("sensitivity").floatValue = 3f;
            p.FindPropertyRelative("snap").boolValue = true;
            p.FindPropertyRelative("invert").boolValue = false;
            p.FindPropertyRelative("type").intValue = 0;   // Key or Mouse Button
            p.FindPropertyRelative("axis").intValue = 0;
            p.FindPropertyRelative("joyNum").intValue = 0;
            added++;
        }

        if (added > 0)
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[ArmRobot] Input Manager에 축 {added}개를 추가했습니다.");
        }
    }

    static bool HasAxis(SerializedProperty axesProp, string axisName)
    {
        for (int i = 0; i < axesProp.arraySize; i++)
        {
            SerializedProperty nameProp = axesProp.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name");
            if (nameProp != null && nameProp.stringValue == axisName) return true;
        }
        return false;
    }
}
#endif
