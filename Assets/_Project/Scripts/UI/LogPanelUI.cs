using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 이벤트 기록 패널 (FR-25, UI-07 / WBS 4.13)
/// - "10:32:05.120  주방  커피 완료" 형식, 최신 기록이 맨 위
/// - 패널이 열려 있는 동안 0.5초마다(PanelUI) 새 기록이 있으면 다시 그림
/// - 패널이 열려 있어도 이동·타이머·로봇팔은 그대로 진행 (Time.timeScale 안 건드림)
/// 기록 저장(최대 100건, FIFO)은 EventLogger가 하고, 이 패널은 EventLogger.Entries를 그리기만 한다.
/// </summary>
public class LogPanelUI : PanelUI
{
    [SerializeField] private Text body;            // ScrollRect Content 안의 Text
    [SerializeField] private ScrollRect scroll;    // 새 기록이 오면 맨 위로 (맨 위를 보고 있을 때만)

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
        if (body == null || drawnVersion == EventLogger.Version) return;
        // 처음 그릴 때나 맨 위를 보고 있을 때만 맨 위로 (아래 기록을 읽는 중이면 위치 유지)
        bool toTop = drawnVersion < 0 || scroll == null || scroll.verticalNormalizedPosition > 0.98f;
        drawnVersion = EventLogger.Version;
        var entries = EventLogger.Entries;

        sb.Clear();
        if (entries.Count == 0) sb.Append("<color=#999999>(아직 기록 없음)</color>");
        for (int i = entries.Count - 1; i >= 0; i--)   // 최신순
        {
            EventLogger.Entry e = entries[i];
            sb.Append("<color=#9FB4D8>").Append(e.time.ToString("HH:mm:ss.fff")).Append("</color>  ")
              .Append("<b>").Append(e.target).Append("</b>  ")
              .Append(e.message);
            if (i > 0) sb.Append('\n');
        }
        body.text = sb.ToString();

        if (scroll != null && toTop) scroll.verticalNormalizedPosition = 1f;
    }
}
