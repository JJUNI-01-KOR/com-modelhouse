using UnityEngine;

// 재실 감지 조명 (FR-04, FR-05, TC-04 ~ TC-07)
// - 입실: 현재 밝기에서 1초 이내에 100%
// - 퇴실: 3초 기다린 뒤 1초 동안 현재 밝기에서 0% (퇴실 4초 후 꺼짐)
// - 3초 안에 재입실: 끄지 않음 / 어두워지는 중 재입실: 멈추고 다시 100%
// 켜기·끄기 동작은 LightFader(재사용용 공통 클래스)에 있고, 여기서는 구역 사건만 연결한다.
public class ZoneLight : LightFader
{
    [SerializeField] ZoneVolume zone;

    public ZoneVolume Zone => zone;
    protected override string LogName => zone != null ? zone.DisplayName : name;

    void OnEnable()
    {
        HomeEvents.OnZoneEntered += OnZoneEntered;
        HomeEvents.OnZoneExited += OnZoneExited;
        HomeEvents.OnScenarioReset += ResetLight;
    }

    void OnDisable()
    {
        HomeEvents.OnZoneEntered -= OnZoneEntered;
        HomeEvents.OnZoneExited -= OnZoneExited;
        HomeEvents.OnScenarioReset -= ResetLight;
    }

    void OnZoneEntered(ZoneVolume z) { if (z == zone) TurnOn(); }
    void OnZoneExited(ZoneVolume z) { if (z == zone) TurnOff(); }
}
