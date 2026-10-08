using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// 옵션 파트 (FR-29-01 ~ FR-29-07). 앞의 5개는 재질형, 뒤의 2개는 모델형.
public enum PartType { Wall, Floor, Countertop, SofaFabric, Rug, Painting, Plant }

public static class PartTypeExtensions
{
    public static string ToKorean(this PartType part)
    {
        switch (part)
        {
            case PartType.Wall: return "벽지";
            case PartType.Floor: return "바닥재";
            case PartType.Countertop: return "주방 상판";
            case PartType.SofaFabric: return "소파 패브릭";
            case PartType.Rug: return "러그";
            case PartType.Painting: return "그림 액자";
            case PartType.Plant: return "화분";
            default: return part.ToString();
        }
    }

    public static bool IsMaterialPart(this PartType part) => part != PartType.Painting && part != PartType.Plant;
}

// 옵션 하나 (부록 A-7의 한 줄)
[Serializable]
public class OptionItem
{
    public string optionId;
    public PartType part;
    public string displayName;
    [Tooltip("재질형: 바꿀 재질. 기본 옵션은 비워 두면 에셋 원본 재질로 되돌린다")]
    public Material material;
    [Tooltip("모델형: 바꿀 프리팹. 기본 옵션은 비워 두면 원래 놓인 모델로 되돌린다")]
    public GameObject prefab;
    public Sprite preview;
    [Tooltip("기본 에셋 대비 추가 비용 (원)")]
    public int extraCost;
    public bool isDefault;

    // 옵션 목록 표시용: "기본" 또는 "+400,000원" (FR-31 1번)
    public string CostLabel => isDefault ? "기본" : "+" + extraCost.ToString("N0", CultureInfo.InvariantCulture) + "원";
}

// 옵션 목록과 가격 (부록 A-7). 메뉴 Tools → COM → 옵션 카탈로그 만들기 로 채운다.
[CreateAssetMenu(menuName = "COM/Option Catalog", fileName = "OptionCatalog")]
public class OptionCatalog : ScriptableObject
{
    [SerializeField] List<OptionItem> items = new List<OptionItem>();

    public List<OptionItem> Items => items;

    public List<OptionItem> GetOptions(PartType part)
    {
        var result = new List<OptionItem>();
        foreach (OptionItem item in items)
            if (item.part == part) result.Add(item);
        return result;
    }

    public OptionItem GetDefault(PartType part)
    {
        OptionItem first = null;
        foreach (OptionItem item in items)
        {
            if (item.part != part) continue;
            if (item.isDefault) return item;
            if (first == null) first = item;
        }
        return first;
    }

    public OptionItem Find(string optionId)
    {
        foreach (OptionItem item in items)
            if (item.optionId == optionId) return item;
        return null;
    }
}
