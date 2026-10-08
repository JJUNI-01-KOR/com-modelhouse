using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 시나리오 초기화 (FR-26). 초기화 사건만 보내고, 대상은 각자 OnScenarioReset을 구독해 되돌아간다.
public class ScenarioManager : MonoBehaviour
{
    [Tooltip("테스트용: Play 중 F5로 초기화. 시연 빌드에서는 끈다")]
    [SerializeField] bool testResetKey = true;

    [ContextMenu("초기화 실행")]
    public void ResetScenario()
    {
        HomeEvents.Log("시스템", "시나리오 초기화");
        HomeEvents.RaiseScenarioReset();
    }

#if ENABLE_INPUT_SYSTEM
    void Update()
    {
        if (testResetKey && Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
            ResetScenario();
    }
#endif
}
