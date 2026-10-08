using UnityEngine;

/// <summary>
/// FR-13 커피 추출 연출 (WBS 3.15) — 김·커피 찬 컵 (SRS v2.0: 소리 삭제)
/// 동작 순서(언제 추출하나)는 CoffeeMachine(김찬중 3.14)이 정하고,
/// 이 컴포넌트는 "보이는 것"만 담당.
///
/// CoffeeMachine.Brew()에서:
///   effect.StartBrew();        // 추출 시작
///   ... 5초 ...
///   effect.StopBrew();         // 추출 끝
///   effect.SetCupFilled(cup, true);
///
/// 붙이는 곳: 커피 머신 오브젝트
///   CoffeeMachine
///    └ Steam (ParticleSystem, Play On Awake 끔)
/// 컵: 머그컵 자식에 "CoffeeLiquid" 이름의 갈색 원판을 꺼 둔 채로 넣어 둠
/// </summary>
public class CoffeeBrewEffect : MonoBehaviour
{
    [SerializeField] private ParticleSystem steam;
    [SerializeField] private string liquidChildName = "CoffeeLiquid";

    private void Awake()
    {
        StopBrew();
    }

    public void StartBrew()
    {
        if (steam != null) steam.Play();
    }

    public void StopBrew()
    {
        // 남은 김 입자는 자연스럽게 사라지게
        if (steam != null) steam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>컵 안 커피 보이기/숨기기. FR-26 초기화 때 false로 "컵 비우기".</summary>
    public void SetCupFilled(Transform cup, bool filled)
    {
        if (cup == null) return;
        Transform liquid = FindDeep(cup, liquidChildName);
        if (liquid != null) liquid.gameObject.SetActive(filled);
        else Debug.LogWarning($"[CoffeeBrewEffect] {cup.name} 아래에 '{liquidChildName}'이 없음", cup);
    }

    private static Transform FindDeep(Transform parent, string childName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }

    // Inspector 우클릭 메뉴로 바로 확인 (Play 중)
    [ContextMenu("테스트: 추출 시작")] private void TestStart() => StartBrew();
    [ContextMenu("테스트: 추출 끝")] private void TestStop() => StopBrew();
}
