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
    bool paused;
    Coroutine routine;

    // 커피 작업 일시정지 (CoffeeTask가 부름): 추출 시간과 김 연출을 같이 멈춘다
    public void SetPaused(bool p)
    {
        if (paused == p) return;
        paused = p;
        if (!isBrewing) return;
        if (effect != null) effect.SetPaused(p);
        else if (steam != null) { if (p) steam.Pause(true); else steam.Play(true); }
    }

    [Tooltip("추출구 오브젝트. 비우면 자식 'Spout'을 찾는다. 있으면 Play 시작 때 cupHolder를 추출구 바로 아래로 맞춘다")]
    [SerializeField] Transform spout;
    [Tooltip("추출구 끝에서 안쪽으로 이만큼 들어온 곳 아래에 컵 가운데를 둔다 (m)")]
    [SerializeField] float spoutTipInset = 0.025f;

    void Awake()
    {
        if (steam != null) steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        AlignHolderToSpout();
    }

    // 머신 몸체·추출구를 옮겨도 컵 받는 자리가 추출구 아래를 따라가게 한다 (높이는 그릴 윗면)
    void AlignHolderToSpout()
    {
        if (spout == null) spout = transform.Find("Spout");
        if (spout == null || cupHolder == null) return;
        Vector3 fwd = spout.forward; fwd.y = 0f;
        float half = spout.lossyScale.z * 0.5f;
        Vector3 under = spout.position + (fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.zero) * Mathf.Max(0f, half - spoutTipInset);
        cupHolder.position = new Vector3(under.x, cupHolder.position.y, under.z);
        SitHolderOnGrille();
    }

    // 컵 바닥이 물받이 그릴 속에 묻히지 않게 cupHolder 높이를 그릴 윗면에 맞춘다
    // (그릴이 없으면 그대로). 컵이 그릴을 뚫고 들어가거나 끌리며 지나가는 것 방지
    void SitHolderOnGrille()
    {
        float top = float.MinValue;
        Vector3 h = cupHolder.position;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            string n = r.name.ToLower();
            if (!n.Contains("grille") && !n.Contains("griddle")) continue;
            Bounds b = r.bounds;
            if (h.x < b.min.x - 0.06f || h.x > b.max.x + 0.06f || h.z < b.min.z - 0.06f || h.z > b.max.z + 0.06f) continue;
            top = Mathf.Max(top, b.max.y);
        }
        if (top > h.y) cupHolder.position = new Vector3(h.x, top + 0.002f, h.z);
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

        // 일시정지 중에는 추출 시간을 멈춘다
        float t = 0f;
        while (t < brewTime)
        {
            yield return null;
            if (!paused) t += Time.deltaTime;
        }

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
        paused = false;
        if (effect != null) effect.StopBrew();
        if (steam != null) steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
