using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 옵션 패널 (FR-03, FR-29, FR-31 / WBS 4.07)
/// - 위쪽 탭: 파트 (벽지, 바닥재, 주방 상판, 소파 패브릭, 러그, 그림 액자, 화분)
/// - 아래 목록: 색 견본 + 옵션 이름 + "기본" 또는 "+400,000원", 지금 고른 것은 파란색
/// - 옵션을 누르면 ConfigurationState.Select → 공간 반영·기록·합계 갱신은 거기서 한다
///
/// 10/12 변경: 옵션 목록과 선택 상태를 이 패널이 따로 들고 있지 않고
///   OptionCatalog(부록 A-7) + ConfigurationState(김찬중 3.09) 하나만 보고 그린다.
///   → 패널, 합계, 이벤트 기록이 항상 같은 값을 본다 (TC-49).
/// </summary>
public class OptionPanelUI : PanelUI
{
    [SerializeField] private ConfigurationState config;
    [SerializeField] private RectTransform tabBar;
    [SerializeField] private RectTransform listRoot;

    private readonly List<PartType> tabs = new List<PartType>();
    private readonly List<Button> tabButtons = new List<Button>();
    private readonly List<(OptionItem item, Image bg)> rows = new List<(OptionItem, Image)>();
    private int currentTab;
    private bool built;

    protected override void Awake()
    {
        base.Awake();
        if (config == null) config = FindAnyObjectByType<ConfigurationState>();
    }

    private void OnEnable() => HomeEvents.OnOptionChanged += HandleOptionChanged;
    private void OnDisable() => HomeEvents.OnOptionChanged -= HandleOptionChanged;

    public override void Open()
    {
        EnsureBuilt();
        base.Open();
        ShowTab(currentTab);
    }

    /// <summary>벽·바닥 등을 클릭했을 때 그 파트 탭으로 이동 (FR-03)</summary>
    public void ShowPart(PartType part)
    {
        EnsureBuilt();
        int i = tabs.IndexOf(part);
        if (i >= 0) ShowTab(i);
    }

    // 0.5초마다(PanelUI) 선택 표시만 다시 칠한다. 버튼을 새로 만들지 않아서 클릭이 끊기지 않음.
    protected override void Refresh() => PaintRows();

    // ------------------------------------------------------------
    private void EnsureBuilt()
    {
        if (built) return;
        if (config == null) config = FindAnyObjectByType<ConfigurationState>();
        if (config == null || config.Catalog == null)
        {
            Debug.LogWarning("[OptionPanelUI] ConfigurationState 또는 OptionCatalog가 없음 (COM → 9번 메뉴 실행)", this);
            return;   // built를 켜지 않음 → 다음에 열 때 다시 시도
        }
        built = true;

        // 씬에 OptionPart가 있고, 카탈로그에 옵션이 있는 파트만 탭으로 (enum 순서 = SRS A-7 순서)
        var partsInScene = new HashSet<PartType>();
        foreach (OptionPart p in FindObjectsByType<OptionPart>()) partsInScene.Add(p.part);

        foreach (PartType part in System.Enum.GetValues(typeof(PartType)))
        {
            if (!partsInScene.Contains(part)) continue;
            if (config.Catalog.GetOptions(part).Count == 0) continue;
            tabs.Add(part);
        }

        // 탭 버튼 (7개면 한 줄이 좁아서 글자를 조금 줄임)
        int fontSize = tabs.Count > 5 ? 15 : 20;
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            Button b = UIFactory.CreateButton(tabBar, $"Tab_{tabs[i]}", tabs[i].ToKorean(), fontSize);
            b.onClick.AddListener(() => ShowTab(index));
            tabButtons.Add(b);
        }
    }

    private void ShowTab(int index)
    {
        if (tabs.Count == 0) return;
        currentTab = Mathf.Clamp(index, 0, tabs.Count - 1);

        for (int i = 0; i < tabButtons.Count; i++)
            ((Image)tabButtons[i].targetGraphic).color = i == currentTab ? UIFactory.SelectedColor : UIFactory.ButtonColor;

        RebuildList();
    }

    private void RebuildList()
    {
        for (int i = listRoot.childCount - 1; i >= 0; i--)
            Destroy(listRoot.GetChild(i).gameObject);
        rows.Clear();
        if (tabs.Count == 0) return;

        foreach (OptionItem item in config.Catalog.GetOptions(tabs[currentTab]))
        {
            OptionItem captured = item;
            Button row = UIFactory.CreateButton(listRoot, $"Row_{item.optionId}", item.displayName, 20, TextAnchor.MiddleLeft);
            UIFactory.SetLayoutSize(row, height: 52);

            // 오른쪽 끝: "기본" / "+400,000원" (FR-31 1번)
            Text priceText = UIFactory.CreateText(row.transform, "Price", item.CostLabel(), 18, TextAnchor.MiddleRight);
            UIFactory.Stretch(priceText.rectTransform, 10f);

            // 왼쪽 색 견본. 기본 옵션은 에셋 원본 색이라 견본 대신 테두리만
            Image swatch = UIFactory.CreatePanel(row.transform, "Swatch", item.isDefault ? new Color(1f, 1f, 1f, 0.15f) : item.color);
            swatch.raycastTarget = false;
            RectTransform rt = swatch.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(10f, 0f);
            rt.sizeDelta = new Vector2(28f, 28f);
            Text label = row.transform.Find("Label").GetComponent<Text>();
            label.rectTransform.offsetMin = new Vector2(48f, label.rectTransform.offsetMin.y);

            row.onClick.AddListener(() => config.Select(captured));
            rows.Add((item, (Image)row.targetGraphic));
        }
        PaintRows();
    }

    private void PaintRows()
    {
        if (config == null) return;
        foreach (var (item, bg) in rows)
            if (bg != null) bg.color = config.IsSelected(item) ? UIFactory.SelectedColor : UIFactory.ButtonColor;
    }

    private void HandleOptionChanged(OptionItem item)
    {
        if (IsOpen()) PaintRows();
    }
}
