using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 옵션 패널 (FR-03, FR-29, FR-31 / WBS 4.07 간단 버전)
/// - 위쪽 탭: 파트 (벽지, 바닥재 …)
/// - 아래 목록: 옵션 이름 + "기본" 또는 "+400,000원", 고른 것은 파란색
/// - 옵션을 누르면 바로 공간에 적용하고 좌측 상단 추가 비용 합계 갱신
/// - 벽·바닥을 클릭하면 HUDController가 ShowPart로 그 파트 탭을 띄움
///
/// 지금은 이 패널이 "파트별 선택 상태"도 같이 들고 있음.
/// 김찬중 ConfigurationState(3.09)가 합류하면 Select → config.Select로 바꾸면 됨.
/// </summary>
public class OptionPanelUI : PanelUI
{
    [Serializable]
    public class PartEntry
    {
        public string label;               // 탭 이름 "벽지"
        public PartType part;
        public OptionPart target;          // 적용할 Option_Wall 등
        public List<OptionItem> options = new List<OptionItem>();
    }

    [SerializeField] private List<PartEntry> entries = new List<PartEntry>();
    [SerializeField] private RectTransform tabBar;
    [SerializeField] private RectTransform listRoot;
    [SerializeField] private HUDController hud;

    private readonly Dictionary<PartType, OptionItem> selected = new Dictionary<PartType, OptionItem>();
    private readonly List<Button> tabButtons = new List<Button>();
    private int currentTab;
    private bool built;

    public override void Open()
    {
        EnsureBuilt();
        base.Open();
        ShowTab(currentTab);
    }

    /// <summary>벽·바닥 클릭 때 그 파트 탭으로 이동</summary>
    public void ShowPart(PartType part)
    {
        EnsureBuilt();
        int i = entries.FindIndex(e => e.part == part);
        if (i >= 0) ShowTab(i);
    }

    // ------------------------------------------------------------
    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        // 처음엔 파트마다 기본 옵션이 선택된 상태 (FR-31: 전부 기본이면 0원)
        foreach (PartEntry e in entries)
        {
            OptionItem def = e.options.Find(o => o.isDefault);
            if (def != null) selected[e.part] = def;
        }

        // 탭 버튼
        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            Button b = UIFactory.CreateButton(tabBar, $"Tab_{entries[i].part}", entries[i].label, 20);
            b.onClick.AddListener(() => ShowTab(index));
            tabButtons.Add(b);
        }
    }

    private void ShowTab(int index)
    {
        if (entries.Count == 0) return;
        currentTab = Mathf.Clamp(index, 0, entries.Count - 1);

        for (int i = 0; i < tabButtons.Count; i++)
            ((Image)tabButtons[i].targetGraphic).color = i == currentTab ? UIFactory.SelectedColor : UIFactory.ButtonColor;

        RebuildList();
    }

    private void RebuildList()
    {
        for (int i = listRoot.childCount - 1; i >= 0; i--)
            Destroy(listRoot.GetChild(i).gameObject);

        PartEntry entry = entries[currentTab];
        selected.TryGetValue(entry.part, out OptionItem current);

        foreach (OptionItem item in entry.options)
        {
            OptionItem captured = item;
            string price = item.isDefault ? "기본" : $"+{item.extraCost:N0}원";
            Button row = UIFactory.CreateButton(listRoot, $"Row_{item.optionId}",
                $"{item.displayName}", 20, TextAnchor.MiddleLeft);
            UIFactory.SetLayoutSize(row, height: 52);
            ((Image)row.targetGraphic).color = item == current ? UIFactory.SelectedColor : UIFactory.ButtonColor;

            // 오른쪽 끝: 가격
            Text priceText = UIFactory.CreateText(row.transform, "Price", price, 18, TextAnchor.MiddleRight);
            UIFactory.Stretch(priceText.rectTransform, 10f);

            // 왼쪽 색 견본 (색상형 옵션만)
            if (item.useColor)
            {
                Image swatch = UIFactory.CreatePanel(row.transform, "Swatch", item.color);
                swatch.raycastTarget = false;
                RectTransform rt = swatch.rectTransform;
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(10f, 0f);
                rt.sizeDelta = new Vector2(28f, 28f);
                // 이름 글자를 견본 오른쪽으로 밀기
                Text label = row.transform.Find("Label").GetComponent<Text>();
                label.rectTransform.offsetMin = new Vector2(48f, label.rectTransform.offsetMin.y);
            }

            row.onClick.AddListener(() => Select(entry, captured));
        }
    }

    private void Select(PartEntry entry, OptionItem item)
    {
        if (entry.target != null) entry.target.Apply(item);   // FR-29: 1초 이내 반영
        selected[entry.part] = item;                          // 파트마다 하나만

        int total = 0;
        foreach (OptionItem o in selected.Values) total += o.extraCost;
        if (hud != null) hud.SetCost(total);                  // FR-31

        Debug.Log($"[옵션] {entry.label} → {item.displayName} (합계 +{total:N0}원)");
        RebuildList();
    }
}
