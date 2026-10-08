using System.Text;
using UnityEditor;
using UnityEngine;

// 메뉴 Tools → COM → 옵션 카탈로그 만들기 (부록 A-7)
// - Assets/_Project/Data/OptionCatalog.asset 을 만들고 A-7의 옵션 21개와 가격을 채운다.
// - 재질형 옵션은 에셋 원본 재질(m_floor 등)을 복제해 색만 바꾼 임시 재질을 만든다
//   (Assets/_Project/Materials/Options). 원본 재질 파일은 건드리지 않는다.
//   CC0 텍스처(ambientCG)로 바꿀 때는 이 복제 재질만 고치면 된다.
// - 모델형 옵션은 sm_painting_2 같은 이름으로 프리팹을 찾아 넣는다.
// - 다시 실행해도 이름·가격만 갱신하고, 이미 넣은 재질·프리팹은 그대로 둔다.
public static class OptionCatalogBuilder
{
    const string DataFolder = "Assets/_Project/Data";
    const string MatFolder = "Assets/_Project/Materials/Options";
    const string CatalogPath = DataFolder + "/OptionCatalog.asset";

    struct Spec
    {
        public string id, name, sourceMat, prefabName;
        public PartType part;
        public int cost;
        public bool isDefault;
        public Color tint;
        public float smoothness;   // 음수면 원본 유지
    }

    static Spec M(string id, PartType part, string name, int cost, string src, Color tint, float smooth = -1f)
        => new Spec { id = id, part = part, name = name, cost = cost, sourceMat = src, tint = tint, smoothness = smooth };
    static Spec D(string id, PartType part, string name)
        => new Spec { id = id, part = part, name = name, isDefault = true };
    static Spec P(string id, PartType part, string name, int cost, string prefab)
        => new Spec { id = id, part = part, name = name, cost = cost, prefabName = prefab };

    static readonly Spec[] Specs =
    {
        // 바닥재 (m_floor)
        D("floor_oak", PartType.Floor, "원목 마루"),
        M("floor_light", PartType.Floor, "밝은 강마루", 1500000, "m_floor", new Color(1.15f, 1.10f, 1.00f)),
        M("floor_marble", PartType.Floor, "대리석 타일", 3200000, "m_floor", new Color(0.93f, 0.93f, 0.95f), 0.85f),
        // 벽지 (m_walls)
        D("wall_white", PartType.Wall, "화이트 페인트"),
        M("wall_greige", PartType.Wall, "그레이지 실크 벽지", 400000, "m_walls", new Color(0.80f, 0.76f, 0.70f)),
        M("wall_sage", PartType.Wall, "세이지 그린 실크 벽지", 400000, "m_walls", new Color(0.70f, 0.78f, 0.68f)),
        M("wall_fabric", PartType.Wall, "패브릭 질감 벽지", 700000, "m_walls", new Color(0.88f, 0.85f, 0.79f), 0.1f),
        // 주방 상판 (m_countertop)
        D("counter_white", PartType.Countertop, "화이트 상판"),
        M("counter_darkstone", PartType.Countertop, "다크 스톤", 1800000, "m_countertop", new Color(0.18f, 0.18f, 0.20f), 0.7f),
        // 소파 패브릭 (m_white_fabric)
        D("sofa_white", PartType.SofaFabric, "화이트"),
        M("sofa_warmgray", PartType.SofaFabric, "웜 그레이", 300000, "m_white_fabric", new Color(0.62f, 0.59f, 0.56f)),
        M("sofa_navy", PartType.SofaFabric, "네이비", 300000, "m_white_fabric", new Color(0.16f, 0.20f, 0.35f)),
        // 러그 (m_carpet)
        D("rug_default", PartType.Rug, "데모 씬 러그"),
        M("rug_beige", PartType.Rug, "베이지 울 러그", 250000, "m_carpet", new Color(0.85f, 0.78f, 0.65f)),
        M("rug_charcoal", PartType.Rug, "차콜 러그", 250000, "m_carpet", new Color(0.22f, 0.22f, 0.24f)),
        // 그림 액자 (모델형)
        D("painting_default", PartType.Painting, "데모 씬 그림"),
        P("painting_2", PartType.Painting, "그림 액자 2", 150000, "sm_painting_2"),
        P("painting_3", PartType.Painting, "그림 액자 3", 150000, "sm_painting_3"),
        // 화분 (모델형)
        D("plant_default", PartType.Plant, "데모 씬 화분"),
        P("plant_fern", PartType.Plant, "고사리 화분", 80000, "sm_plant_fern"),
        P("plant_broadleaf", PartType.Plant, "넓은잎 화분", 80000, "sm_broadleaf"),
    };

    [MenuItem("Tools/COM/옵션 카탈로그 만들기 (부록 A-7)")]
    public static void Build()
    {
        EnsureFolder(DataFolder);
        EnsureFolder(MatFolder);

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

            if (!string.IsNullOrEmpty(s.sourceMat) && item.material == null)
            {
                item.material = MakeOptionMaterial(s);
                if (item.material == null) missing.AppendLine($"  재질 {s.sourceMat} 없음 → {s.name}");
            }
            if (!string.IsNullOrEmpty(s.prefabName) && item.prefab == null)
            {
                item.prefab = FindAsset<GameObject>(s.prefabName, "t:Prefab") ?? FindAsset<GameObject>(s.prefabName, "t:Model");
                if (item.prefab == null) missing.AppendLine($"  프리팹 {s.prefabName} 없음 → {s.name}");
            }
        }

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);

        Debug.Log($"[OptionCatalog] {catalog.Items.Count}개 옵션 준비 완료 ({CatalogPath})");
        if (missing.Length > 0)
            Debug.LogWarning("[OptionCatalog] 못 찾은 원본 (Modern Apartment를 Import했는지 확인):\n" + missing);
    }

    static Material MakeOptionMaterial(Spec s)
    {
        string path = $"{MatFolder}/opt_{s.id}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Material src = FindAsset<Material>(s.sourceMat, "t:Material");
        if (src == null) return null;

        var mat = new Material(src) { name = "opt_" + s.id };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", s.tint);
        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", s.tint);
        if (s.smoothness >= 0f && mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", s.smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // 이름이 정확히 같은 에셋을 찾는다 (m_floor 검색에 m_floor_tile이 걸리지 않게)
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
