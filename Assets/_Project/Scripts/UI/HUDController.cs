using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌측 상단 메뉴 (FR-11, FR-31 / WBS 3.11, 4.07, 4.18)
/// - [옵션] [기록] [초기화] 세로 버튼 + 그 아래 추가 비용 합계
/// - 패널은 한 번에 하나만. 같은 버튼을 다시 누르면 닫힘.
///
/// 10/12 변경
/// - 옵션 부분 클릭: OptionPart.Clicked → HomeEvents.OnPartClicked 구독
/// - 비용 합계: HomeEvents.OnOptionChanged를 받으면 ConfigurationState.GetTotalCost()로 갱신
/// - 초기화: ScenarioManager.ResetScenario() 호출 (없으면 같은 일을 직접),
///           HomeEvents.OnScenarioReset을 받으면 패널을 모두 닫음 (F5로 초기화해도 닫힘, FR-26 1번)
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("버튼")]
    [SerializeField] private Button optionButton;
    [SerializeField] private Button logButton;
    [SerializeField] private Button resetButton;

    [Header("패널")]
    [SerializeField] private PanelUI optionPanel;
    [SerializeField] private PanelUI logPanel;

    [Header("추가 비용 합계")]
    [SerializeField] private Text costText;   // 한글 표시가 쉬워서 기본 Text 사용
    [SerializeField] private ConfigurationState config;

    [Header("초기화")]
    [SerializeField] private ScenarioManager scenario;

    private PanelUI openPanel;

    private void Awake()
    {
        if (config == null) config = FindAnyObjectByType<ConfigurationState>();
        if (scenario == null) scenario = FindAnyObjectByType<ScenarioManager>();

        optionButton.onClick.AddListener(() => TogglePanel(optionPanel));
        logButton.onClick.AddListener(() => TogglePanel(logPanel));
        resetButton.onClick.AddListener(OnResetClicked);
    }

    private void OnEnable()
    {
        HomeEvents.OnPartClicked += HandlePartClicked;
        HomeEvents.OnOptionChanged += HandleOptionChanged;
        HomeEvents.OnScenarioReset += CloseAllPanels;
    }

    private void OnDisable()
    {
        HomeEvents.OnPartClicked -= HandlePartClicked;
        HomeEvents.OnOptionChanged -= HandleOptionChanged;
        HomeEvents.OnScenarioReset -= CloseAllPanels;
    }

    private void Start()
    {
        // 시작할 때 패널은 모두 닫힌 상태
        CloseAllPanels();
        UpdateCost();
    }

    // FR-03: 옵션 부분을 클릭하면 옵션 패널을 열고 그 파트 탭으로
    private void HandlePartClicked(OptionPart part)
    {
        OpenOptionPanel();
        if (optionPanel is OptionPanelUI op) op.ShowPart(part.part);
    }

    private void HandleOptionChanged(OptionItem item) => UpdateCost();

    /// <summary>같은 패널이면 닫고, 다른 패널이면 기존 것 닫고 새로 열기</summary>
    public void TogglePanel(PanelUI panel)
    {
        if (panel == null) return;

        // 패널 안의 [닫기]로 닫혔을 수도 있으니 실제 상태로 확인
        if (openPanel != null && !openPanel.IsOpen()) openPanel = null;

        if (openPanel == panel)
        {
            panel.Close();
            openPanel = null;
            return;
        }

        if (openPanel != null) openPanel.Close();
        panel.Open();
        openPanel = panel;
    }

    /// <summary>옵션 부분 클릭(FR-03) 때 옵션 패널을 열기 위함. 이미 열려 있으면 그대로.</summary>
    public void OpenOptionPanel()
    {
        if (openPanel != null && !openPanel.IsOpen()) openPanel = null;
        if (openPanel == optionPanel) return;
        TogglePanel(optionPanel);
    }

    public void CloseAllPanels()
    {
        if (optionPanel != null) optionPanel.Close();
        if (logPanel != null) logPanel.Close();
        openPanel = null;
    }

    private void UpdateCost() => SetCost(config != null ? config.GetTotalCost() : 0);

    /// <summary>FR-31 "추가 비용 합계 (기본 에셋 대비, 예시 금액)"</summary>
    public void SetCost(int total)
    {
        if (costText == null) return;
        costText.text = $"추가 비용 합계\n<b>+{total:N0}원</b>\n<size=14>(기본 에셋 대비, 예시 금액)</size>";
    }

    private void OnResetClicked()
    {
        // FR-26 1번: 초기화 사건만 보낸다. 패널 닫기는 OnScenarioReset 구독(CloseAllPanels)에서.
        if (scenario != null)
        {
            scenario.ResetScenario();
        }
        else
        {
            HomeEvents.Log("시스템", "시나리오 초기화");
            HomeEvents.RaiseScenarioReset();
        }
    }
}
