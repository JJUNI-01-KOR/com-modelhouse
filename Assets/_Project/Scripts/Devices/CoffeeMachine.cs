using System.Collections;
using UnityEngine;

// 커피 머신 (FR-13 3번, FR-26 4번)
// - Brew(cup): 추출 시간(5초) 동안 김을 내고, 끝나면 컵의 자식 "Coffee"를 켜서 커피가 찬 모습으로 바꾼다.
// - cupHolder: 추출구 아래에서 컵 바닥(피벗)이 와야 할 위치. 빈 오브젝트로 만든다.
public class CoffeeMachine : MonoBehaviour
{
    [SerializeField] string logTarget = "주방";
    [Tooltip("추출구 아래 컵 바닥 위치 (빈 오브젝트)")]
    [SerializeField] Transform cupHolder;
    [SerializeField] float brewTime = 5f;
    [Tooltip("추출 중 김 효과 (Play On Awake는 끈다)")]
    [SerializeField] ParticleSystem steam;
    [Tooltip("컵 안 커피 오브젝트 이름")]
    [SerializeField] string coffeeChildName = "Coffee";

    bool isBrewing;
    Coroutine routine;

    void Awake()
    {
        if (steam != null) steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void OnEnable() => HomeEvents.OnScenarioReset += ResetMachine;
    void OnDisable() => HomeEvents.OnScenarioReset -= ResetMachine;

    public void Brew(Transform cup)
    {
        if (isBrewing) return;
        routine = StartCoroutine(BrewRoutine(cup));
    }

    IEnumerator BrewRoutine(Transform cup)
    {
        isBrewing = true;
        HomeEvents.Log(logTarget, "커피 추출 시작");
        if (steam != null) steam.Play();

        yield return new WaitForSeconds(brewTime);

        if (steam != null) steam.Stop();
        SetFilled(cup, true);
        isBrewing = false;
        routine = null;
        HomeEvents.Log(logTarget, "커피 추출 끝");
    }

    public void SetFilled(Transform cup, bool filled)
    {
        if (cup == null) return;
        Transform coffee = cup.Find(coffeeChildName);
        if (coffee != null) coffee.gameObject.SetActive(filled);
    }

    public bool IsBrewing() => isBrewing;
    public Transform GetCupHolder() => cupHolder != null ? cupHolder : transform;

    public void ResetMachine()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        isBrewing = false;
        if (steam != null) steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
