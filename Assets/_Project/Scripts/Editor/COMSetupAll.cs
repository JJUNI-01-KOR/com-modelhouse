using UnityEditor;
using UnityEngine;

/// <summary>
/// 상단 메뉴 COM → 전체 세팅·점검 (1~12 통합, 이 메뉴 하나만 남김)
///
/// 몇 번을 눌러도 안전하다:
///   - 10/8 기본 세팅(구역, 플레이어, 충돌체, UI, 옵션 대상)은 씬에 없을 때만 만든다. 이미 있으면 건너뜀.
///   - 10/12 세팅(액자·화분, 시스템, 시각 버튼, 기록 패널, 현관 조명, 시간대 컨트롤러)은 빠진 것만 채운다.
///   - 10/13 화면 품질(NFR-10)·UI 정리(5.08)는 같은 결과로 맞춘다.
///   - 마지막에 씬 연결·문 높이(NFR-03)·UI 문구·3클릭을 점검해서 결과를 txt 하나로 남긴다.
/// 결과 파일: C:\Unity\COM_Evidence\날짜_시각_setup_check.txt (5.10 증거)
/// </summary>
internal static class COMSetupAll
{
    [MenuItem("COM/전체 세팅·점검 (1~12)")]
    static void Run()
    {
        if (!EditorUtility.DisplayDialog("COM 전체 세팅·점검",
                "씬에 빠진 것만 채우고, 화면 품질 설정을 맞춘 뒤 점검 결과를 남겨.\n이미 있는 UI·오브젝트는 다시 만들지 않아.\n\n진행할까?",
                "진행", "취소"))
            return;

        var r = new COMDay13.Report("COM 전체 세팅·점검");
        Undo.SetCurrentGroupName("COM 전체 세팅·점검");
        int group = Undo.GetCurrentGroup();

        // ── 1~8: 10/8 기본 세팅 (없을 때만) ─────────────────────
        r.Section("기본 세팅 (10/8) — 없을 때만 실행");
        GameObject zoneRoot = GameObject.Find("Zone");
        if (zoneRoot == null) r.Check(false, "구역: Hierarchy에 'Zone' 오브젝트가 없음 → 구역 박스부터 배치 필요");
        else if (NeedsZones(zoneRoot)) { COMSetupTools.SetupZones(); r.Note("구역: ZoneVolume·ZoneData 연결"); }
        else r.Note("구역: 이미 세팅됨 → 건너뜀");

        if (Object.FindAnyObjectByType<PlayerController>() == null) { COMSetupTools.SetupPlayer(); r.Note("플레이어: Player·SpawnPoint 생성 → SpawnPoint를 현관으로 옮길 것"); }
        else r.Note("플레이어: 이미 있음 → 건너뜀");

        COMSetupTools.AddStructureColliders();   // 빠진 바닥·벽 충돌체만 추가
        r.Note("바닥·벽 충돌체: 빠진 것만 추가");

        if (GameObject.Find("COM_UI") == null) { COMSetupTools.BuildUI(); r.Note("UI: 메뉴·옵션 패널·기록 패널·구역 정보 창 생성"); }
        else r.Note("UI: 이미 있음 → 건너뜀 (손으로 바꾼 위치 유지)");

        var catalog = AssetDatabase.LoadAssetAtPath<OptionCatalog>("Assets/_Project/Data/OptionCatalog.asset");
        if (catalog == null || catalog.Items.Count == 0) { OptionCatalogBuilder.Build(); r.Note("옵션 카탈로그: 부록 A-7로 생성"); }
        else r.Note($"옵션 카탈로그: 이미 있음 ({catalog.Items.Count}개) → 건너뜀");

        if (GameObject.Find("Option_Countertop") == null || GameObject.Find("Option_Sofa") == null || GameObject.Find("Option_Rug") == null)
        { COMSetupTools.AddFurnitureOptions(); r.Note("상판·소파·러그: 대상 생성"); }
        else r.Note("상판·소파·러그: 이미 있음 → 건너뜀");

        // ── 9: 10/12 세팅 ───────────────────────────────────────
        r.Section("10/12 세팅 — 빠진 것만 채움");
        COMSetup1012.Setup();
        r.Note("액자·화분 대상, COM_System(옵션 상태·초기화), 옵션 패널·HUD 연결, 기록 패널, 시각 버튼 확인");

        // ── 10/12 오후: 현관 조명, 시간대 컨트롤러 ──────────────
        COMEntranceLight.Setup(r);
        r.Section("시간대 컨트롤러 (FR-30)");
        TimeOfDaySetup.Setup();   // 김찬중 코드: 없을 때만 COM_System에 붙이고 햇빛 연결
        var tod = Object.FindAnyObjectByType<TimeOfDayController>();
        r.Check(tod != null, tod != null ? $"시간대 컨트롤러: {tod.gameObject.name}에 붙어 있음" : "시간대 컨트롤러: 없음");

        // ── 10/12 오후: 거울 반사 (NFR-10) ──────────────────────
        COMMirrorProbes.Setup(r);

        // ── 11, 12: 10/13 화면 품질·UI 정리 ─────────────────────
        COMDay13.SetupQuality(r);
        COMDay13.CheckUI(r);

        // ── 10, 7: 점검 ─────────────────────────────────────────
        COMDay13.CheckScene(r);
        MeasureDoors(r);

        Undo.CollapseUndoOperations(group);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        string path = r.Finish("setup_check");
        bool bake = EditorUtility.DisplayDialog("COM 전체 세팅·점검 끝",
            $"통과 {r.Ok} / 문제 {r.Fail}\n\n결과: {path}\n\n" +
            "씬을 저장하고 조명 굽기(Baked Indirect)를 시작할까?\n몇 분 걸리고, 진행 상황은 오른쪽 아래에 나와.",
            "저장하고 굽기", "나중에");
        if (bake)
        {
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Lightmapping.BakeAsync();
        }
    }

    static bool NeedsZones(GameObject root)
    {
        foreach (Transform t in root.transform)
        {
            if (!t.name.StartsWith("Zone_")) continue;
            ZoneVolume z = t.GetComponent<ZoneVolume>();
            if (z == null || (z.Data == null && !z.IsSensorOnly)) return true;
        }
        return false;
    }

    // NFR-03, TC-39: 모델 배율 1, 문 높이(에셋 기본값 약 2.5m)
    static void MeasureDoors(COMDay13.Report r)
    {
        r.Section("공간 크기 (NFR-03, TC-39)");
        GameObject apt = GameObject.Find("open_concept_apartment_unity");
        if (apt != null)
        {
            Vector3 s = apt.transform.lossyScale;
            r.Check(Mathf.Approximately(s.x, 1f) && Mathf.Approximately(s.y, 1f) && Mathf.Approximately(s.z, 1f), $"아파트 Transform Scale = {s}");
            Object src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(apt);
            string path = src != null ? AssetDatabase.GetAssetPath(src) : "";
            if (AssetImporter.GetAtPath(path) is ModelImporter mi)
                r.Note($"FBX Scale Factor = {mi.globalScale}, Convert Units = {mi.useFileScale}, 파일 단위 배율 = {mi.fileScale}");
        }
        else r.Note("open_concept_apartment_unity 오브젝트를 못 찾음 (이름이 바뀌었으면 직접 확인)");

        int doors = 0;
        foreach (Renderer rd in Object.FindObjectsByType<Renderer>())
        {
            string n = rd.name.ToLower();
            if (!n.StartsWith("sm_door") && !n.StartsWith("sm_closet_door")) continue;
            Vector3 size = rd.bounds.size;
            r.Note($"{rd.name}: 높이 {size.y:0.00}m, 폭 {Mathf.Max(size.x, size.z):0.00}m");
            doors++;
        }
        r.Check(doors > 0, $"문 {doors}개 측정");
        Camera cam = Camera.main;
        if (cam != null) r.Note($"비교용 눈높이(카메라) y = {cam.transform.position.y:0.00}m");
    }
}
