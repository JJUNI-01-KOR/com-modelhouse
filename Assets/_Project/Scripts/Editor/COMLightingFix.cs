using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 10/13 강사 피드백 반영: 바닥 밝기 경계·전체 밝기 (NFR-10)
// 메뉴 없음 → COM → 전체 세팅·점검 (1~12)이 부른다 (COMSetupAll). 몇 번 눌러도 같은 결과.
//   1) 간접광 세기(Indirect Intensity) 2.0
//      창유리 발광 없이 다시 구우면 실내가 어두워져서 튕긴 빛을 올린다. 1.5 → 2.0 (10/13 강사 피드백: 낮인데 어둡다)
//   2) 바닥(sm_floor) 라이트맵 해상도 2배 (Scale in Lightmap 2)
//      바닥이 넓어 텍셀이 성겨서 차트(조각)마다 밝기가 달라 보이던 것(체크무늬 경계)을 줄인다. Stitch Seams 켬.
//   3) 구역 반사 프로브(Baked)끼리 경계에서 섞이게 Blend Distance 1m, Box Projection 켬
//      광택 바닥에서 프로브 박스 경계가 직선으로 보이지 않게. 거울 전용(Custom) 프로브는 건드리지 않음.
// 바꾼 뒤에는 조명을 다시 구워야 반영된다 (COMSetupAll 마지막 '저장하고 굽기').
internal static class COMLightingFix
{
    const float IndirectScale = 2.0f;
    const float FloorLightmapScale = 2f;
    const float ProbeBlendDistance = 1f;
    static readonly string[] FloorNames = { "sm_floor" };

    public static void Setup(COMDay13.Report r)
    {
        r.Section("바닥 경계·밝기 (10/13 피드백)");

        // 1) 간접광 세기
        LightingSettings ls = null;
        try { ls = Lightmapping.lightingSettings; } catch { }
        if (ls != null)
        {
            if (!Mathf.Approximately(ls.indirectScale, IndirectScale))
            {
                Undo.RecordObject(ls, "간접광 세기");
                ls.indirectScale = IndirectScale;
                EditorUtility.SetDirty(ls);
            }
            r.Check(Mathf.Approximately(ls.indirectScale, IndirectScale), $"Indirect Intensity {ls.indirectScale:0.##}");
        }
        else r.Check(false, "Lighting Settings 에셋이 없음 → Lighting 창에서 New로 만든 뒤 다시 실행");

        // 2) 바닥 라이트맵 해상도
        int floors = 0;
        foreach (MeshRenderer mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            if (System.Array.IndexOf(FloorNames, mr.gameObject.name) < 0) continue;
            var so = new SerializedObject(mr);
            SerializedProperty scale = so.FindProperty("m_ScaleInLightmap");
            SerializedProperty stitch = so.FindProperty("m_StitchLightmapSeams");
            if (scale != null) scale.floatValue = FloorLightmapScale;
            if (stitch != null) stitch.boolValue = true;
            so.ApplyModifiedProperties();
            floors++;
            r.Note($"{mr.name}: Scale in Lightmap {FloorLightmapScale}, Stitch Seams 켬");
        }
        r.Check(floors > 0, $"바닥 라이트맵 해상도: {floors}개");

        // 3) 구역 반사 프로브 섞임
        int probes = 0;
        foreach (ReflectionProbe p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude))
        {
            if (p.mode == ReflectionProbeMode.Custom) continue;   // 거울 전용은 COMMirrorProbes 담당
            Undo.RecordObject(p, "반사 프로브 섞임");
            p.blendDistance = ProbeBlendDistance;
            p.boxProjection = true;
            probes++;
        }
        r.Check(probes > 0, $"구역 반사 프로브 {probes}개: Blend Distance {ProbeBlendDistance}m, Box Projection");
    }
}
