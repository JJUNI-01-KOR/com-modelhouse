using UnityEditor;
using UnityEngine;

// 창문 통과 막기 (FR-01 "벽, 가구를 통과하지 못하게")
// 메뉴 없음 → COM → 전체 세팅·점검 (1~12)이 부른다 (COMSetupAll)
// - 이름이 sm_window로 시작하는 창문마다 BoxCollider를 붙인다 (메시 크기에 자동으로 맞춰짐)
// - 창은 얇아서 빠르게 걸으면 뚫릴 수 있으니, 가장 얇은 축을 최소 0.3m로 두껍게 한다
// - 이미 콜라이더가 있으면 건너뜀 (여러 번 눌러도 안전)
// MeshCollider 대신 BoxCollider를 쓰는 이유: 유리판처럼 한 면짜리 메시는 반대쪽에서 들어오면 막지 못할 수 있음
internal static class COMWindowColliders
{
    static readonly string[] Prefixes = { "sm_window" };
    const float MinThickness = 0.3f;

    public static void Setup(COMDay13.Report r)
    {
        r.Section("창문 충돌체 (FR-01)");
        int added = 0, already = 0;

        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
        {
            if (!Match(mf.gameObject.name) || mf.sharedMesh == null) continue;
            if (mf.GetComponent<Collider>() != null) { already++; continue; }

            BoxCollider box = Undo.AddComponent<BoxCollider>(mf.gameObject);   // 메시 크기에 자동으로 맞춰짐
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 size = mb.size;

            // 로컬 축 중 가장 얇은 축 (월드 기준 두께로 비교)
            Vector3 s = mf.transform.lossyScale;
            Vector3 world = new Vector3(Mathf.Abs(size.x * s.x), Mathf.Abs(size.y * s.y), Mathf.Abs(size.z * s.z));
            int thin = world.x <= world.y && world.x <= world.z ? 0 : (world.y <= world.z ? 1 : 2);
            if (world[thin] < MinThickness && Mathf.Abs(s[thin]) > 1e-4f)
                size[thin] = MinThickness / Mathf.Abs(s[thin]);

            box.center = mb.center;
            box.size = size;
            added++;
            r.Note($"{mf.gameObject.name}: BoxCollider 추가 (크기 {Vector3.Scale(size, s)})");
        }

        r.Check(added + already > 0, $"창문 충돌체: 새로 {added}개, 이미 있던 것 {already}개");
    }

    static bool Match(string n)
    {
        foreach (string p in Prefixes) if (n.StartsWith(p)) return true;
        return false;
    }
}
