using System.Collections;
using UnityEngine;

// 커피 머신 (FR-13 3번, FR-26 4번)
// - Brew(cup): 추출 시간(5초) 동안 김을 내고, 끝나면 컵의 자식 "CoffeeLiquid"를 켜서 커피가 찬 모습으로 바꾼다.
//   (예전 이름 "Coffee"도 찾는다)
// - cupHolder: 추출구 아래에서 컵 바닥(피벗)이 와야 할 위치. 빈 오브젝트로 만든다.
// - 언제 추출하나(순서)는 여기서, 김 연출은 CoffeeBrewEffect(권오민 3.15)가 있으면 그쪽에 맡긴다.
public class CoffeeMachine : MonoBehaviour
{
    [SerializeField] string logTarget = "주방";
    [Tooltip("추출구 아래 컵 바닥 위치 (빈 오브젝트)")]
    [SerializeField] Transform cupHolder;
    [SerializeField] float brewTime = 5f;
    [Tooltip("김 연출 (권오민 3.15). 있으면 아래 steam 대신 이걸 쓴다")]
    [SerializeField] CoffeeBrewEffect effect;
    [Tooltip("추출 중 김 효과 (Play On Awake는 끈다). effect가 없을 때만 쓴다")]
    [SerializeField] ParticleSystem steam;
    [Tooltip("컵 안 커피 오브젝트 이름")]
    [SerializeField] string coffeeChildName = "CoffeeLiquid";
    const string LegacyCoffeeName = "Coffee";

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
        if (effect != null) effect.StartBrew();
        else if (steam != null) steam.Play();

        yield return new WaitForSeconds(brewTime);

        if (effect != null) effect.StopBrew();
        else if (steam != null) steam.Stop();
        SetFilled(cup, true);
        isBrewing = false;
        routine = null;
        HomeEvents.Log(logTarget, "커피 추출 끝");
    }

    public void SetFilled(Transform cup, bool filled)
    {
        if (cup == null) return;
        Transform coffee = FindDeep(cup, coffeeChildName) ?? FindDeep(cup, LegacyCoffeeName);
        if (coffee != null) coffee.gameObject.SetActive(filled);
        else Debug.LogWarning($"[CoffeeMachine] {cup.name} 아래에 '{coffeeChildName}'이 없음", cup);
    }

    static Transform FindDeep(Transform parent, string childName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t != parent && t.name == childName) return t;
        return null;
    }

    public bool IsBrewing() => isBrewing;
    public Transform GetCupHolder() => cupHolder != null ? cupHolder : transform;

    public void ResetMachine()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        isBrewing = false;
        if (effect != null) effect.StopBrew();
        if (steam != null) steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
