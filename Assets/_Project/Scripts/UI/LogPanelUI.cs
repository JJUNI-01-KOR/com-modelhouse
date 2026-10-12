using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 이벤트 기록 패널 (FR-25, UI-07 / WBS 4.13)
/// - "10:32:05.120  주방  커피 완료" 형식, 최신 기록이 맨 위
/// - 최대 100건, 넘으면 가장 오래된 것부터 지움
/// - 패널이 열려 있는 동안 0.5초마다(PanelUI) 새 기록이 있으면 다시 그림
/// - 패널이 열려 있어도 이동·타이머·로봇팔은 그대로 진행 (Time.timeScale 안 건드림)
///
/// 기록은 HomeEvents.Log() → HomeEvents.OnLog 하나로만 들어온다.
/// 패널이 꺼져 있을 때 생긴 기록도 놓치지 않도록, 씬이 열리기 전에 정적 버퍼가 먼저 구독한다.
/// (김찬중 EventLogger(4.12)가 생기면 Entries만 그쪽 GetEntries()로 바꾸면 됨)
/// </summary>
public class LogPanelUI : PanelUI
{
    public struct Entry
    {
        public DateTime time;
        public string target;
        public string message;
    }

    public const int MaxEntries = 100;

    [SerializeField] private Text body;            // ScrollRect Content 안의 Text
    [SerializeField] private ScrollRect scroll;    // 새 기록이 오면 맨 위로

    // ── 정적 버퍼 (씬과 상관없이 Play 동안 계속 기록) ──
    private static readonly List<Entry> entries = new List<Entry>();   // 오래된 것 → 최신
    private static int version;                                        // 기록이 바뀔 때마다 +1

    public static IReadOnlyList<Entry> Entries => entries;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void StartCapture()
    {
        // HomeEvents는 SubsystemRegistration에서 구독자를 지우므로, 그 뒤인 여기서 구독
        entries.Clear();
        version = 0;
        HomeEvents.OnLog += Capture;
    }

    private static void Capture(DateTime time, string target, string message)
    {
        entries.Add(new Entry { time = time, target = target, message = message });
        if (entries.Count > MaxEntries) entries.RemoveAt(0);   // FIFO
        version++;
    }

    // ── 화면 ──────────────────────────────────────────
    private int drawnVersion = -1;
    private readonly StringBuilder sb = new StringBuilder(8 * 1024);

    public override void Open()
    {
        drawnVersion = -1;   // 열 때는 무조건 다시 그림
        base.Open();
    }

    protected override void Refresh()
    {
        if (body == null || drawnVersion == version) return;
        drawnVersion = version;

        sb.Clear();
        if (entries.Count == 0) sb.Append("<color=#999999>(아직 기록 없음)</color>");
        for (int i = entries.Count - 1; i >= 0; i--)   // 최신순
        {
            Entry e = entries[i];
            sb.Append("<color=#9FB4D8>").Append(e.time.ToString("HH:mm:ss.fff")).Append("</color>  ")
              .Append("<b>").Append(e.target).Append("</b>  ")
              .Append(e.message);
            if (i > 0) sb.Append('\n');
        }
        body.text = sb.ToString();

        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }
}
