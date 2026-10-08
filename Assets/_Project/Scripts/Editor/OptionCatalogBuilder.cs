using System.Text;
using UnityEditor;
using UnityEngine;

// 메뉴 Tools → COM → 옵션 카탈로그 만들기 (부록 A-7)
// - Assets/_Project/Data/OptionCatalog.asset 에 A-7의 옵션 21개와 가격을 채운다.
// - 팀 결정(10/8): 마감재·패브릭은 "색상만" 바꾼다 → useColor + color (OptionPart.ApplyColor).
//   기본 옵션은 useColor만 켜 두면 OptionPart가 원래 색으로 되돌린다.
// - 그림 액자·화분은 모델 교체 → prefab. 기본 옵션도 원래 프리팹을 넣어야 되돌릴 수 있다.
// - 다시 실행해도 이름·가격·색은 갱신하고, 이미 넣은 프리팹·미리보기는 그대로 둔다.
public static class OptionCatalogBuilder
{
    const string DataFolder = "Assets/_Project/Data";
    const string CatalogPath = DataFolder + "/OptionCatalog.asset";
    const string OldMatFolder = "Assets/_Project/Materials/Options";   // 예전 방식(재질 복제)에서 만든 폴더

    struct Spec
    {
        public string id, name, hex, prefabName;
        public PartType part;
        public int cost;
        public bool isDefault;
    }

    // 색상형 기본 (원래 색)
    static Spec D(string id, PartType part, string name) => new Spec { id = id, part = part, name = name, isDefault = true };
    // 색상형 옵션
    static Spec C(string id, PartType part, string name, int cost, string hex) => new Spec { id = id, part = part, name = name, cost = cost, hex = hex };
    // 모델형 (기본이면 cost 0, isDefault)
    static Spec M(string id, PartType part, string name, int cost, string prefab, bool isDefault = false)
        => new Spec { id = id, part = part, name = name, cost = cost, prefabName = prefab, isDefault = isDefault };

    static readonly Spec[] Specs =
    {
        // 벽지 (m_walls) — 색은 권오민 3.10 작업내역과 같게
        D("wall_white", PartType.Wall, "화이트 페인트"),
        C("wall_greige", PartType.Wall, "그레이지 실크 벽지", 400000, "#BDB5A6"),
        C("wall_sage", PartType.Wall, "세이지 그린 실크 벽지", 400000, "#A3B18A"),
        C("wall_fabric", PartType.Wall, "패브릭 질감 벽지", 700000, "#D9CFC1"),
        // 바닥재 (m_floor)
        D("floor_oak", PartType.Floor, "원목 마루"),
        C("floor_light", PartType.Floor, "밝은 강마루", 1500000, "#E48120"),
        C("floor_marble", PartType.Floor, "대리석 타일", 3200000, "#E6E6E8"),
        // 주방 상판 (m_countertop)
        D("counter_white", PartType.Countertop, "화이트 상판"),
        C("counter_darkstone", PartType.Countertop, "다크 스톤", 1800000, "#2E2E33"),
        // 소파 패브릭 (m_white_fabric)
        D("sofa_white", PartType.SofaFabric, "화이트"),
        C("sofa_warmgray", PartType.SofaFabric, "웜 그레이", 300000, "#9E968F"),
        C("sofa_navy", PartType.SofaFabric, "네이비", 300000, "#29334F"),
        // 러그 (m_carpet)
        D("rug_default", PartType.Rug, "데모 씬 러그"),
        C("rug_beige", PartType.Rug, "베이지 울 러그", 250000, "#D9C7A6"),
        C("rug_charcoal", PartType.Rug, "차콜 러그", 250000, "#383A3D"),
        // 그림 액자 (모델형)
        M("painting_default", PartType.Painting, "데모 씬 그림", 0, "sm_painting", true),
        M("painting_2", PartType.Painting, "그림 액자 2", 150000, "sm_painting_2"),
        M("painting_3", PartType.Painting, "그림 액자 3", 150000, "sm_painting_3"),
        // 화분 (모델형)
        M("plant_default", PartType.Plant, "데모 씬 화분", 0, "sm_strelitzia", true),
        M("plant_fern", PartType.Plant, "고사리 화분", 80000, "sm_plant_fern"),
        M("plant_broadleaf", PartType.Plant, "넓은잎 화분", 80000, "sm_broadleaf"),
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

        var missing = new StringBuilder();
        foreach (Spec s in Specs)
        {
            OptionItem item = catalog.Find(s.id);
            if (item == null)
            {
                item = new OptionItem { optionId = s.id };
                catalog.Items.Add(item);
            }
            item.part = s.part;
            item.displayName = s.name;
            item.extraCost = s.cost;
            item.isDefault = s.isDefault;

            if (s.part.IsMaterialPart())
            {
                item.useColor = true;
                item.material = null;
                item.prefab = null;
                Color c = Color.white;
                if (!string.IsNullOrEmpty(s.hex)) ColorUtility.TryParseHtmlString(s.hex, out c);
                item.color = c;
            }
            else
            {
                item.useColor = false;
                item.material = null;
                if (item.prefab == null)
                {
                    item.prefab = FindAsset<GameObject>(s.prefabName, "t:Prefab") ?? FindAsset<GameObject>(s.prefabName, "t:Model");
                    if (item.prefab == null) missing.AppendLine($"  프리팹 {s.prefabName} 없음 → {s.name}");
                }
            }
        }

        // 예전 방식으로 만든 복제 재질은 더 이상 쓰지 않으므로 정리
        if (AssetDatabase.IsValidFolder(OldMatFolder)) AssetDatabase.DeleteAsset(OldMatFolder);

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);

        Debug.Log($"[OptionCatalog] {catalog.Items.Count}개 옵션 준비 완료 ({CatalogPath})");
        if (missing.Length > 0)
            Debug.LogWarning("[OptionCatalog] 못 찾은 프리팹 (Modern Apartment를 Import했는지 확인):\n" + missing);
    }

    // 이름이 정확히 같은 에셋을 찾는다 (sm_painting 검색에 sm_painting_2가 걸리지 않게)
    static T FindAsset<T>(string exactName, string typeFilter) where T : Object
    {
        foreach (string guid in AssetDatabase.FindAssets($"{exactName} {typeFilter}"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(p) != exactName) continue;
            T asset = AssetDatabase.LoadAssetAtPath<T>(p);
            if (asset != null) return asset;
        }
        return null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
