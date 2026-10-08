using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 메뉴 Tools → COM → 옵션 카탈로그 만들기 (부록 A-7, SRS v2.1)
// - Assets/_Project/Data/OptionCatalog.asset 을 아래 표 순서·내용으로 다시 만든다.
// - SRS v2.1: 모든 옵션은 색상형 (useColor + color). 모델·재질 교체 없음.
//   기본 옵션은 useColor만 켜 두면 OptionPart가 에셋 원본 색으로 되돌린다.
// - 표에 없는 옵션 ID는 지운다. 이미 넣어 둔 미리보기(preview)는 같은 ID면 그대로 둔다.
// - 그림 액자·화분 색 2종은 A-7에 "구현 시 확정"이라 제안 값이다. 확정되면 여기와 SRS A-7을 같이 고친다.
public static class OptionCatalogBuilder
{
    const string DataFolder = "Assets/_Project/Data";
    const string CatalogPath = DataFolder + "/OptionCatalog.asset";
    const string OldMatFolder = "Assets/_Project/Materials/Options";   // 예전 방식(재질 복제)에서 만든 폴더

    struct Spec
    {
        public string id, name, hex;
        public PartType part;
        public int cost;
        public bool isDefault;
    }

    // 기본 (에셋 원본 색으로 되돌림)
    static Spec D(string id, PartType part, string name) => new Spec { id = id, part = part, name = name, isDefault = true };
    // 색상 옵션
    static Spec C(string id, PartType part, string name, int cost, string hex) => new Spec { id = id, part = part, name = name, cost = cost, hex = hex };

    // 옵션 ID는 권오민 COMSetupTools와 같게 맞춤 (wall_*, floor_wood/kang/navy/red)
    static readonly Spec[] Specs =
    {
        // 바닥재 (m_floor, 대상 sm_floor)
        D("floor_wood", PartType.Floor, "원목 마루"),
        C("floor_kang", PartType.Floor, "강마루", 1500000, "#E48120"),
        C("floor_navy", PartType.Floor, "네이비 마루", 3200000, "#1747BE"),
        C("floor_red", PartType.Floor, "레드 포인트", 1000000, "#EC0000"),
        // 벽지 (m_walls, 대상 sm_walls)
        D("wall_white", PartType.Wall, "화이트 페인트"),
        C("wall_greige", PartType.Wall, "그레이지", 400000, "#BDB5A6"),
        C("wall_sage", PartType.Wall, "세이지 그린", 400000, "#A3B18A"),
        C("wall_fabric", PartType.Wall, "패브릭 베이지", 700000, "#D9CFC1"),
        // 주방 상판 (m_countertop)
        D("counter_white", PartType.Countertop, "화이트 상판"),
        C("counter_darkstone", PartType.Countertop, "다크 스톤", 1800000, "#2E2E33"),
        // 소파 패브릭 (m_white_fabric)
        D("sofa_white", PartType.SofaFabric, "화이트"),
        C("sofa_warmgray", PartType.SofaFabric, "웜 그레이", 300000, "#9E968F"),
        C("sofa_navy", PartType.SofaFabric, "네이비", 300000, "#29334F"),
        // 러그 (m_carpet)
        D("rug_default", PartType.Rug, "데모 씬 러그"),
        C("rug_beige", PartType.Rug, "베이지", 250000, "#D9C7A6"),
        C("rug_charcoal", PartType.Rug, "차콜", 250000, "#383A3D"),
        // 그림 액자 (sm_painting) — 색 2종은 제안 값
        D("painting_default", PartType.Painting, "데모 씬 그림"),
        C("painting_warm", PartType.Painting, "웜 톤", 150000, "#E3C7A1"),
        C("painting_cool", PartType.Painting, "쿨 톤", 150000, "#A7BED6"),
        // 화분 (sm_plant_pot) — 색 2종은 제안 값
        D("plant_default", PartType.Plant, "데모 씬 화분"),
        C("plant_terracotta", PartType.Plant, "테라코타", 80000, "#C2693E"),
        C("plant_charcoal", PartType.Plant, "차콜", 80000, "#3D3D3D"),
    };

    [MenuItem("Tools/COM/옵션 카탈로그 만들기 (부록 A-7)")]
    public static void Build()
    {
        EnsureFolder(DataFolder);

        OptionCatalog catalog = AssetDatabase.LoadAssetAtPath<OptionCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<OptionCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        // 같은 ID에 넣어 둔 미리보기는 살린다
        var previews = new Dictionary<string, Sprite>();
        foreach (OptionItem old in catalog.Items)
            if (old != null && !string.IsNullOrEmpty(old.optionId) && old.preview != null)
                previews[old.optionId] = old.preview;

        catalog.Items.Clear();
        foreach (Spec s in Specs)
        {
            Color c = Color.white;
            if (!string.IsNullOrEmpty(s.hex)) ColorUtility.TryParseHtmlString(s.hex, out c);
            previews.TryGetValue(s.id, out Sprite preview);

            catalog.Items.Add(new OptionItem
            {
                optionId = s.id,
                part = s.part,
                displayName = s.name,
                useColor = true,
                color = c,
                material = null,
                prefab = null,
                preview = preview,
                extraCost = s.cost,
                isDefault = s.isDefault,
            });
        }

        // 예전 방식으로 만든 복제 재질은 더 이상 쓰지 않으므로 정리
        if (AssetDatabase.IsValidFolder(OldMatFolder)) AssetDatabase.DeleteAsset(OldMatFolder);

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);

        Debug.Log($"[OptionCatalog] {catalog.Items.Count}개 옵션 준비 완료 ({CatalogPath}, SRS v2.1 색상형)");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
