using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// 고른 옵션과 추가 비용 합계 (FR-29, FR-31, TC-47, TC-49)
// - 파트마다 하나만 선택된다. 같은 파트의 이전 선택은 자동으로 해제된다.
// - 시나리오 초기화(FR-26)로는 바뀌지 않는다 (OnScenarioReset을 구독하지 않음).
// UI(권오민)는 Select()를 부르고, 합계는 GetTotalCost() / TotalCostText 로 읽는다.
public class ConfigurationState : MonoBehaviour
{
    [SerializeField] OptionCatalog catalog;
    [Tooltip("비우면 씬의 OptionPart를 모두 찾아 쓴다")]
    [SerializeField] List<OptionPart> parts = new List<OptionPart>();

    readonly Dictionary<PartType, OptionItem> selected = new Dictionary<PartType, OptionItem>();

    public OptionCatalog Catalog => catalog;

    void Awake()
    {
        if (parts.Count == 0)
            parts.AddRange(FindObjectsByType<OptionPart>());

        if (catalog == null)
        {
            Debug.LogWarning("[ConfigurationState] OptionCatalog가 비어 있습니다.", this);
            return;
        }

        // 시작은 모든 파트가 기본 옵션 (합계 0원)
        foreach (PartType part in System.Enum.GetValues(typeof(PartType)))
        {
            OptionItem def = catalog.GetDefault(part);
            if (def != null) selected[part] = def;
        }
    }

    public void Select(OptionItem item)
    {
        if (item == null) return;
        if (selected.TryGetValue(item.part, out OptionItem current) && current == item) return;

        selected[item.part] = item;
        foreach (OptionPart part in parts)
            if (part != null && part.part == item.part) part.Apply(item);

        HomeEvents.Log(item.part.ToKorean(), $"옵션 변경 {item.displayName} ({item.CostLabel})");
        HomeEvents.RaiseOptionChanged(item);
    }

    public void Select(string optionId)
    {
        if (catalog != null) Select(catalog.Find(optionId));
    }

    public OptionItem GetSelected(PartType part)
    {
        selected.TryGetValue(part, out OptionItem item);
        return item;
    }

    public bool IsSelected(OptionItem item) => item != null && GetSelected(item.part) == item;

    public int GetTotalCost()
    {
        int sum = 0;
        foreach (OptionItem item in selected.Values) sum += item.extraCost;
        return sum;
    }

    // 좌측 상단 메뉴 아래 표시용 (FR-31 3번): "1,900,000원"
    public string TotalCostText => GetTotalCost().ToString("N0", CultureInfo.InvariantCulture) + "원";

    // OptionPart를 실행 중에 추가로 등록할 때
    public void Register(OptionPart part)
    {
        if (part != null && !parts.Contains(part)) parts.Add(part);
    }

    // ── UI 없이 시험하기 (Play 중 컴포넌트 ⋮ 메뉴) ──
    [Header("테스트")]
    [Tooltip("예: wall_greige, floor_marble, sofa_navy")]
    [SerializeField] string testOptionId = "wall_greige";

    [ContextMenu("테스트: testOptionId 옵션 선택")]
    void TestSelect() => Select(testOptionId);

    [ContextMenu("테스트: 고른 옵션과 합계 출력")]
    void TestPrintTotal()
    {
        var sb = new System.Text.StringBuilder();
        foreach (PartType part in System.Enum.GetValues(typeof(PartType)))
        {
            OptionItem item = GetSelected(part);
            sb.AppendLine($"{part.ToKorean()}: {(item != null ? item.displayName + " " + item.CostLabel : "없음")}");
        }
        sb.Append("추가 비용 합계: ").Append(TotalCostText);
        Debug.Log(sb.ToString());
    }
}
