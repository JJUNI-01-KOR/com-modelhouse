using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 우측 상단 시각 버튼 (FR-30, UI-09 / WBS 4.08, 4.11, SRS v2.4)
/// - 버튼 5개: 06:00 해 뜸, 09:00 아침, 12:00 낮, 18:00 해 질 녘, 22:00 밤 (부록 A-8 기준 시각)
/// - 누르면: ITimeOfDay.SetHour → 햇빛·하늘이 1초 이내에 바뀜, 이벤트 기록 "시각 변경 18:00"
/// - 지금 시각 버튼은 파란색, 버튼 아래에 현재 구역 조도(lx)
/// - 초기화(FR-26 8번): 12:00
///
/// v2.3까지는 시침을 끄는 아날로그 시계였음 → v2.4에서 버튼으로 변경
/// (클래스 이름은 씬 참조가 끊기지 않게 ClockUI 그대로 둠)
///
/// ITimeOfDay(김찬중 TimeOfDayController, 4.09·4.10)가 아직 없으면
/// 버튼 표시와 기록, HomeEvents.OnHourChanged만 동작하고 조도는 '-'로 표시
/// </summary>
public class ClockUI : MonoBehaviour
{
    [Serializable]
    public struct Preset
    {
        public int hour;
        public string label;
        public Preset(int hour, string label) { this.hour = hour; this.label = label; }
    }

    /// <summary>SRS v2.4 FR-30 시각 버튼 (부록 A-8)</summary>
    public static readonly Preset[] DefaultPresets =
    {
        new Preset(6,  "해 뜸"),
        new Preset(9,  "아침"),
        new Preset(12, "낮"),
        new Preset(18, "해 질 녘"),
        new Preset(22, "밤"),
    };

    [SerializeField] private RectTransform buttonRow;     // 버튼이 들어갈 곳 (HorizontalLayoutGroup)
    [SerializeField] private Text luxText;                // "거실 조도 820 lx"
    [Tooltip("비우면 씬에서 ITimeOfDay를 구현한 컴포넌트를 찾는다")]
    [SerializeField] private MonoBehaviour timeSource;
    [SerializeField] private float luxRefreshInterval = 0.5f;

    private const int StartHour = 12;                     // 체험 시작·초기화 때 12:00 (SRS 6장)

    private ITimeOfDay time;
    private int hour = StartHour;
    private ZoneVolume currentZone;
    private float luxTimer;
    private readonly List<(int hour, Image bg)> buttons = new List<(int, Image)>();

    private void Awake()
    {
        time = timeSource as ITimeOfDay;
        if (time == null)
        {
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>())
                if (mb is ITimeOfDay t) { time = t; timeSource = mb; break; }
        }
        if (time == null)
            Debug.Log("[ClockUI] ITimeOfDay(TimeOfDayController)가 아직 없음 → 버튼만 동작, 조도는 '-'로 표시");

        BuildButtons();
    }

    private void OnEnable()
    {
        HomeEvents.OnHourChanged += HandleHourChanged;
        HomeEvents.OnZoneEntered += HandleZoneEntered;
        HomeEvents.OnScenarioReset += HandleReset;
    }

    private void OnDisable()
    {
        HomeEvents.OnHourChanged -= HandleHourChanged;
        HomeEvents.OnZoneEntered -= HandleZoneEntered;
        HomeEvents.OnScenarioReset -= HandleReset;
    }

    private void Start()
    {
        // Awake 뒤에 자동으로 생긴 TimeOfDayController(김찬중 4.09)도 잡도록 한 번 더 찾는다
        if (time == null)
        {
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>())
                if (mb is ITimeOfDay t) { time = t; timeSource = mb; break; }
            if (time != null) Debug.Log("[ClockUI] TimeOfDayController 연결됨");
        }
        hour = time != null ? time.CurrentHour : StartHour;
        PaintButtons();
        UpdateLux();
    }

    private void Update()
    {
        // 조명이 켜지고 꺼지는 중에도 조도가 바뀌므로 0.5초마다 다시 읽음
        luxTimer += Time.unscaledDeltaTime;
        if (luxTimer >= luxRefreshInterval)
        {
            luxTimer = 0f;
            UpdateLux();
        }
    }

    // ── 버튼 ─────────────────────────────────────────
    // 9번 메뉴가 미리 만든 버튼(Btn_06 …)이 있으면 그대로 쓰고, 없으면 여기서 만든다
    private void BuildButtons()
    {
        if (buttonRow == null) { Debug.LogWarning("[ClockUI] buttonRow가 비어 있음 (COM → 9번 메뉴 실행)", this); return; }
        buttons.Clear();

        foreach (Preset p in DefaultPresets)
        {
            int h = p.hour;
            Transform found = buttonRow.Find(ButtonName(h));
            Button b = found != null ? found.GetComponent<Button>() : null;
            if (b == null) b = CreateButton(buttonRow, p);
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => Select(h));
            buttons.Add((h, (Image)b.targetGraphic));
        }
    }

    public static string ButtonName(int hour) => $"Btn_{hour:00}";

    /// <summary>시각 버튼 하나 만들기 (에디터 9번 메뉴와 실행 중 둘 다 사용)</summary>
    public static Button CreateButton(Transform row, Preset p)
        => UIFactory.CreateButton(row, ButtonName(p.hour), $"<b>{p.hour:00}:00</b>\n<size=14>{p.label}</size>", 18);

    /// <summary>시각 버튼을 눌렀을 때 (FR-30 처리 1~4)</summary>
    public void Select(int h)
    {
        if (h == hour) return;
        hour = h;
        PaintButtons();

        if (time != null) time.SetHour(h);            // TimeOfDayController가 햇빛 변경 + RaiseHourChanged
        else HomeEvents.RaiseHourChanged(h);          // 아직 없으면 사건만
        HomeEvents.Log("시각", $"시각 변경 {h:00}:00");
        UpdateLux();
    }

    // ── 받는 사건 ─────────────────────────────────────
    private void HandleHourChanged(int h)
    {
        if (h == hour) return;
        hour = h;
        PaintButtons();
        UpdateLux();
    }

    private void HandleZoneEntered(ZoneVolume zone)
    {
        if (zone == null || zone.IsSensorOnly) return;
        currentZone = zone;
        UpdateLux();
    }

    private void HandleReset()
    {
        // TimeOfDayController가 있으면 그쪽이 12시로 되돌리고 OnHourChanged(12)를 보낸다
        if (time == null)
        {
            hour = StartHour;
            HomeEvents.RaiseHourChanged(StartHour);
        }
        else
        {
            hour = time.CurrentHour;
        }
        PaintButtons();
    }

    // ── 표시 ─────────────────────────────────────────
    private void PaintButtons()
    {
        foreach (var (h, bg) in buttons)
            if (bg != null) bg.color = h == hour ? UIFactory.SelectedColor : UIFactory.ButtonColor;
    }

    private void UpdateLux()
    {
        if (luxText == null) return;
        string zoneName = currentZone != null ? currentZone.DisplayName : "현재 구역";
        if (time == null || currentZone == null)
            luxText.text = $"{zoneName} 조도 - lx";
        else
            luxText.text = $"{zoneName} 조도 {time.GetZoneLux(currentZone):N0} lx";
    }
}
