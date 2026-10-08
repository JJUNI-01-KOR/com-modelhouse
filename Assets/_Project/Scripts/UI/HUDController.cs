using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 좌측 상단 메뉴 (FR-11, FR-31 / WBS 3.11 골격)
/// - [옵션] [기록] [초기화] 세로 버튼 + 그 아래 추가 비용 합계
/// - 패널은 한 번에 하나만. 같은 버튼을 다시 누르면 닫힘.
///
/// 아직 안 붙은 것 (합류 후 연결):
///   - 비용: 지금은 OptionPanelUI가 SetCost 호출 → ConfigurationState(김찬중 3.09) 합류 후 교체
///   - 초기화: onResetClicked에 ScenarioManager.ResetScenario를 Inspector로 연결
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

    [Header("초기화 (ScenarioManager 연결)")]
    public UnityEvent onResetClicked;

    private PanelUI openPanel;

    private void Awake()
    {
        optionButton.onClick.AddListener(() => TogglePanel(optionPanel));
        logButton.onClick.AddListener(() => TogglePanel(logPanel));
        resetButton.onClick.AddListener(OnResetClicked);
    }

    // FR-03: 옵션 부분을 클릭하면 옵션 패널 열기
    // (HomeEvents 합류 후엔 HomeEvents.OnPartClicked 구독으로 바꿀 자리)
    private void OnEnable() => OptionPart.Clicked += HandlePartClicked;
    private void OnDisable() => OptionPart.Clicked -= HandlePartClicked;

    private void HandlePartClicked(OptionPart part)
    {
        OpenOptionPanel();
        if (optionPanel is OptionPanelUI op) op.ShowPart(part.part);   // 클릭한 파트 탭으로
    }

    private void Start()
    {
        // 시작할 때 패널은 모두 닫힌 상태
        if (optionPanel != null) optionPanel.Close();
        if (logPanel != null) logPanel.Close();
        openPanel = null;
        SetCost(0);
    }

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

    /// <summary>FR-31 "추가 비용 합계 (기본 에셋 대비, 예시 금액)"</summary>
    public void SetCost(int total)
    {
        if (costText == null) return;
        costText.text = $"추가 비용 합계\n<b>+{total:N0}원</b>\n<size=14>(기본 에셋 대비, 예시 금액)</size>";
    }

    private void OnResetClicked()
    {
        CloseAllPanels();            // FR-26 1번: 열린 패널 닫기
        onResetClicked?.Invoke();
    }
}
