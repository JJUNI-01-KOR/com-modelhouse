/// <summary>
/// 시각 버튼 UI(ClockUI, 권오민 4.08)와 시간대 처리(TimeOfDayController, 김찬중 4.09·4.10) 사이의 약속 (FR-30)
/// - ClockUI는 이 인터페이스만 알고, TimeOfDayController를 직접 참조하지 않는다.
/// - TimeOfDayController가 이걸 구현하면 시각 버튼이 자동으로 찾아 연결한다 (4.11).
/// - SRS v2.4: 시각은 버튼 5개(06·09·12·18·22시)로만 바뀐다. 보간은 필요 없고 A-8 값을 그대로 쓰면 된다.
///
/// 구현할 때 지킬 것
/// - SetHour(hour): 햇빛·하늘·환경광을 바꾸고 HomeEvents.RaiseHourChanged(hour)를 보낸다.
///   이벤트 기록("시각 변경 18:00")은 ClockUI가 버튼을 누를 때 남기므로 여기서는 Log 하지 않는다.
/// - 초기화(FR-26 8번)는 각자 HomeEvents.OnScenarioReset을 구독해 12시로 되돌리고 RaiseHourChanged(12)를 보낸다.
/// </summary>
public interface ITimeOfDay
{
    /// <summary>현재 시각 0 ~ 23</summary>
    int CurrentHour { get; }

    /// <summary>시각 변경 (1시간 단위)</summary>
    void SetHour(int hour);

    /// <summary>구역 조도(lx) = 창 쪽 햇빛 조도 × 구역 계수 + 천장 조명 켜짐 300 (부록 A-8)</summary>
    float GetZoneLux(ZoneVolume zone);
}
