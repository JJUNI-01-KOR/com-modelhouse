using System;
using System.Collections.Generic;

// 이벤트 기록 저장소 (FR-25, NFR-02 / WBS 4.12)
// - HomeEvents.Log() → HomeEvents.OnLog 하나만 구독해 기록을 모은다 (타입별 사건까지 구독하면 같은 줄이 두 번 남음)
// - 최대 100건, 넘으면 가장 오래된 것부터 지운다 (FIFO)
// - 화면과 상관없이 Play 동안 계속 기록한다. 화면(LogPanelUI)과 증거 저장(EvidenceCapture)은 여기서 읽기만 한다.
public static class EventLogger
{
    public struct Entry
    {
        public DateTime time;
        public string target;
        public string message;
    }

    public const int MaxEntries = 100;

    static readonly List<Entry> entries = new List<Entry>();   // 오래된 것 → 최신

    // 오래된 것 → 최신 순서
    public static IReadOnlyList<Entry> Entries => entries;
    // 기록이 바뀔 때마다 +1 (화면이 다시 그릴지 판단할 때 사용)
    public static int Version { get; private set; }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartCapture()
    {
        // HomeEvents는 SubsystemRegistration에서 구독자를 지우므로, 그 뒤인 여기서 구독
        Clear();
        HomeEvents.OnLog -= Add;
        HomeEvents.OnLog += Add;
    }

    static void Add(DateTime time, string target, string message)
    {
        entries.Add(new Entry { time = time, target = target, message = message });
        if (entries.Count > MaxEntries) entries.RemoveAt(0);
        Version++;
    }

    public static void Clear()
    {
        entries.Clear();
        Version++;
    }
}
