using System.Collections;
using UnityEngine;

// 재실 감지 조명 (FR-04, FR-05, TC-04 ~ TC-07)
// - 입실: 현재 밝기에서 1초 이내에 100%
// - 퇴실: 3초 기다린 뒤 1초 동안 현재 밝기에서 0% (퇴실 4초 후 꺼짐)
// - 3초 안에 재입실: 끄지 않음 / 어두워지는 중 재입실: 멈추고 다시 100%
// 천장 조명 Light는 Realtime 또는 Mixed여야 밝기가 바뀐다 (Baked는 안 바뀜).
public class ZoneLight : MonoBehaviour
{
    [SerializeField] ZoneVolume zone;
    [Tooltip("이 구역의 천장 조명. 비우면 자식의 Light 전부")]
    [SerializeField] Light[] lights;
    [Tooltip("0이면 각 Light에 설정된 Intensity를 100%로 쓴다")]
    [SerializeField] float maxIntensity = 0f;
    [SerializeField] float fadeTime = 1f;
    [SerializeField] float offDelay = 3f;

    float brightness;          // 0 ~ 1
    float[] fullIntensity;
    Coroutine routine;

    string ZoneName => zone != null ? zone.DisplayName : name;

    void Awake()
    {
        if (lights == null || lights.Length == 0) lights = GetComponentsInChildren<Light>();
        fullIntensity = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            fullIntensity[i] = maxIntensity > 0f ? maxIntensity : lights[i].intensity;
        brightness = 0f;
        ApplyBrightness();
    }

    void OnEnable()
    {
        HomeEvents.OnZoneEntered += OnZoneEntered;
        HomeEvents.OnZoneExited += OnZoneExited;
        HomeEvents.OnScenarioReset += ResetLight;
    }

    void OnDisable()
    {
        HomeEvents.OnZoneEntered -= OnZoneEntered;
        HomeEvents.OnZoneExited -= OnZoneExited;
        HomeEvents.OnScenarioReset -= ResetLight;
    }

    void OnZoneEntered(ZoneVolume z)
    {
        if (z != zone) return;
        StopRoutine();                       // 꺼짐 예약 취소 또는 어두워지는 중 멈춤
        if (brightness >= 1f) return;        // 이미 켜져 있으면 그대로
        HomeEvents.Log(ZoneName, "조명 켜기 시작");
        routine = StartCoroutine(FadeIn());
    }

    void OnZoneExited(ZoneVolume z)
    {
        if (z != zone) return;
        StopRoutine();
        routine = StartCoroutine(FadeOutAfterDelay());
    }

    IEnumerator FadeIn()
    {
        while (brightness < 1f)
        {
            brightness = Mathf.MoveTowards(brightness, 1f, Time.deltaTime / fadeTime);
            ApplyBrightness();
            yield return null;
        }
        routine = null;
    }

    IEnumerator FadeOutAfterDelay()
    {
        yield return new WaitForSeconds(offDelay);
        if (brightness <= 0f) { routine = null; yield break; }

        HomeEvents.Log(ZoneName, "조명 끄기 시작");
        float start = brightness;
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            brightness = Mathf.Lerp(start, 0f, t / fadeTime);
            ApplyBrightness();
            yield return null;
        }
        brightness = 0f;
        ApplyBrightness();
        HomeEvents.Log(ZoneName, "조명 꺼짐");
        routine = null;
    }

    void StopRoutine()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
    }

    void ApplyBrightness()
    {
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            lights[i].intensity = fullIntensity[i] * brightness;
            lights[i].enabled = brightness > 0f;
        }
    }

    public bool IsOn() => brightness > 0f;
    public float Brightness => brightness;
    public ZoneVolume Zone => zone;

    public void ResetLight()
    {
        StopRoutine();
        brightness = 0f;
        ApplyBrightness();
    }
}
