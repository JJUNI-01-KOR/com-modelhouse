using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 거울 프로브를 시각별로 찍기 (10/13, 시간대별 스카이박스와 짝)
// 메뉴 없음 → COM → 전체 세팅·점검이 COMMirrorProbes·COMSkyboxSetup 다음에 부른다.
// 시각(06·09·12·18·22)마다 잠깐 아래처럼 맞춘 뒤 거울 전용(Custom) 프로브를 찍고, 끝나면 모두 원래대로 되돌린다.
//   - 하늘: Resources/Skybox/Sky_XX (해 방향으로 회전)
//   - 햇빛: TimeOfDayController의 시각 값(고도·방위·세기·색, 부록 A-8)
//   - 창유리 m_glass: 실행 중과 같게 일반 알파 블렌드 + 불투명도(낮 30%, 밤 12%) + 발광(시각 밝기)
// 결과: Assets/_Project/Settings/MirrorProbes/<프로브>_HH.exr, 프로브에 MirrorProbeHours로 연결
internal static class COMMirrorProbeHours
{
    const string SaveFolder = "Assets/_Project/Settings/MirrorProbes";
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    struct Preset { public int hour; public float el, az, si, glow, tint; public Color sunColor, skyColor; }

    public static void Setup(COMDay13.Report r)
    {
        r.Section("거울 반사 시각별 (스카이박스와 짝)");

        // 1) 시각 값: TimeOfDayController(김찬중 4.09)에 들어 있는 부록 A-8 값
        TimeOfDayController tod = Object.FindAnyObjectByType<TimeOfDayController>();
        if (tod == null) { r.Check(false, "TimeOfDayController 없음 → 시간대 컨트롤러 먼저"); return; }
        var so = new SerializedObject(tod);
        SerializedProperty arr = so.FindProperty("presets");
        float north = so.FindProperty("northOffset")?.floatValue ?? 0f;
        var presets = new List<Preset>();
        for (int i = 0; arr != null && i < arr.arraySize; i++)
        {
            SerializedProperty e = arr.GetArrayElementAtIndex(i);
            presets.Add(new Preset
            {
                hour = e.FindPropertyRelative("hour").intValue,
                el = e.FindPropertyRelative("elevation").floatValue,
                az = e.FindPropertyRelative("azimuth").floatValue,
                si = e.FindPropertyRelative("sunIntensity").floatValue,
                sunColor = e.FindPropertyRelative("sunColor").colorValue,
                skyColor = e.FindPropertyRelative("skyColor").colorValue,
                glow = e.FindPropertyRelative("windowGlow").floatValue,
                tint = e.FindPropertyRelative("windowTint").floatValue,
            });
        }
        if (presets.Count == 0) { r.Check(false, "시각 값(presets)을 못 읽음"); return; }

        // 2) 거울 전용 프로브
        var probes = new List<ReflectionProbe>();
        foreach (ReflectionProbe p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude))
            if (p.mode == ReflectionProbeMode.Custom && p.name.StartsWith("RP_Mirror_")) probes.Add(p);
        if (probes.Count == 0) { r.Check(false, "거울 전용 프로브 없음 (COMMirrorProbes 먼저)"); return; }

        // 3) 원래 상태 저장
        Light sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in Object.FindObjectsByType<Light>()) if (l.type == LightType.Directional) { sun = l; break; }
        Material origSky = RenderSettings.skybox;
        Quaternion sunRot = sun != null ? sun.transform.rotation : Quaternion.identity;
        float sunInt = sun != null ? sun.intensity : 1f;
        Color sunCol = sun != null ? sun.color : Color.white;
        bool sunOn = sun != null && sun.enabled;
        bool sunTemp = sun != null && sun.useColorTemperature;

        Material glass = FindGlass();
        float gSrc = 0f; Color gBase = Color.white, gEmit = Color.black;
        if (glass != null)
        {
            gSrc = glass.HasProperty("_SrcBlend") ? glass.GetFloat("_SrcBlend") : 0f;
            gBase = glass.HasProperty(BaseColorId) ? glass.GetColor(BaseColorId) : Color.white;
            gEmit = glass.HasProperty(EmissionId) ? glass.GetColor(EmissionId) : Color.black;
        }

        var made = new Dictionary<ReflectionProbe, (List<int> h, List<Texture> t)>();
        foreach (ReflectionProbe p in probes) made[p] = (new List<int>(), new List<Texture>());

        try
        {
            foreach (Preset pr in presets)
            {
                // 하늘
                Material src = Resources.Load<Material>($"Skybox/Sky_{pr.hour:00}");
                if (src != null)
                {
                    var sky = new Material(src);
                    sky.SetFloat("_Rotation", Mathf.Repeat(pr.az + north, 360f));
                    RenderSettings.skybox = sky;
                }
                // 햇빛 (TimeOfDayController.Apply와 같은 식)
                if (sun != null)
                {
                    sun.useColorTemperature = false;
                    sun.transform.rotation = Quaternion.Euler(Mathf.Max(pr.el, 1f), pr.az + 180f + north, 0f);
                    sun.color = pr.sunColor;
                    sun.intensity = sunInt * pr.si;
                    sun.enabled = pr.si > 0f;
                }
                // 창유리 (실행 중과 같게)
                if (glass != null)
                {
                    if (glass.HasProperty("_SrcBlend")) glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    Color b = gBase; b.a = pr.hour >= 20 || pr.hour < 5 ? 0.12f : 0.30f;
                    glass.SetColor(BaseColorId, b);
                    float mag = Mathf.Max(gEmit.r, gEmit.g, gEmit.b);
                    glass.SetColor(EmissionId, Color.Lerp(gEmit, pr.skyColor.linear * mag, pr.tint) * pr.glow);
                }

                foreach (ReflectionProbe p in probes)
                {
                    string path = $"{SaveFolder}/{p.name}_{pr.hour:00}.exr";
                    if (Lightmapping.BakeReflectionProbe(p, path))
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                        made[p].h.Add(pr.hour);
                        made[p].t.Add(AssetDatabase.LoadAssetAtPath<Texture>(path));
                    }
                }
            }
        }
        finally
        {
            // 4) 원래대로
            RenderSettings.skybox = origSky;
            if (sun != null)
            {
                sun.transform.rotation = sunRot; sun.intensity = sunInt; sun.color = sunCol;
                sun.enabled = sunOn; sun.useColorTemperature = sunTemp;
            }
            if (glass != null)
            {
                if (glass.HasProperty("_SrcBlend")) glass.SetFloat("_SrcBlend", gSrc);
                glass.SetColor(BaseColorId, gBase);
                glass.SetColor(EmissionId, gEmit);
            }
        }

        // 5) 프로브에 연결
        foreach (ReflectionProbe p in probes)
        {
            var (h, t) = made[p];
            MirrorProbeHours mh = p.GetComponent<MirrorProbeHours>();
            if (mh == null) mh = Undo.AddComponent<MirrorProbeHours>(p.gameObject);
            Undo.RecordObject(mh, "거울 시각별 반사");
            mh.Set(h.ToArray(), t.ToArray());
            EditorUtility.SetDirty(mh);
            int i12 = h.IndexOf(12);
            if (i12 >= 0) { Undo.RecordObject(p, "거울 프로브"); p.customBakedTexture = t[i12]; }
            r.Check(h.Count == presets.Count, $"{p.name}: 시각별 반사 {h.Count}/{presets.Count}장 ({string.Join("·", h)}시)");
        }
    }

    static Material FindGlass()
    {
        foreach (Renderer rd in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            foreach (Material m in rd.sharedMaterials)
                if (m != null && m.name == "m_glass") return m;
        return null;
    }
}
