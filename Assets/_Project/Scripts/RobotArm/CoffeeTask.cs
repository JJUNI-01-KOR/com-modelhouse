using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 커피 로봇 작업 (FR-13, TC-16, TC-33)
// 커피 로봇팔 클릭(HomeEvents.OnArmClicked) →
//   0) 받침대에 지난번 커피 잔이 있으면: 받침대에서 집어 싱크대에 넣는다 (싱크대에 그대로 둠)
//      보관대 컵을 다 쓰고 다시 1번 컵 차례가 되면, 싱크대의 컵들이 모두 보관대 제자리로 돌아온다
//   1) 집는 중: 보관대의 다음 컵 위 → 직선으로 내려가 잡기
//   2) 옮기는 중: 커피 머신 앞 → 수평으로 추출구 아래까지 밀어 넣기
//   3) 커피 받는 중: 컵을 든 채 추출
//   4) 옮기는 중 → 놓는 중: 받침대 위 → 내려놓고 그리퍼 열기
//   5) 일자 홈 → 대기, "커피 완료". 커피 잔은 다음 클릭 때까지 받침대에 둔다
// 모든 이동은 RobotArm.GoToRoutine의 정해진 패턴(물러나기 → 운반 자세 → 조인트1 회전 → 펴기 → 직선 진입)을 쓴다.
// 실패(팔이 안 닿음): 작업 실패 → 컵 보관대로 → 홈 → 대기
// 위치는 모두 "컵 피벗(바닥 가운데)" 기준이다.
public class CoffeeTask : MonoBehaviour
{
    [SerializeField] string logTarget = "주방";
    [SerializeField] RobotArm arm;
    [SerializeField] CoffeeMachine machine;
    [Tooltip("머그컵 (자식으로 커피 표시용 'CoffeeLiquid' 오브젝트, 처음엔 꺼 둠). 컵 보관대를 쓰면 무시")]
    [SerializeField] Transform cup;
    [Tooltip("컵 원위치 = 초기화 때 컵을 되돌릴 곳. 비우면 시작 위치. 컵 보관대를 쓰면 무시")]
    [SerializeField] Transform cupSpot;
    [Tooltip("커피를 다 받은 컵을 놓을 곳 (찻잔 받침대 윗면). 비우면 컵을 집었던 자리")]
    [SerializeField] Transform tableSpot;

    [Header("컵 보관대 (선택)")]
    [Tooltip("보관대에 놓인 컵들. 클릭할 때마다 다음 컵을 꺼내 커피를 담아 받침대에 놓는다")]
    [SerializeField] Transform[] rackCups = new Transform[0];

    [Header("싱크대")]
    [Tooltip("다음 잔을 만들기 전에 받침대의 지난 잔을 싱크대에 넣는다")]
    [SerializeField] bool clearToSink = true;
    [Tooltip("싱크대 개수대 오브젝트. 비우면 씬에서 이름에 'sink'가 들어간 오브젝트를 찾아 메쉬 가운데를 쓴다")]
    [SerializeField] Transform sink;
    [Tooltip("싱크대 위치를 직접 정할 때: 컵 바닥이 놓일 곳 (개수대 안). 비우면 자동")]
    [SerializeField] Transform sinkSpot;
    [Tooltip("싱크대 가장자리보다 이만큼 위에서 컵을 놓는다 (m)")]
    [SerializeField] float sinkReleaseAbove = 0.03f;

    [Header("동작 거리 (m)")]
    [Tooltip("컵 피벗에서 그리퍼로 잡을 높이")]
    [SerializeField] float graspHeight = 0.05f;
    [Tooltip("컵을 잡을 때 손을 수평보다 아래로 숙이는 각도 (도). 클수록 손목이 높이 떠서 식탁에 안 닿음")]
    [Range(0f, 50f)]
    [SerializeField] float graspPitch = 25f;
    [Tooltip("커피 머신 추출구 아래로 넣을 때 손 각도 (도). 0 = 수평으로 정면에서 밀어 넣음")]
    [Range(0f, 30f)]
    [SerializeField] float machinePitch = 0f;
    [Tooltip("커피 머신 앞에서 수평으로 밀어 넣기 시작하는 거리 (m)")]
    [SerializeField] float machineEntryDistance = 0.12f;
    [Tooltip("컵 보관대·받침대에서 컵 위 이 높이에서 직선으로 내려와 잡는다 (m)")]
    [SerializeField] float approachAbove = 0.07f;
    [Range(0f, 1f)]
    [SerializeField] float gripAmount = 0.6f;

    [Header("테스트")]
    [Tooltip("Play 중 C 키로 로봇팔 클릭을 흉내 낸다 (에디터에서만)")]
    [SerializeField] bool testKey = true;

    bool isRunning;
    Coroutine routine;
    Transform current;                       // 이번에 쓰는 컵
    Transform onSaucer;                      // 받침대에 놓여 있는 지난 잔
    int nextRack;                            // 다음에 꺼낼 보관대 컵 번호
    readonly Dictionary<Transform, Pose> homePose = new Dictionary<Transform, Pose>();
    readonly HashSet<Transform> inRack = new HashSet<Transform>();
    readonly List<Transform> inSink = new List<Transform>();

    bool UseRack => rackCups != null && rackCups.Length > 0;

    void Start()
    {
        if (UseRack)
        {
            foreach (Transform c in rackCups)
            {
                if (c == null) continue;
                homePose[c] = new Pose(c.position, c.rotation);
                inRack.Add(c);
                if (machine != null) machine.SetFilled(c, false);
            }
        }
        else if (cup != null)
        {
            homePose[cup] = cupSpot != null ? new Pose(cupSpot.position, cupSpot.rotation) : new Pose(cup.position, cup.rotation);
            inRack.Add(cup);
            if (machine != null) machine.SetFilled(cup, false);
        }
        if (clearToSink && sink == null && sinkSpot == null) sink = FindSink();
        if (arm != null && clearToSink && (sink != null || sinkSpot != null))
        {
            Vector3 s = SinkCupPos();
            Debug.Log($"[CoffeeTask] 싱크대 컵 놓는 곳 {s}, 로봇에서 {Flat(s):F2}m" + (arm.CanReach(Grasp(s)) ? "" : " → 팔이 안 닿음! 싱크대 쪽으로 로봇을 옮기거나 sinkSpot 지정"), this);
        }
        else if (clearToSink) Debug.LogWarning("[CoffeeTask] 싱크대를 못 찾음 → sink 또는 sinkSpot 지정", this);
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
        // C 키 테스트는 에디터에서만 (시연 빌드에서는 자동으로 꺼짐)
        if (testKey && Application.isEditor && Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            HomeEvents.RaiseArmClicked(arm);
    }
#endif

    void OnArmClicked(RobotArm clicked)
    {
        if (clicked == arm) Run();
    }

    Transform PickNextCup()
    {
        if (!UseRack) return cup != null && inRack.Contains(cup) ? cup : null;
        for (int k = 0; k < rackCups.Length; k++)
        {
            Transform c = rackCups[(nextRack + k) % rackCups.Length];
            if (c != null && c.gameObject.activeInHierarchy && inRack.Contains(c))
            {
                nextRack = (nextRack + k + 1) % rackCups.Length;
                return c;
            }
        }
        return null;
    }

    public bool HasCup() => UseRack ? System.Array.Exists(rackCups, c => c != null && c.gameObject.activeInHierarchy)
                                    : cup != null && cup.gameObject.activeInHierarchy;

    public bool CanStart() => !isRunning && arm != null && machine != null
                              && arm.GetState() == ArmState.Idle && !machine.IsBrewing();

    public void Run()
    {
        if (!CanStart()) return;                // 작업 중 다시 클릭: 무시 (FR-13 예외)
        HomeEvents.Log(logTarget, "커피 만들기 시작");
        routine = StartCoroutine(Sequence());
    }

    public void Cancel() => ResetTask();

    IEnumerator Sequence()
    {
        isRunning = true;
        Vector3 placePos = tableSpot != null ? tableSpot.position : Vector3.zero;
        Vector3 holderPos = machine.GetCupHolder().position;

        // 0. 받침대의 지난 잔 → 싱크대
        if (clearToSink && onSaucer != null && (sink != null || sinkSpot != null))
        {
            Transform old = onSaucer;
            current = old;
            arm.SetIgnore(old);
            arm.SetState(ArmState.Picking);
            yield return arm.SetGripRoutine(0f);
            Vector3 p = old.position;
            yield return arm.GoToRoutine(Grasp(p), Facing(p), Vector3.up * approachAbove);
            if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }
            yield return arm.SetGripRoutine(gripAmount);
            arm.Grab(old);
            onSaucer = null;

            arm.SetState(ArmState.Carrying);
            Vector3 s = SinkCupPos();
            Vector3 drop = s + Vector3.up * sinkReleaseAbove;
            yield return arm.GoToRoutine(Grasp(drop), Facing(drop), Vector3.up * approachAbove);
            if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }
            arm.SetState(ArmState.Placing);
            arm.Release();
            old.SetPositionAndRotation(SinkBottom(s), Quaternion.Euler(0f, old.eulerAngles.y, 0f));
            machine.SetFilled(old, false);
            yield return arm.SetGripRoutine(0f);
            HomeEvents.Log(logTarget, "지난 잔을 싱크대에 넣음");
            inSink.Add(old);
            current = null;
        }

        // 1. 집는 중: 보관대의 다음 컵
        Transform c = PickNextCup();
        if (c == null && inSink.Count > 0)
        {
            // 보관대 컵을 다 씀 → 싱크대의 컵을 모두 제자리로 되돌리고 1번 컵부터 다시
            foreach (Transform w in inSink) ReturnToRack(w);
            inSink.Clear();
            nextRack = 0;
            HomeEvents.Log(logTarget, "컵을 보관대에 다시 채움");
            c = PickNextCup();
        }
        if (c == null)
        {
            Debug.LogWarning("[CoffeeTask] 보관대에 쓸 컵이 없어요", this);
            yield return arm.GoHomeRoutine();
            arm.SetState(ArmState.Idle);
            isRunning = false; routine = null;
            yield break;
        }
        current = c;
        inRack.Remove(c);
        if (tableSpot == null) placePos = c.position;
        Quaternion placeRot = Quaternion.Euler(0f, c.eulerAngles.y, 0f);
        arm.SetIgnore(c);

        if (!arm.CanReach(Grasp(c.position))) Debug.LogWarning($"[CoffeeTask] 컵({c.name})이 팔이 닿는 범위 밖: 로봇에서 {Flat(c.position):F2}m", this);
        if (!arm.CanReach(Grasp(holderPos))) Debug.LogWarning($"[CoffeeTask] 커피 머신 컵 자리가 팔이 닿는 범위 밖: 로봇에서 {Flat(holderPos):F2}m", this);
        if (!arm.CanReach(Grasp(placePos))) Debug.LogWarning($"[CoffeeTask] 받침대가 팔이 닿는 범위 밖: 로봇에서 {Flat(placePos):F2}m", this);

        arm.SetState(ArmState.Picking);
        yield return arm.SetGripRoutine(0f);
        Vector3 pick = c.position;
        yield return arm.GoToRoutine(Grasp(pick), Facing(pick), Vector3.up * approachAbove);
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }
        yield return arm.SetGripRoutine(gripAmount);
        arm.Grab(c);

        // 2. 옮기는 중 → 커피 머신 (앞에서 수평으로 밀어 넣기)
        arm.SetState(ArmState.Carrying);
        yield return arm.GoToRoutine(Grasp(holderPos), FacingM(holderPos), -ToolForward(holderPos, machinePitch) * machineEntryDistance);
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }

        // 3. 커피 받는 중
        arm.SetState(ArmState.WaitingBrew);
        machine.Brew(c);
        while (machine.IsBrewing()) yield return null;

        // 4. 옮기는 중 → 놓는 중: 받침대
        arm.SetState(ArmState.Carrying);
        yield return arm.GoToRoutine(Grasp(placePos), Facing(placePos), Vector3.up * approachAbove);
        if (!arm.LastMoveSucceeded) { yield return Fail(); yield break; }
        arm.SetState(ArmState.Placing);
        arm.Release();
        c.SetPositionAndRotation(placePos, placeRot);
        yield return arm.SetGripRoutine(0f);
        onSaucer = c;

        // 5. 일자 홈 → 대기
        yield return arm.GoHomeRoutine();
        arm.SetIgnore();
        arm.SetState(ArmState.Idle);
        HomeEvents.Log(logTarget, "커피 완료");
        current = null;
        isRunning = false;
        routine = null;
    }

    void ReturnToRack(Transform c)
    {
        if (c == null) return;
        if (machine != null) machine.SetFilled(c, false);
        if (homePose.TryGetValue(c, out Pose home)) c.SetPositionAndRotation(home.position, home.rotation);
        inRack.Add(c);
    }

    // 목표에 닿지 못함 → 작업 실패 → 컵 보관대로 → 기본 자세 → 대기 (FR-15 예외)
    IEnumerator Fail()
    {
        arm.SetState(ArmState.Failed);
        if (arm.IsHolding) arm.Release();
        if (current != null) { if (current == onSaucer) onSaucer = null; ReturnToRack(current); }
        machine.ResetMachine();
        yield return arm.SetGripRoutine(0f);
        yield return arm.GoHomeRoutine();
        arm.SetIgnore();
        arm.SetState(ArmState.Idle);
        current = null;
        isRunning = false;
        routine = null;
    }

    // ── 싱크대 ──
    Transform FindSink()
    {
        foreach (Renderer r in FindObjectsByType<Renderer>())
            if (r.name.ToLower().Contains("sink")) return r.transform;
        foreach (Transform t in FindObjectsByType<Transform>())
            if (t.name.ToLower().Contains("sink")) return t;
        return null;
    }

    bool SinkBounds(out Bounds b)
    {
        b = default; bool any = false;
        if (sink == null) return false;
        foreach (Renderer r in sink.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    // 컵 바닥이 올 위치: 개수대 가운데, 가장자리 높이
    Vector3 SinkCupPos()
    {
        if (sinkSpot != null) return sinkSpot.position;
        if (SinkBounds(out Bounds b)) return new Vector3(b.center.x, b.max.y, b.center.z);
        return sink != null ? sink.position : arm.BasePosition;
    }

    // 놓은 컵이 떨어져 앉을 곳: 개수대 바닥 쪽
    Vector3 SinkBottom(Vector3 rim)
    {
        if (sinkSpot != null) return sinkSpot.position;
        if (SinkBounds(out Bounds b)) return new Vector3(rim.x, Mathf.Max(b.min.y + 0.02f, rim.y - 0.15f), rim.z);
        return rim;
    }

    // ── 손끝 목표 계산: 로봇 베이스에서 컵 쪽으로 수평으로 뻗어 옆에서 잡는다 ──

    Vector3 Direction(Vector3 target)
    {
        Vector3 d = target - arm.BasePosition;
        d.y = 0f;
        return d.sqrMagnitude > 1e-6f ? d.normalized : arm.transform.forward;
    }

    float Flat(Vector3 p) => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(arm.BasePosition.x, arm.BasePosition.z));

    // 손을 수평보다 pitch만큼 아래로 숙여 잡는다 → 손목·팔뚝이 위로 올라가 식탁에 닿지 않음
    Vector3 ToolForward(Vector3 target, float pitch)
    {
        Vector3 d = Direction(target);
        Vector3 side = Vector3.Cross(Vector3.up, d);
        return Quaternion.AngleAxis(pitch, side) * d;
    }
    Quaternion Facing(Vector3 target) => Quaternion.LookRotation(ToolForward(target, graspPitch), Vector3.up);
    Quaternion FacingM(Vector3 target) => Quaternion.LookRotation(ToolForward(target, machinePitch), Vector3.up);
    Vector3 Grasp(Vector3 cupPivot) => cupPivot + Vector3.up * graspHeight;

    // 초기화 (FR-26 3~5번): 작업 중단, 팔 홈·대기, 머신 대기, 컵 비우고 제자리
    public void ResetTask()
    {
        StopAllCoroutines();
        routine = null;
        isRunning = false;
        nextRack = 0;
        onSaucer = null;
        current = null;
        inSink.Clear();
        if (arm != null) { if (arm.IsHolding) arm.Release(); arm.ResetArm(); arm.SetIgnore(); }
        if (machine != null) machine.ResetMachine();
        foreach (var kv in homePose) ReturnToRack(kv.Key);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        if (UseRack) { foreach (Transform c in rackCups) if (c != null) Gizmos.DrawWireSphere(c.position + Vector3.up * graspHeight, 0.02f); }
        else if (cup != null) Gizmos.DrawWireSphere(cup.position + Vector3.up * graspHeight, 0.02f);
        if (tableSpot != null) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(tableSpot.position + Vector3.up * graspHeight, 0.02f); }
        if (machine != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(machine.GetCupHolder().position + Vector3.up * graspHeight, 0.02f);
        }
        if (sinkSpot != null || sink != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(SinkCupPos() + Vector3.up * graspHeight, 0.03f);
        }
    }
}
