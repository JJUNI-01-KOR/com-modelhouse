using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 거울 반사를 시각에 맞춘다 (10/13, 시간대별 스카이박스와 짝)
// COM → 전체 세팅·점검이 거울 전용 프로브를 시각마다(06·09·12·18·22) 그 시각의 하늘·햇빛·창유리로 미리 찍어
// 이 컴포넌트에 넣어 둔다.
// 실행 중: URP Forward+는 반사 프로브를 아틀라스에 올려 재사용해서, 같은 프로브의 사진(customBakedTexture)만 바꾸면
// 화면에 반영되지 않는다 → 시작할 때 시각마다 같은 설정의 프로브를 하나씩 만들어 두고, 시각 버튼마다 그 시각 것만 켠다.
// 사진 자체가 시각별 밝기를 담고 있으므로 ReflectionTimeOfDay의 밝기 조절 대상에서는 빠진다.
[RequireComponent(typeof(ReflectionProbe))]
public class MirrorProbeHours : MonoBehaviour
{
    [SerializeField] int[] hours = new int[0];
    [SerializeField] Texture[] cubes = new Texture[0];

    readonly List<ReflectionProbe> perHour = new List<ReflectionProbe>();

    public void Set(int[] h, Texture[] c) { hours = h; cubes = c; }   // 에디터 세팅용

    void Awake()
    {
        ReflectionProbe src = GetComponent<ReflectionProbe>();
        if (hours == null || cubes == null || hours.Length == 0) return;

        for (int i = 0; i < hours.Length && i < cubes.Length; i++)
        {
            var go = new GameObject($"{name}_{hours[i]:00}h (자동)");
            go.transform.SetParent(transform, false);
            ReflectionProbe p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Custom;
            p.customBakedTexture = cubes[i];
            p.size = src.size;
            p.center = src.center;
            p.boxProjection = src.boxProjection;
            p.importance = src.importance;
            p.blendDistance = src.blendDistance;
            p.intensity = src.intensity;
            p.resolution = src.resolution;
            p.hdr = src.hdr;
            p.enabled = false;
            perHour.Add(p);
        }
        src.enabled = false;   // 원래 프로브는 끄고 시각별 프로브 중 하나만 켠다
    }

    void OnEnable()  { HomeEvents.OnHourChanged += Apply; }
    void OnDisable() { HomeEvents.OnHourChanged -= Apply; }

    void Start()
    {
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            if (mb is ITimeOfDay t) { Apply(t.CurrentHour >= 0 ? t.CurrentHour : 12); return; }
        Apply(12);
    }

    void Apply(int hour)
    {
        if (perHour.Count == 0) return;
        int best = 0, diff = int.MaxValue;
        for (int i = 0; i < perHour.Count; i++)
        {
            int d = Mathf.Abs(hours[i] - hour);
            if (d < diff) { diff = d; best = i; }
        }
        for (int i = 0; i < perHour.Count; i++) perHour[i].enabled = i == best;
    }
}
