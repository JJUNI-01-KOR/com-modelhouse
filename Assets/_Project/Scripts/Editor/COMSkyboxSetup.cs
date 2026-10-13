using UnityEditor;
using UnityEngine;

// 시간대별 스카이박스 준비 (10/13 강사 피드백 3번, FR-30 하늘 배경 · 부록 A-8)
// 메뉴 없음 → COM → 전체 세팅·점검 (1~12)이 부른다 (COMSetupAll). 몇 번 눌러도 같은 결과.
//   - Assets/_Project/Textures/Skybox/Sky_06·09·12·18·22.png (파노라마 360° 하늘, 해는 +Z 쪽에 그려 둠)
//     → 가져오기 설정을 하늘용으로 맞춘다 (밉맵 끔, 가로 Repeat·세로 Clamp, 2048)
//   - Assets/_Project/Resources/Skybox/Sky_XX.mat (Skybox/Panoramic, 위도·경도 배치) 를 만들거나 갱신
// 실행 중 교체는 SkyboxTimeOfDay(Devices)가 한다. 씬의 Skybox 설정(굽기용)은 건드리지 않는다.
internal static class COMSkyboxSetup
{
    const string TexDir = "Assets/_Project/Textures/Skybox";
    const string MatDir = "Assets/_Project/Resources/Skybox";
    static readonly int[] Hours = { 6, 9, 12, 18, 22 };

    public static void Setup(COMDay13.Report r)
    {
        r.Section("시간대별 스카이박스 (피드백 3번)");
        Shader sh = Shader.Find("Skybox/Panoramic");
        if (sh == null) { r.Check(false, "Skybox/Panoramic 셰이더를 못 찾음"); return; }
        EnsureFolder(MatDir);

        int ok = 0;
        foreach (int h in Hours)
        {
            string texPath = $"{TexDir}/Sky_{h:00}.png";
            var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (ti == null) { r.Check(false, $"{texPath} 없음"); continue; }
            if (ti.mipmapEnabled || ti.wrapModeU != TextureWrapMode.Repeat || ti.wrapModeV != TextureWrapMode.Clamp || ti.maxTextureSize != 2048)
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.mipmapEnabled = false;          // 밉맵이 있으면 파노라마 이음새(뒤쪽)에 선이 보임
                ti.wrapModeU = TextureWrapMode.Repeat;
                ti.wrapModeV = TextureWrapMode.Clamp;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);

            string matPath = $"{MatDir}/Sky_{h:00}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(sh) { name = $"Sky_{h:00}" }; AssetDatabase.CreateAsset(m, matPath); }
            m.shader = sh;
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Mapping", 1f);          // Latitude Longitude Layout
            m.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            m.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            m.SetFloat("_ImageType", 0f);        // 360°
            m.SetFloat("_Exposure", 1f);
            m.SetFloat("_Rotation", 0f);         // 실행 중 햇빛 방향에 맞춰 돌림
            EditorUtility.SetDirty(m);
            ok++;
        }
        AssetDatabase.SaveAssets();
        r.Check(ok == Hours.Length, $"스카이박스 재질 {ok}/{Hours.Length}개 ({MatDir})");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
