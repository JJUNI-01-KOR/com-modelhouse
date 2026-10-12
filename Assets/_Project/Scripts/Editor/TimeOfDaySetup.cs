using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 Tools → COM → 시간대 컨트롤러 붙이기 (WBS 4.09·4.10)
// COM_System(없으면 만듦)에 TimeOfDayController를 붙이고 Directional Light를 햇빛으로 연결한다.
public static class TimeOfDaySetup
{
    [MenuItem("Tools/COM/시간대 컨트롤러 붙이기")]
    public static void Setup()
    {
        TimeOfDayController ctrl = Object.FindAnyObjectByType<TimeOfDayController>();
        if (ctrl == null)
        {
            GameObject sys = GameObject.Find("COM_System");
            if (sys == null)
            {
                sys = new GameObject("COM_System");
                Undo.RegisterCreatedObjectUndo(sys, "시간대 컨트롤러");
            }
            ctrl = Undo.AddComponent<TimeOfDayController>(sys);
        }

        Light sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) { sun = l; break; }

        var so = new SerializedObject(ctrl);
        if (sun != null) so.FindProperty("sun").objectReferenceValue = sun;
        so.ApplyModifiedProperties();
        if (sun != null && RenderSettings.sun == null) RenderSettings.sun = sun;

        EditorSceneManager.MarkSceneDirty(ctrl.gameObject.scene);
        Selection.activeObject = ctrl.gameObject;
        Debug.Log($"[TimeOfDay] {ctrl.gameObject.name}에 붙임, 햇빛 = {(sun != null ? sun.name : "없음")}. Ctrl+S로 저장", ctrl);
    }
}
