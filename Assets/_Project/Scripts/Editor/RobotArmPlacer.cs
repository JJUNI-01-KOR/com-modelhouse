using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 메뉴 Tools → COM → 로봇팔을 식탁 위에 올리기
// 로봇팔 밑면이 식탁 윗면에 딱 붙도록 높이를 맞춘다. CoffeeStation(컵·머신)도 같은 만큼 같이 옮긴다.
// - 로봇팔 바로 위에서 아래로 광선을 쏴서 로봇·커피 스테이션이 아닌 첫 번째 면(식탁)을 찾는다
// - 로봇 밑동(Base) 메쉬의 가장 아래가 그 면보다 2mm 위에 오게 한다
// Ctrl+Z로 되돌릴 수 있다.
public static class RobotArmPlacer
{
    const float Gap = 0.002f;

    [MenuItem("Tools/COM/로봇팔을 식탁 위에 올리기")]
    public static void Place()
    {
        RobotArm arm = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<RobotArm>()
            : null;
        if (arm == null) arm = Object.FindAnyObjectByType<RobotArm>();
        if (arm == null) { EditorUtility.DisplayDialog("로봇팔 올리기", "씬에 RobotArm이 없어요.", "확인"); return; }

        GameObject station = GameObject.Find("CoffeeStation");
        Physics.SyncTransforms();

        // 1) 로봇 밑동의 가장 아래 높이
        float baseBottom = BaseBottom(arm);

        // 2) 밑동 가운데 바로 위에서 아래로 광선 → 식탁 윗면
        Vector3 origin = new Vector3(arm.transform.position.x, baseBottom + 1.5f, arm.transform.position.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        float? surface = null;
        string surfaceName = "";
        foreach (RaycastHit h in hits)
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(arm.transform)) continue;
            if (station != null && t.IsChildOf(station.transform)) continue;
            surface = h.point.y; surfaceName = h.collider.name;
            break;
        }
        if (surface == null)
        {
            EditorUtility.DisplayDialog("로봇팔 올리기",
                "로봇 아래에서 식탁 콜라이더를 못 찾았어요.\n식탁에 콜라이더가 없으면 RobotArm의 Y를 직접 맞춰 주세요.", "확인");
            return;
        }

        float delta = surface.Value + Gap - baseBottom;
        Undo.RecordObject(arm.transform, "로봇팔 식탁 위에 올리기");
        arm.transform.position += Vector3.up * delta;
        if (station != null)
        {
            Undo.RecordObject(station.transform, "로봇팔 식탁 위에 올리기");
            station.transform.position += Vector3.up * delta;
        }
        Debug.Log($"[RobotArmPlacer] 식탁 면 '{surfaceName}' 높이 {surface.Value:F3}m에 맞춤 → {delta * 100f:+0.0;-0.0}cm 이동" +
                  (station != null ? " (CoffeeStation도 같이)" : ""), arm);
        EditorGUIUtility.PingObject(arm.gameObject);
    }

    // 첫 번째 ArticulationBody(밑동)와 그 자식 중 다른 관절이 아닌 것들의 렌더러로 밑면 높이를 구한다
    static float BaseBottom(RobotArm arm)
    {
        ArticulationBody root = arm.GetComponentInChildren<ArticulationBody>();
        var renderers = new List<Renderer>();
        if (root != null)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                ArticulationBody owner = r.GetComponentInParent<ArticulationBody>();
                if (owner == root && r.enabled && r.gameObject.activeInHierarchy) renderers.Add(r);
            }
        }
        if (renderers.Count == 0)
            foreach (Renderer r in arm.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.gameObject.activeInHierarchy) renderers.Add(r);

        float min = float.MaxValue;
        foreach (Renderer r in renderers) min = Mathf.Min(min, r.bounds.min.y);
        return min == float.MaxValue ? arm.transform.position.y : min;
    }
}