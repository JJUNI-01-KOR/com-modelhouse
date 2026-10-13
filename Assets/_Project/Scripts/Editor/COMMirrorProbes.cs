using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 거울 반사 바로잡기 (NFR-10 화면 품질)
// 메뉴 없음 → COM → 전체 세팅·점검 (1~12)이 부른다 (COMSetupAll)
//
// 문제: 거울(m_mirror)이 구역 반사 프로브(RP_LivingMain 등)를 그대로 써서,
//       프로브가 찍힌 자리(방 가운데)에서 본 모습이 비친다 → 주방 거울에 소파, 침실 앞 거울에 엉뚱한 벽.
// 해결: 거울마다 바로 앞(0.6m)에 전용 반사 프로브를 하나씩 두고 그 자리에서 다시 찍는다.
//   - 박스는 거울 둘레만 덮는 얇은 판 (10/13: 방 전체를 덮으면 광택 바닥에 직사각형 경계가 생겨서 줄임)
//   - Importance 10으로 구역 프로브(1)보다 우선, 거울이 박스 안에 들어가게 벽 뒤까지 넓힘 (Forward+ 대응)
//   - 시간대 밝기는 실행 중 ReflectionTimeOfDay가 조절
//   - 거울 렌더러는 반사 프로브를 하나만 쓰게(Simple) 해서 다른 프로브와 섞이지 않게 한다
//   - 찍은 결과는 Assets/_Project/Settings/MirrorProbes/에 저장해 Custom 텍스처로 연결 (전체 굽기 없이 바로 반영)
// 한계: 프로브 반사라 시점을 따라 움직이지 않고, 플레이어 자신은 비치지 않는다 (실시간 거울은 FPS 부담이 커서 안 씀).
internal static class COMMirrorProbes
{
    const string MirrorMaterialPrefix = "m_mirror";
    const string RootName = "MirrorProbes";
    const string SaveFolder = "Assets/_Project/Settings/MirrorProbes";
    const float FrontDistance = 0.6f;
    const float MaxRay = 15f;

    public static void Setup(COMDay13.Report r)
    {
        r.Section("거울 반사 프로브 (NFR-10)");

        int count = 0;
        foreach (Renderer rd in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (!UsesMirror(rd)) continue;
            count++;
            SetupOne(rd, count, r);
        }
        r.Check(count > 0, $"거울 {count}개 처리 (재질 이름이 '{MirrorMaterialPrefix}'로 시작하는 렌더러)");
    }

    static bool UsesMirror(Renderer rd)
    {
        foreach (Material m in rd.sharedMaterials)
            if (m != null && m.name.StartsWith(MirrorMaterialPrefix)) return true;
        return false;
    }

    static void SetupOne(Renderer mirror, int index, COMDay13.Report r)
    {
        Bounds b = mirror.bounds;

        // 1. 거울이 바라보는 방향 = 가장 얇은 수평 축 중 방 쪽(앞이 더 넓은 쪽)
        Vector3 axis = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
        float thick = Vector3.Scale(b.extents, axis).magnitude;
        float pos = FreeDistance(b.center + axis * (thick + 0.05f), axis);
        float neg = FreeDistance(b.center - axis * (thick + 0.05f), -axis);
        Vector3 normal = pos >= neg ? axis : -axis;

        Vector3 probePos = b.center + normal * (thick + FrontDistance);

        // 2. 프로브 만들기 (있으면 재사용)
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "거울 프로브");
        }
        // 이름은 거울 이름 기준 (순서 번호를 붙이면 찾는 순서가 바뀔 때마다 새 프로브가 생겨 중복됨)
        string probeName = $"RP_Mirror_{mirror.name}";
        Transform t = root.transform.Find(probeName);
        // 예전 이름(RP_Mirror_<번호>_<거울>)으로 만든 것: 하나는 이름만 바꿔 재사용, 나머지는 지움
        var olds = new System.Collections.Generic.List<Transform>();
        foreach (Transform ch in root.transform)
            if (ch != t && ch.name.StartsWith("RP_Mirror_") && ch.name.EndsWith("_" + mirror.name)) olds.Add(ch);
        foreach (Transform o in olds)
        {
            if (t == null) { Undo.RecordObject(o.gameObject, "거울 프로브 이름"); o.name = probeName; t = o; }
            else { r.Note($"중복 거울 프로브 삭제: {o.name}"); Undo.DestroyObjectImmediate(o.gameObject); }
        }
        ReflectionProbe probe;
        if (t == null)
        {
            GameObject go = new GameObject(probeName);
            Undo.RegisterCreatedObjectUndo(go, "거울 프로브");
            go.transform.SetParent(root.transform, false);
            probe = go.AddComponent<ReflectionProbe>();
        }
        else
        {
            probe = t.GetComponent<ReflectionProbe>();
            if (probe == null) probe = Undo.AddComponent<ReflectionProbe>(t.gameObject);
        }

        Undo.RecordObject(probe.transform, "거울 프로브 위치");
        Undo.RecordObject(probe, "거울 프로브 설정");
        probe.transform.position = probePos;

        // 3. 박스 = 거울 둘레만 덮는 얇은 판 (10/13 강사 피드백: 현관 바닥에 직사각형 경계)
        //    예전에는 박스를 벽~맞은편 벽·바닥~천장까지 넓혀서, Forward+가 픽셀마다 Importance 높은 이 프로브를 고르는 바람에
        //    광택 바닥도 박스 안쪽만 거울용 반사를 써서 경계가 생겼다.
        //    → 거울 면 앞뒤로만 얇게(벽 뒤 0.5m ~ 방 쪽 0.25m), 옆·위아래는 거울 크기 + 0.3m.
        //    박스가 얇아 Box Projection은 끈다 (프로브 자리에서 본 반사를 그대로 씀).
        Vector3 front = b.center + normal * (thick + 0.25f);   // 방 쪽 0.25m (바닥·벽 대부분은 박스 밖)
        Vector3 behind = b.center - normal * (thick + 0.5f);   // 벽 뒤로 0.5m
        Vector3 side = Vector3.one * 0.3f;
        Vector3 min = Vector3.Min(Vector3.Min(front, behind), b.min - side);
        Vector3 max = Vector3.Max(Vector3.Max(front, behind), b.max + side);
        // 앞뒤 축은 front~behind로만 자른다 (b.min/max ± 0.3 이 방 쪽으로 더 나가지 않게)
        if (normal.x != 0f) { min.x = Mathf.Min(front.x, behind.x); max.x = Mathf.Max(front.x, behind.x); }
        else                { min.z = Mathf.Min(front.z, behind.z); max.z = Mathf.Max(front.z, behind.z); }
        min.y = b.min.y;   // 아래로는 넓히지 않음 (바닥까지 내려오는 전신 거울이어도 바닥은 박스 밖)

        probe.size = max - min;
        probe.center = (max + min) * 0.5f - probePos;
        probe.boxProjection = false;
        probe.importance = 10;  // 구역 프로브(1)보다 확실히 우선 (이제 박스가 거울 둘레뿐이라 바닥에는 영향 없음)
        probe.blendDistance = 0f;   // 경계에서 구역 프로브와 섞이지 않게
        probe.resolution = 256;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;

        // 4. 거울은 이 프로브 하나만 쓰게
        Undo.RecordObject(mirror, "거울 반사 설정");
        mirror.reflectionProbeUsage = ReflectionProbeUsage.Simple;
        mirror.probeAnchor = probe.transform;

        // 5. 바로 찍어서 Custom 텍스처로 연결
        if (!AssetDatabase.IsValidFolder(SaveFolder))
        {
            Directory.CreateDirectory(SaveFolder);
            AssetDatabase.Refresh();
        }
        string path = $"{SaveFolder}/{probeName}.exr";
        probe.mode = ReflectionProbeMode.Custom;
        bool baked = Lightmapping.BakeReflectionProbe(probe, path);
        if (baked)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
        }

        r.Check(baked && probe.customBakedTexture != null,
            $"{mirror.name}: 앞쪽 {(normal.x != 0 ? (normal.x > 0 ? "+X" : "-X") : (normal.z > 0 ? "+Z" : "-Z"))}, " +
            $"프로브 {probe.transform.position}, 박스 {probe.size}, 저장 {path}");
    }

    // origin에서 dir로 가장 가까운 벽까지 거리 (트리거·플레이어는 무시)
    static float FreeDistance(Vector3 origin, Vector3 dir)
    {
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, MaxRay, ~0, QueryTriggerInteraction.Ignore);
        float best = MaxRay;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
            if (h.distance < best) best = h.distance;
        }
        return best >= MaxRay ? 3f : best;   // 아무것도 안 맞으면 3m로 둔다
    }
}
