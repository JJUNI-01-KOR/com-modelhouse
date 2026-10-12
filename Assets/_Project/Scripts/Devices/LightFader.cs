using System.Collections;
using UnityEngine;

// 조명 켜기·끄기 공통 동작 (재사용용). 구역 조명(ZoneLight) 말고도 무엇이든 이 클래스로 조명을 켜고 끌 수 있다.
//   예) 다른 센서·버튼 스크립트에서 GetComponent<LightFader>().TurnOn() / TurnOff()
// - TurnOn(): 현재 밝기에서 fadeTime 동안 100% (이미 켜져 있으면 그대로, 꺼짐 예약은 취소)
// - TurnOff(): offDelay 기다린 뒤 fadeTime 동안 0%. 그 사이 TurnOn()이 오면 끄지 않음
// - 기록: "조명 켜기 시작" / "조명 끄기 시작" / "조명 꺼짐" (FR-25, 대상 이름은 LogName)
// 천장 조명 Light는 Realtime 또는 Mixed여야 밝기가 바뀐다 (Baked는 안 바뀜).
public class LightFader : MonoBehaviour
{
    [Tooltip("켜고 끌 조명. 비우면 자식의 Light 전부")]
    [SerializeField] protected Light[] lights;
    [Tooltip("0이면 각 Light에 설정된 Intensity를 100%로 쓴다")]
    [SerializeField] protected float maxIntensity = 0f;
    [SerializeField] protected float fadeTime = 1f;
    [SerializeField] protected float offDelay = 3f;

    float brightness;          // 0 ~ 1
    float[] fullIntensity;
    Coroutine routine;

    public bool IsOn() => brightness > 0f;
    public float Brightness => brightness;
    // 기록에 쓰는 대상 이름 (구역 조명은 구역 이름)
    protected virtual string LogName => name;

    protected virtual void Awake()
    {
        if (lights == null || lights.Length == 0) lights = GetComponentsInChildren<Light>();
        lights = System.Array.FindAll(lights, l => l != null);   // Inspector에서 빈 칸으로 남은 항목은 무시
        fullIntensity = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            fullIntensity[i] = maxIntensity > 0f ? maxIntensity : lights[i].intensity;
        brightness = 0f;
        ApplyBrightness();
    }

    public void TurnOn()
    {
        StopRoutine();                       // 꺼짐 예약 취소 또는 어두워지는 중 멈춤
        if (brightness >= 1f) return;        // 이미 켜져 있으면 그대로
        HomeEvents.Log(LogName, "조명 켜기 시작");
        routine = StartCoroutine(FadeIn());
    }

    public void TurnOff()
    {
        StopRoutine();
        routine = StartCoroutine(FadeOutAfterDelay());
    }

    public void ResetLight()
    {
        StopRoutine();
        brightness = 0f;
        ApplyBrightness();
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

        HomeEvents.Log(LogName, "조명 끄기 시작");
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
        HomeEvents.Log(LogName, "조명 꺼짐");
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
}
