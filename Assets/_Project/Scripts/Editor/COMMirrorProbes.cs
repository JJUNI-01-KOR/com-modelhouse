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
//   - 박스 크기는 프로브 자리에서 6방향으로 벽·바닥·천장까지 재서 맞춘다 (Box Projection → 각도 보정)
//   - Importance 10으로 구역 프로브(1)보다 우선, 거울이 박스 깊숙이 들어가게 벽 뒤까지 넓힘 (Forward+ 대응)
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

        // 3. 박스 = 프로브 자리에서 벽·바닥·천장까지
        float px = FreeDistance(probePos, Vector3.right), nx = FreeDistance(probePos, Vector3.left);
        float py = FreeDistance(probePos, Vector3.up),    ny = FreeDistance(probePos, Vector3.down);
        float pz = FreeDistance(probePos, Vector3.forward), nz = FreeDistance(probePos, Vector3.back);
        Vector3 min = probePos - new Vector3(nx, ny, nz);
        Vector3 max = probePos + new Vector3(px, py, pz);
        // 거울이 박스 '깊숙이' 들어가게 넓힌다.
        // Forward+는 렌더러별 프로브 지정(probeAnchor)을 무시하고 픽셀마다 박스 안의 프로브를 Importance 순으로 고른다.
        // 거울이 박스 면에 붙어 있으면 그 자리 가중치가 0에 가까워져 구역 프로브(소파 사진)가 그대로 비친다.
        Vector3 behind = b.center - normal * (thick + 0.5f);   // 벽 뒤로 0.5m
        min = Vector3.Min(min, Vector3.Min(b.min, behind) - Vector3.one * 0.3f);
        max = Vector3.Max(max, Vector3.Max(b.max, behind) + Vector3.one * 0.3f);

        probe.size = max - min;
        probe.center = (max + min) * 0.5f - probePos;
        probe.boxProjection = true;
        probe.importance = 10;  // 구역 프로브(1)보다 확실히 우선
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
