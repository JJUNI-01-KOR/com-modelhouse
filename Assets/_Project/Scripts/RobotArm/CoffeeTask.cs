using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 커피 로봇 작업 (FR-13, TC-16, TC-33)
// 커피 로봇팔 클릭(HomeEvents.OnArmClicked) →
//   집는 중: 컵 앞 → 컵 → 그리퍼 닫고 고정 → 들어 올리기
//   옮기는 중: 추출구 앞 → 추출구 아래
//   커피 받는 중: 컵을 든 채 5초 추출 (컵을 내려놓지 않음)
//   옮기는 중: 추출구 앞 → 식탁 컵 자리 위
//   놓는 중: 내려놓고 그리퍼 열고 물러나기 → 홈 → 대기, "커피 완료"
// 실패(팔이 안 닿음, 5초 안에 못 닿음): 작업 실패 → 컵 원위치 → 홈 → 대기
//
// 위치는 모두 "컵 피벗(바닥 가운데)" 기준이다. 컵 피벗이 가운데라면 graspHeight를 0으로 둔다.
public class CoffeeTask : MonoBehaviour
{
    [SerializeField] string logTarget = "주방";
    [SerializeField] RobotArm arm;
    [SerializeField] CoffeeMachine machine;
    [Tooltip("머그컵 (자식으로 커피 표시용 'CoffeeLiquid' 오브젝트, 처음엔 꺼 둠)")]
    [SerializeField] Transform cup;
    [Tooltip("컵 원위치 = 초기화 때 컵을 되돌릴 곳. 비우면 시작 위치")]
    [SerializeField] Transform cupSpot;
    [Tooltip("커피를 다 받은 컵을 놓을 곳. 비우면 컵을 집었던 자리")]
    [SerializeField] Transform tableSpot;

    [Header("동작 거리 (m)")]
    [Tooltip("컵 피벗에서 그리퍼로 잡을 높이")]
    [SerializeField] float graspHeight = 0.05f;
    [Tooltip("잡기 전·놓은 뒤 컵 앞에서 기다리는 거리")]
    [SerializeField] float approachDistance = 0.10f;
    [Tooltip("컵을 들어 올리는 높이")]
    [SerializeField] float liftHeight = 0.08f;
    [Range(0f, 1f)]
    [SerializeField] float gripAmount = 0.6f;

    [Header("테스트")]
    [Tooltip("Play 중 C 키로 로봇팔 클릭을 흉내 낸다. 시연 빌드에서는 끈다")]
    [SerializeField] bool testKey = true;

    bool isRunning;
    Coroutine routine;
    Vector3 cupStartPos;
    Quaternion cupStartRot;
    Vector3 pickPos;
    Quaternion pickRot;

    void Start()
    {
        if (cup != null)
        {
            cupStartPos = cupSpot != null ? cupSpot.position : cup.position;
            cupStartRot = cupSpot != null ? cupSpot.rotation : cup.rotation;
            if (machine != null) machine.SetFilled(cup, false);
        }
    }

    void OnEnable()
    {
        HomeEvents.OnArmClicked += OnArmClicked;
        HomeEvents.OnScenarioReset += ResetTask;
    }

    void OnDisable()
    {
        HomeEvents.OnArmClicked -= OnArmClicked;
        HomeEvents.OnScenarioReset -= ResetTask;
    }

#if ENABLE_INPUT_SYSTEM
    void Update()
    {
        if (testKey && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            HomeEvents.RaiseArmClicked(arm);
    }
#endif

    void OnArmClicked(RobotArm clicked)
    {
        if (clicked == arm) Run();
    }

    public bool HasCup() => cup != null && cup.gameObject.activeInHierarchy;

    public bool CanStart() => !isRunning && arm != null && machine != null
                              && arm.GetState() == ArmState.Idle && !machine.IsBrewing();

    public void Run()
    {
        if (!CanStart()) return;                // 작업 중 다시 클릭: 무시 (FR-13 예외)
        if (!HasCup())
        {
            HomeEvents.Log(logTarget, "컵 없음");
            return;
        }
        HomeEvents.Log(logTarget, "커피 만들기 시작");
        routine = StartCoroutine(Sequence());
    }

    public void Cancel() => ResetTask();

    IEnumerator Sequence()
    {
        isRunning = true;
        pickPos = cup.position;
        pickRot = cup.rotation;
        Vector3 placePos = tableSpot != null ? tableSpot.position : pickPos;
        Quaternion placeRot = tableSpot != null ? tableSpot.rotation : pickRot;
        Vector3 holderPos = machine.GetCupHolder().position;

        // 1. 집는 중 (PickCup)
        arm.SetState(ArmState.Picking);
        yield return arm.SetGripRoutine(0f);
        yield return arm.MoveToRoutine(Front(pickPos), Facing(pickPos));
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }
        yield return arm.MoveToRoutine(Grasp(pickPos), Facing(pickPos));
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }
        yield return arm.SetGripRoutine(gripAmount);
        arm.Grab(cup);
        yield return arm.MoveToRoutine(Lift(pickPos), Facing(pickPos));
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }

        // 2. 옮기는 중 → 추출구 아래 (PlaceOnMachine: 내려놓지 않고 든 채로 멈춤)
        arm.SetState(ArmState.Carrying);
        yield return arm.MoveToRoutine(Front(holderPos), Facing(holderPos));
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }
        yield return arm.MoveToRoutine(Grasp(holderPos), Facing(holderPos));
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }

        // 3. 커피 받는 중 (WaitBrew)
        arm.SetState(ArmState.WaitingBrew);
        machine.Brew(cup);
        while (machine.IsBrewing()) yield return null;

        // 4. 옮기는 중 → 식탁 컵 자리 위
        arm.SetState(ArmState.Carrying);
        yield return arm.MoveToRoutine(Front(holderPos), Facing(holderPos));
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }
        yield return arm.MoveToRoutine(Lift(placePos), Facing(placePos));
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }

        // 5. 놓는 중 (PlaceOnTable)
        arm.SetState(ArmState.Placing);
        yield return arm.MoveToRoutine(Grasp(placePos), Facing(placePos));
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }
        arm.Release();
        cup.SetPositionAndRotation(placePos, placeRot);
        yield return arm.SetGripRoutine(0f);
        yield return arm.MoveToRoutine(Front(placePos), Facing(placePos));

        // 6. 기본 자세 → 대기
        yield return arm.GoHomeRoutine();
        arm.SetState(ArmState.Idle);
        HomeEvents.Log(logTarget, "커피 완료");
        isRunning = false;
        routine = null;
    }

    // 목표에 닿지 못함 → 작업 실패 → 기본 자세 → 대기 (FR-15 예외)
    IEnumerator Fail()
    {
        arm.SetState(ArmState.Failed);
        if (arm.IsHolding)
        {
            arm.Release();
            cup.SetPositionAndRotation(pickPos, pickRot);
        }
        machine.ResetMachine();
        yield return arm.SetGripRoutine(0f);
        yield return arm.GoHomeRoutine();
        arm.SetState(ArmState.Idle);
        isRunning = false;
        routine = null;
    }

    // ── 손끝 목표 계산: 로봇 베이스에서 컵 쪽으로 수평으로 뻗어 옆에서 잡는다 ──

    Vector3 Direction(Vector3 target)
    {
        Vector3 d = target - arm.BasePosition;
        d.y = 0f;
        return d.sqrMagnitude > 1e-6f ? d.normalized : arm.transform.forward;
    }

    Quaternion Facing(Vector3 target) => Quaternion.LookRotation(Direction(target), Vector3.up);
    Vector3 Grasp(Vector3 cupPivot) => cupPivot + Vector3.up * graspHeight;
    Vector3 Front(Vector3 cupPivot) => Grasp(cupPivot) - Direction(cupPivot) * approachDistance;
    Vector3 Lift(Vector3 cupPivot) => Grasp(cupPivot) + Vector3.up * liftHeight;

    // 초기화 (FR-26 3~5번): 작업 중단, 팔 홈·대기, 머신 대기, 컵 비우고 원위치
    public void ResetTask()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        isRunning = false;
        if (arm != null) arm.ResetArm();
        if (machine != null) machine.ResetMachine();
        if (cup != null)
        {
            cup.SetPositionAndRotation(cupStartPos, cupStartRot);
            if (machine != null) machine.SetFilled(cup, false);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (cup == null) return;
        Vector3 p = cup.position + Vector3.up * graspHeight;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(p, 0.02f);
        if (machine != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(machine.GetCupHolder().position + Vector3.up * graspHeight, 0.02f);
        }
    }
}
