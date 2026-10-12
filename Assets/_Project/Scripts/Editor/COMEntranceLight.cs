using UnityEditor;
using UnityEngine;

// 현관 재실 감지 조명 연결 (FR-04, FR-05)
// 메뉴 없음 → COM → 전체 세팅·점검 (1~12)이 부른다 (COMSetupAll)
// - Zone_Entrance 아래에 실시간 Point Light "EntranceLight"를 만든다 (천장 조명 sm_light.004 바로 아래)
// - Zone_Entrance에 ZoneLight를 붙이고 zone = Zone_Entrance, lights = [EntranceLight]로 연결한다
// - 이미 있으면 새로 만들지 않고 연결만 다시 맞춘다 (여러 번 눌러도 안전, 손으로 바꾼 위치·세기 유지)
// 기존 Zone_Entrance/Light(Area, Baked)는 굽기용이라 밝기를 바꿀 수 없어서 건드리지 않는다.
internal static class COMEntranceLight
{
    const string ZoneName = "Zone_Entrance";
    const string LightName = "EntranceLight";
    const string FixtureName = "sm_light.004";

    public static void Setup(COMDay13.Report r)
    {
        r.Section("현관 조명 (FR-04, FR-05)");

        GameObject zoneGo = GameObject.Find(ZoneName);
        ZoneVolume zone = zoneGo != null ? zoneGo.GetComponent<ZoneVolume>() : null;
        if (zone == null)
        {
            r.Check(false, $"현관 조명: {ZoneName}(ZoneVolume)을 못 찾음");
            return;
        }

        // 1. 실시간 조명 (없을 때만 만듦)
        Transform lightTf = zone.transform.Find(LightName);
        Light light;
        if (lightTf == null)
        {
            GameObject lightGo = new GameObject(LightName);
            Undo.RegisterCreatedObjectUndo(lightGo, "현관 조명 만들기");
            lightGo.transform.SetParent(zone.transform, false);

            GameObject fixture = GameObject.Find(FixtureName);
            lightGo.transform.position = fixture != null
                ? fixture.transform.position + Vector3.down * 0.25f
                : zone.transform.position + Vector3.up * 1.4f;

            light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.92f, 0.8f);   // 따뜻한 실내등 (약 3,500K)
            light.intensity = 3f;
            light.range = 5f;
            light.shadows = LightShadows.Soft;
            r.Note($"현관 조명: {LightName} 생성 (위치 {light.transform.position})");
        }
        else
        {
            light = lightTf.GetComponent<Light>();
            if (light == null) light = Undo.AddComponent<Light>(lightTf.gameObject);
            r.Note($"현관 조명: {LightName} 이미 있음 → 위치·세기 유지");
        }
        Undo.RecordObject(light, "현관 조명 설정");
        light.lightmapBakeType = LightmapBakeType.Realtime; // Baked면 밝기가 안 바뀜

        // 2. ZoneLight 붙이고 연결
        ZoneLight zl = zone.GetComponent<ZoneLight>();
        if (zl == null) zl = Undo.AddComponent<ZoneLight>(zone.gameObject);

        SerializedObject so = new SerializedObject(zl);
        so.FindProperty("zone").objectReferenceValue = zone;
        SerializedProperty lights = so.FindProperty("lights");
        lights.arraySize = 1;
        lights.GetArrayElementAtIndex(0).objectReferenceValue = light;
        so.ApplyModifiedProperties();

        r.Check(zl.Zone == zone, $"현관 조명: {ZoneName}에 ZoneLight 연결 (조명 {light.name}, 세기 {light.intensity})");
    }
}
