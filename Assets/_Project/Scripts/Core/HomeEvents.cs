using System;
using UnityEngine;

// 중앙 이벤트 관리자 (SRS v2.0 1.3, NFR-05/06).
// 센서·옵션·로봇팔·초기화는 여기로 알리기만 하고, 조명·문·로봇팔·화면은 구독만 한다.
// 모듈끼리 서로를 직접 참조하지 않는다.
public static class HomeEvents
{
    // ── 클래스 다이어그램(System 페이지)의 사건 ──
    public static event Action<Zone> OnZoneEntered;
    public static event Action<Zone> OnZoneExited;
    public static event Action<OptionPart> OnPartClicked;
    public static event Action<RobotArm> OnArmClicked;
    public static event Action<OptionItem> OnOptionChanged;
    public static event Action<int> OnHourChanged;
    public static event Action<RobotArm, ArmState> OnArmStateChanged;
    public static event Action OnScenarioReset;

    // ── 이벤트 기록(FR-25) 통로 ──
    // 모든 기록은 Log()로 보낸다. EventLogger는 이 사건 하나만 구독하면 된다
    // (타입별 사건까지 구독하면 같은 줄이 두 번 남는다).
    // 인자: 발생 시각, 대상, 내용
    public static event Action<DateTime, string, string> OnLog;

    // EventLogger가 아직 없을 때 Console에서 확인하려고 켜 둔다. 기록 패널이 붙으면 꺼도 된다.
    public static bool EchoToConsole = true;

    public static void RaiseZoneEntered(Zone zone) => OnZoneEntered?.Invoke(zone);
    public static void RaiseZoneExited(Zone zone) => OnZoneExited?.Invoke(zone);
    public static void RaisePartClicked(OptionPart part) => OnPartClicked?.Invoke(part);
    public static void RaiseArmClicked(RobotArm arm) => OnArmClicked?.Invoke(arm);
    public static void RaiseOptionChanged(OptionItem item) => OnOptionChanged?.Invoke(item);
    public static void RaiseHourChanged(int hour) => OnHourChanged?.Invoke(hour);
    public static void RaiseArmStateChanged(RobotArm arm, ArmState state) => OnArmStateChanged?.Invoke(arm, state);
    public static void RaiseScenarioReset() => OnScenarioReset?.Invoke();

    // 예: HomeEvents.Log("거실", "조명 켜기 시작") → "10:32:05.120 거실 조명 켜기 시작"
    public static void Log(string target, string message)
    {
        DateTime now = DateTime.Now;
        if (EchoToConsole)
            Debug.Log($"[기록] {now:HH:mm:ss.fff} {target} {message}");
        OnLog?.Invoke(now, target, message);
    }

    // Play 모드에 들어갈 때 이전 플레이의 구독자를 지운다
    // (Enter Play Mode Options에서 도메인 리로드를 꺼도 안전하게).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearSubscribers()
    {
        OnZoneEntered = null;
        OnZoneExited = null;
        OnPartClicked = null;
        OnArmClicked = null;
        OnOptionChanged = null;
        OnHourChanged = null;
        OnArmStateChanged = null;
        OnScenarioReset = null;
        OnLog = null;
    }
}
