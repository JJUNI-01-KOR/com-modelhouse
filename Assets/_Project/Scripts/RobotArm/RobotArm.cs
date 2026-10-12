using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 로봇팔 자체의 상태: 팔이 IsMoving / IsHolding으로 스스로 계산한다 (밖에서 바꾸지 않음)
// 커피 작업 단계(집는 중·옮기는 중·커피 받는 중·놓는 중)는 CoffeeTask의 CoffeeStep
public enum ArmState { Idle, Moving, Holding }

// ArticulationBody 6축 로봇팔 + IK (FR-15, TC-18)
// ★ 이동 방식 (공장 로봇처럼 정해진 패턴, 매번 똑같이 움직임)
//   GoToRoutine(목표, 손 방향, 들어갈 방향):
//     ① 물러나기: 지난번 들어온 길을 거꾸로 직선으로 (컵 위로 / 머신 앞으로) → 안전 높이
//     ② 운반 자세: 팔을 밑동 가까이 높게 접는다 (조인트 1은 그대로)
//     ③ 조인트 1만 회전: 목표 방향을 본다 (접힌 채라 아무것도 안 쓸고 지나감)
//     ④ 목표 위 안전 높이로 팔을 편다
//     ⑤ 직선으로 들어간다 (컵·받침대는 위에서 아래로, 머신은 앞에서 수평으로)
//   GoHomeRoutine: ① → ② → ③ → 일자 홈
// 1) 시작할 때 관절 구조를 읽어 계산용 모델을 만든다.
// 2) 목표 자세는 모델에서 감쇠 최소제곱(DLS) IK로 구한다. 못 구하면 바로 실패(팔이 안 닿음).
// 3) 관절마다 최대 회전 속도(moveSpeed)를 넘지 않게 움직인 뒤, 1초 동안 실제 자세로 미세 보정한다.
// 4) 손끝이 목표에서 2cm를 넘으면 경고만 남긴다 (FR-15).
// Animation Rigging IK는 Transform을 직접 움직이는 방식이라 물리로 움직이는 ArticulationBody와 맞지 않아 직접 계산한다.
// 클릭(FR-13 1번): IClickable → CursorPicker(권오민)가 5m 이내에서 강조·클릭을 보내면 HomeEvents.OnArmClicked로 알린다.
//   로봇팔 링크의 콜라이더를 "Interactable" 레이어로 두어야 커서에 걸린다.
[DisallowMultipleComponent]
public class RobotArm : MonoBehaviour, IClickable
{
    [Header("식별")]
    public string armId = "coffee-arm";
    [Tooltip("이벤트 기록 대상 이름")]
    [SerializeField] string displayName = "커피 로봇팔";

    [Header("구조")]
    [Tooltip("그리퍼 끝 빈 오브젝트. Hand 아래에 두고 파란 Z축이 그리퍼 바깥, 초록 Y축이 위를 향하게")]
    [SerializeField] Transform tip;
    [Tooltip("비우면 자식에서 찾는다 (데모의 PincherController)")]
    [SerializeField] PincherController gripper;
    [Tooltip("홈 자세 관절 각도(도), Base부터 순서대로. 비우면 시작 자세가 홈")]
    [SerializeField] float[] homeAnglesDeg = new float[0];
    [Tooltip("들고 있는 물체(컵)를 손 각도와 상관없이 항상 똑바로 세운다")]
    [SerializeField] bool keepHeldUpright = true;

    [Header("IK (FR-15)")]
    [Tooltip("팔꿈치·손목 관절이 식탁 면(로봇을 놓은 높이)보다 이만큼 위에 있는 자세만 고른다 (m)")]
    [SerializeField] float tableClearance = 0.03f;
    [Tooltip("팔꿈치를 펴진 상태에서 이 각도보다 더 접지 않는다 (도). 더 접으면 아래팔이 위팔에 박힘")]
    [SerializeField] float elbowFoldLimit = 150f;
    [Tooltip("항상 팔꿈치 위·앞으로 뻗기·홈과 같은 손목 방향 자세만 쓴다 (관절끼리 뚫고 넘어가는 것 방지)")]
    [SerializeField] bool lockConfiguration = true;

    [Header("충돌 회피 (식탁·컵·커피 머신·로봇 자신)")]
    [Tooltip("실제 메쉬 크기 상자로 식탁·장애물·로봇 마디끼리 겹치지 않는 자세만 고른다. 끄면 충돌 경고(CheckPath)도 꺼짐")]
    [SerializeField] bool avoidCollisions = true;
    [Tooltip("장애물을 찾을 부모들. 비우면 씬의 CoffeeStation 아래 전부(머신·컵·받침대·트레이)")]
    [SerializeField] Transform[] obstacleRoots = new Transform[0];
    [Tooltip("장애물 상자를 이만큼 줄여서 본다 (m). 살짝 닿는 정도는 허용 → 계획 실패 방지")]
    [SerializeField] float collisionMargin = 0.01f;
    [Tooltip("로봇 마디끼리 검사할 때 상자를 이만큼 줄인다 (m). 관절 덮개가 겹쳐 보이는 정도는 허용")]
    [SerializeField] float selfShrink = 0.015f;
    [Tooltip("시작(대기) 자세를 팔을 위로 곧게 세운 일자 자세로")]
    [SerializeField] bool startStraightUp = true;
    [Tooltip("IK 후보 점수 계산 때 '밑동(조인트 1)을 먼저 돌린다'고 보고 중간 자세까지 검사한다. 실제 이동은 항상 정해진 패턴(조인트1 회전 단계 포함)")]
    [SerializeField] bool baseFirst = true;
    [Tooltip("밑동을 먼저 돌리는 기준 각도 (도). 이보다 작게 돌 때는 한 번에 움직임")]
    [SerializeField] float baseFirstMinDeg = 15f;

    [Header("이동 패턴 (물러나기 → 운반 자세 → 조인트1 회전 → 펴기 → 직선 진입)")]
    [Tooltip("목표 위에서 기다리는 안전 높이: 식탁 면 위 (m). 컵·받침대·머신 물받이보다 높게")]
    [SerializeField] float travelHeight = 0.24f;
    [Tooltip("운반 자세 손끝 높이: 식탁 면 위 (m)")]
    [SerializeField] float carryHeight = 0.32f;
    [Tooltip("운반 자세 손끝 거리: 밑동 중심에서 (m). 작을수록 회전할 때 쓸고 지나가는 원이 작음")]
    [SerializeField] float carryRadius = 0.22f;
    [Tooltip("직선 이동을 나누는 간격 (m)")]
    [SerializeField] float lineStep = 0.01f;
    [Tooltip("팔이 닿는 수평 거리: 밑동 중심에서 (m). 프리팹 관절 구조로 계산하면 최대 0.65, 무리 없는 범위 0.60")]
    [SerializeField] float reachRadius = 0.62f;
    [Tooltip("계획한 동작이 식탁·컵·머신·팔끼리 닿으면 콘솔에 경고 (움직임은 바꾸지 않음, 에디터에서만)")]
    [SerializeField] bool warnCollisions = true;
    [SerializeField] float reachTolerance = 0.02f;
    [SerializeField] float angleTolerance = 10f;
    [Tooltip("관절 최대 회전 속도 (도/초)")]
    [SerializeField] float moveSpeed = 120f;
    [SerializeField] float orientationWeight = 0.2f;
    [SerializeField] float damping = 0.02f;
    [SerializeField] float jointLimitDeg = 270f;

    [Header("충돌")]
    [Tooltip("로봇팔이 부딪히지 않고 지나갈 레이어 (식탁·커피 머신이 있는 레이어)")]
    [SerializeField] LayerMask passThroughLayers = ~0;   // 기본: 모든 레이어 통과 (식탁·가구와 부딪히지 않음, 클릭 판정에는 영향 없음)

    [Header("조명")]
    [Tooltip("햇빛(Directional Light)이 로봇팔을 비추지 않게 한다 → 시각 버튼을 눌러도 로봇팔 색이 노을·밤빛으로 바뀌지 않음. 천장 조명은 그대로 받음 (URP Rendering Layer 1 사용)")]
    [SerializeField] bool ignoreSunlight = true;

    [Header("클릭 강조 (FR-13 1번)")]
    [Tooltip("커서가 올라가면 색을 이만큼 밝게 (OptionPart와 같은 방식)")]
    [SerializeField] float highlightBoost = 1.4f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    Renderer[] highlightRenderers;
    MaterialPropertyBlock mpb;

    // 관절 (회전 관절만, Base부터 순서대로)
    ArticulationBody[] joints;
    float[] homeDeg;

    // 계산용 모델: 루트 다음 링크부터 손끝 링크까지
    Transform rootLink;
    ArticulationBody[] links;
    int[] linkJoint;            // 링크 → 관절 번호 (고정이면 -1)
    Vector3[] relPos0, pivotLocal, axisLocal;
    Quaternion[] relRot0;
    float[] restRad;            // 모델을 읽을 때의 관절 값
    float[] axisSign;
    Vector3 tipRelPos;
    Quaternion tipRelRot;
    bool ready;

    // FK 결과 (관절별 월드 중심·축)
    Vector3[] fkPivot, fkAxis;

    Transform heldObject;
    Vector3 heldOffsetPos;
    Quaternion heldOffsetRot;
    Rigidbody heldBody;
    bool heldWasKinematic;
    readonly List<Collider> disabledColliders = new List<Collider>();

    public bool IsReady => ready;
    public bool IsMoving { get; private set; }
    public bool LastMoveSucceeded { get; private set; }
    public bool IsHolding => heldObject != null;
    public ArmState State => IsMoving ? ArmState.Moving : IsHolding ? ArmState.Holding : ArmState.Idle;
    // 일시정지: 켜면 그 자리에서 멈춰 기다리고, 끄면 하던 이동을 이어서 한다
    public bool Paused { get; private set; }

    public void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        Paused = paused;
        if (paused && ready) HoldCurrent();
        if (paused && gripper != null) gripper.gripState = GripState.Fixed;
    }
    public string DisplayName => displayName;
    public Transform Tip => tip;
    public Vector3 BasePosition => joints != null && joints.Length > 0 ? JointPivotNow(0) : transform.position;

    // ── 초기화 ──

    void Awake()
    {
        if (tip == null)
        {
            Debug.LogError("[RobotArm] tip(그리퍼 끝)을 지정하세요.", this);
            enabled = false;
            return;
        }
        if (gripper == null) gripper = GetComponentInChildren<PincherController>();

        // 손끝에서 루트까지 올라가며 링크를 모은다
        var path = new List<ArticulationBody>();
        for (Transform t = tip; t != null; t = t.parent)
        {
            ArticulationBody ab = t.GetComponent<ArticulationBody>();
            if (ab == null) continue;
            if (ab.isRoot) { rootLink = ab.transform; break; }
            path.Add(ab);
        }
        path.Reverse();
        links = path.ToArray();
        if (rootLink == null && links.Length > 0) rootLink = FindParentLink(links[0]);

        var jointList = new List<ArticulationBody>();
        linkJoint = new int[links.Length];
        for (int k = 0; k < links.Length; k++)
        {
            bool revolute = links[k].jointType == ArticulationJointType.RevoluteJoint;
            linkJoint[k] = revolute ? jointList.Count : -1;
            if (revolute) jointList.Add(links[k]);
        }
        joints = jointList.ToArray();
        axisSign = new float[joints.Length];
        for (int i = 0; i < axisSign.Length; i++) axisSign[i] = 1f;
        fkPivot = new Vector3[joints.Length];
        fkAxis = new Vector3[joints.Length];

        foreach (ArticulationBody ab in GetComponentsInChildren<ArticulationBody>())
            ab.excludeLayers |= passThroughLayers;

        if (ignoreSunlight) ExcludeFromSunlight();
    }

    // 로봇팔 렌더러만 Rendering Layer 1에 두고, 햇빛은 그 층을 빼고, 나머지 조명(천장 조명 등)은 그 층도 비추게 한다
    const int RobotLightLayer = 1 << 1;
    void ExcludeFromSunlight()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.renderingLayerMask = (uint)RobotLightLayer;
        foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Include))
            l.renderingLayerMask = l.type == LightType.Directional ? l.renderingLayerMask & ~RobotLightLayer
                                                                    : l.renderingLayerMask | RobotLightLayer;
    }

    IEnumerator Start()
    {
        if (!enabled || joints.Length == 0) yield break;
        BuildModel();
        yield return CalibrateAxisSigns();

        int n = joints.Length;
        homeDeg = new float[n];
        bool hasHome = homeAnglesDeg != null && homeAnglesDeg.Length == n;
        for (int i = 0; i < n; i++) homeDeg[i] = hasHome ? homeAnglesDeg[i] : restRad[i] * Mathf.Rad2Deg;

        BuildCollisionModel();

        // 홈 각도를 안 정했으면 일자 자세를 홈으로 쓴다
        if (!hasHome && startStraightUp)
        {
            // 일자 자세: 어깨 관절만 0°/+90°/-90° 중 손끝이 가장 높은 것 (팔 전체가 위로 곧게 선 모양)
            float[] q = new float[n];
            float bestY = float.MinValue; float bestShoulder = restRad[Mathf.Min(1, n - 1)];
            foreach (float add in new[] { 0f, 90f, -90f })
            {
                for (int i = 0; i < n; i++) q[i] = restRad[i];
                if (n > 1) q[1] = restRad[1] + add * Mathf.Deg2Rad;
                ForwardKinematics(q, out Vector3 tp, out _);
                if (tp.y > bestY + 0.01f) { bestY = tp.y; bestShoulder = q[1]; }
            }
            for (int i = 0; i < n; i++) homeDeg[i] = restRad[i] * Mathf.Rad2Deg;
            if (n > 1) homeDeg[1] = bestShoulder * Mathf.Rad2Deg;
        }
        if (joints.Length >= 5)
        {
            float d = Mathf.DeltaAngle(restRad[4] * Mathf.Rad2Deg, homeDeg[4]);
            refWristSign = Mathf.Abs(d) > 10f ? (int)Mathf.Sign(d) : 0;
        }
        SnapToHome();
        BuildCarryPose();
        ready = true;
    }

    void OnEnable() => HomeEvents.OnScenarioReset += ResetArm;
    void OnDisable() => HomeEvents.OnScenarioReset -= ResetArm;

    void LateUpdate()
    {
        if (heldObject != null)
        {
            // 컵은 손 각도가 바뀌어도(식탁 25° ↔ 머신 수평) 항상 똑바로 세운다 (커피가 쏟아져 보이지 않게)
            Quaternion rot = tip.rotation * heldOffsetRot;
            if (keepHeldUpright) rot = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
            heldObject.SetPositionAndRotation(tip.TransformPoint(heldOffsetPos), rot);
        }
    }

    static Transform FindParentLink(ArticulationBody ab)
    {
        for (Transform t = ab.transform.parent; t != null; t = t.parent)
            if (t.GetComponent<ArticulationBody>() != null) return t;
        return ab.transform.parent;
    }

    void BuildModel()
    {
        int m = links.Length;
        relPos0 = new Vector3[m];
        relRot0 = new Quaternion[m];
        pivotLocal = new Vector3[m];
        axisLocal = new Vector3[m];
        restRad = new float[joints.Length];
        for (int k = 0; k < m; k++)
        {
            Transform parent = k == 0 ? rootLink : links[k - 1].transform;
            Quaternion inv = Quaternion.Inverse(parent.rotation);
            // 크기(scale)가 1이 아닌 링크가 있어도 맞도록 회전만 되돌린 실제 거리로 저장한다
            relPos0[k] = inv * (links[k].transform.position - parent.position);
            relRot0[k] = inv * links[k].transform.rotation;
            pivotLocal[k] = inv * (parent.TransformPoint(links[k].parentAnchorPosition) - parent.position);
            axisLocal[k] = links[k].parentAnchorRotation * Vector3.right;   // 회전 관절은 앵커의 X축으로 돈다
            if (linkJoint[k] >= 0) restRad[linkJoint[k]] = links[k].jointPosition[0];
        }
        Transform last = m > 0 ? links[m - 1].transform : rootLink;
        tipRelPos = Quaternion.Inverse(last.rotation) * (tip.position - last.position);
        tipRelRot = Quaternion.Inverse(last.rotation) * tip.rotation;
    }

    // 관절 값을 아주 조금 바꿔 보고 실제로 어느 쪽으로 도는지 확인한다 (축 방향 규칙에 기대지 않음)
    IEnumerator CalibrateAxisSigns()
    {
        const float probe = 0.15f;
        for (int i = 0; i < joints.Length; i++) SetJointImmediate(i, (restRad[i] + probe) * Mathf.Rad2Deg);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        for (int k = 0; k < links.Length; k++)
        {
            int i = linkJoint[k];
            if (i < 0) continue;
            Transform parent = k == 0 ? rootLink : links[k - 1].transform;
            Quaternion rel = Quaternion.Inverse(parent.rotation) * links[k].transform.rotation;
            Quaternion delta = rel * Quaternion.Inverse(relRot0[k]);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            float moved = joints[i].jointPosition[0] - restRad[i];
            if (Mathf.Abs(angle) < 1f || float.IsNaN(axis.x) || Mathf.Abs(moved) < 0.01f)
            {
                Debug.LogWarning($"[RobotArm] {joints[i].name} 축 방향 확인 실패, 기본값 사용", this);
                continue;
            }
            axisSign[i] = Mathf.Sign(Vector3.Dot(axis, axisLocal[k]) * angle * moved);
        }

        for (int i = 0; i < joints.Length; i++) SetJointImmediate(i, restRad[i] * Mathf.Rad2Deg);
        yield return new WaitForFixedUpdate();
    }

    // ── 클릭 (IClickable) ──

    public void Highlight(bool on)
    {
        if (highlightRenderers == null) highlightRenderers = GetComponentsInChildren<Renderer>(true);
        if (mpb == null) mpb = new MaterialPropertyBlock();
        foreach (Renderer r in highlightRenderers)
        {
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                mpb.Clear();
                Material m = mats[i];
                if (on && m != null)
                {
                    if (m.HasProperty(BaseColorId)) mpb.SetColor(BaseColorId, m.GetColor(BaseColorId) * highlightBoost);
                    else if (m.HasProperty(ColorId)) mpb.SetColor(ColorId, m.GetColor(ColorId) * highlightBoost);
                }
                r.SetPropertyBlock(mpb, i);   // 빈 블록 = 강조 지우기
            }
        }
    }

    // 작업 중 다시 클릭하면 CoffeeTask가 무시한다 (FR-13 예외)
    public void OnClick() => HomeEvents.RaiseArmClicked(this);

    // ── 공개 동작 (클래스 다이어그램 RobotArm) ──

    public bool CanReach(Vector3 pos)
    {
        if (joints == null || joints.Length == 0) return false;
        Vector3 b = BasePosition;
        float flat = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(b.x, b.z));
        return flat <= reachRadius && pos.y > transform.position.y - 0.05f;
    }

    // ── 정해진 패턴 이동 ──
    // 지난번 들어온 길 (물러날 때 거꾸로 따라감). 0번이 목표 쪽
    readonly List<Vector3> exitPoints = new List<Vector3>();
    float[] carryDeg;          // 운반 자세 (조인트 1 제외하고 이 모양으로 접음)

    float TableY => transform.position.y;

    // pos/rot = 손끝 최종 목표, entryOffset = 목표에서 들어오기 직전 지점까지 (위에서 내려오면 up*거리, 머신은 -앞*거리)
    public IEnumerator GoToRoutine(Vector3 pos, Quaternion rot, Vector3 entryOffset)
    {
        LastMoveSucceeded = false;
        while (!ready) yield return null;
        if (!CanReach(pos)) { Debug.LogWarning($"[RobotArm] 목표가 닿는 범위 밖 ({FlatDist(pos):F2}m > {reachRadius:F2}m)", this); yield break; }
        IsMoving = true;

        Vector3 entry = pos + entryOffset;
        Vector3 above = new Vector3(entry.x, Mathf.Max(entry.y, TableY + travelHeight), entry.z);

        // ①~③ 물러나기 → 운반 자세 → 조인트 1 회전 (목표 위 자세를 미리 풀어 조인트 1 각도를 정함)
        if (!SolveAbove(above, rot, out float[] aboveRad))
        {
            Debug.LogWarning($"[RobotArm] 목표 위 자세를 못 풀었어요 (목표 {pos}, 거리 {FlatDist(pos):F2}m)", this);
            IsMoving = false; yield break;
        }
        yield return Retreat();
        yield return CarryAndTurn(aboveRad[0] * Mathf.Rad2Deg);

        // ④ 목표 위로 펴기
        var cur = CurrentRad();
        var aboveDeg = ToGoalDeg(cur, aboveRad);
        var unfold = new List<float[]> { ToDeg(cur), aboveDeg };
        CheckPath(unfold, "목표 위로 펴기");
        yield return DrivePath(unfold);

        // ⑤ 직선 진입: 위 → 들어가기 직전 → 목표
        var path = new List<float[]>();
        bool ok = PlanLine(Rad(aboveDeg), entry, rot, path) && PlanLine(Rad(path[path.Count - 1]), pos, rot, path, false);
        if (!ok)
        {
            Debug.LogWarning($"[RobotArm] 직선으로 들어가는 길을 못 풀었어요 (목표 {pos})", this);
            IsMoving = false; yield break;
        }
        CheckPath(path, "직선 진입");
        // 들어가기 시작할 때 물러날 길을 먼저 기록 → 들어가는 도중 정지해도 같은 길로 빠져나온다
        exitPoints.Clear();
        exitPoints.Add(entry);
        exitPoints.Add(above);
        yield return DrivePath(path);
        yield return FineTune(pos, rot);

        LastMoveSucceeded = true;
        IsMoving = false;
    }

    public IEnumerator GoHomeRoutine()
    {
        while (!ready) yield return null;
        IsMoving = true;
        yield return Retreat();
        yield return CarryAndTurn(homeDeg[0]);
        var cur = CurrentRad();
        var home = ToGoalDeg(cur, Rad(homeDeg));
        var path = new List<float[]> { ToDeg(cur), home };
        CheckPath(path, "홈으로");
        yield return DrivePath(path);
        exitPoints.Clear();
        IsMoving = false;
    }

    // ① 들어온 길을 거꾸로 직선으로 (지금 손 방향 유지). 기록이 없으면 손끝이 안전 높이보다 낮을 때만 위로 들어 올림
    IEnumerator Retreat()
    {
        // 물러나는 도중 정지해도 다음에 같은 길로 빠져나오도록, 기록은 다 물러난 뒤에 지운다
        var pts = new List<Vector3>(exitPoints);
        if (pts.Count == 0)
        {
            float safe = TableY + travelHeight;
            if (tip.position.y < safe - 0.01f) pts.Add(new Vector3(tip.position.x, safe, tip.position.z));
        }
        if (pts.Count == 0) yield break;
        Quaternion rot = tip.rotation;
        var path = new List<float[]>();
        float[] q = CurrentRad();
        bool first = true;
        foreach (Vector3 p in pts)
        {
            if (!PlanLine(q, p, rot, path, first)) { Debug.LogWarning("[RobotArm] 물러나는 직선 경로를 못 풀어 운반 자세로 바로 감", this); break; }
            q = Rad(path[path.Count - 1]); first = false;
        }
        if (path.Count > 1) { CheckPath(path, "물러나기"); yield return DrivePath(path); }
        exitPoints.Clear();
    }

    // ② 운반 자세로 접고 ③ 조인트 1만 목표 각도로 돌린다
    IEnumerator CarryAndTurn(float baseGoalDeg)
    {
        int n = joints.Length;
        var cur = CurrentRad();
        var c = new float[n];
        for (int i = 0; i < n; i++) c[i] = carryDeg != null ? carryDeg[i] : homeDeg[i];
        var folded = ToGoalDeg(cur, Rad(c));
        folded[0] = cur[0] * Mathf.Rad2Deg;                       // 조인트 1은 그대로
        var turned = (float[])folded.Clone();
        turned[0] = folded[0] + Mathf.DeltaAngle(folded[0], baseGoalDeg);
        if (Mathf.Abs(turned[0]) > jointLimitDeg) turned[0] = baseGoalDeg;
        var path = new List<float[]> { ToDeg(cur), folded, turned };
        CheckPath(path, "운반 자세·조인트1 회전");
        yield return DrivePath(path);
    }

    // 목표 위 자세: 운반 자세를 목표 방향으로 돌린 모양에서 출발해 푼다 → 항상 같은 계열(팔꿈치 위)의 자연스러운 자세
    bool SolveAbove(Vector3 pos, Quaternion rot, out float[] qRad)
    {
        int n = joints.Length;
        var seed = new float[n];
        for (int i = 0; i < n; i++) seed[i] = (carryDeg != null ? carryDeg[i] : homeDeg[i]) * Mathf.Deg2Rad;
        // 운반 자세 손끝 방향과 목표 방향의 차이만큼 조인트 1을 돌린다 (조인트 1 회전 방향 부호는 FK로 확인)
        Vector3 b = BasePosition;
        ForwardKinematics(seed, out Vector3 t0, out _);
        float yaw0 = Mathf.Atan2(t0.x - b.x, t0.z - b.z) * Mathf.Rad2Deg;
        var probe = (float[])seed.Clone(); probe[0] += 0.2f;
        ForwardKinematics(probe, out Vector3 t1, out _);
        float yawSign = Mathf.Sign(Mathf.DeltaAngle(yaw0, Mathf.Atan2(t1.x - b.x, t1.z - b.z) * Mathf.Rad2Deg));
        float yawGoal = Mathf.Atan2(pos.x - b.x, pos.z - b.z) * Mathf.Rad2Deg;
        float j0 = CurrentRad()[0];
        seed[0] = seed[0] + yawSign * Mathf.DeltaAngle(yaw0, yawGoal) * Mathf.Deg2Rad;
        seed[0] = j0 + Mathf.DeltaAngle(j0 * Mathf.Rad2Deg, seed[0] * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        qRad = (float[])seed.Clone();
        if (SolveModel(seed, qRad, pos, rot)) return true;
        // 예비: 여러 시작점으로
        if (PlanIK(pos, rot, out float[] g)) { qRad = Rad(g); return true; }
        return false;
    }

    // 손끝을 from 자세에서 pos/rot까지 직선으로: lineStep 간격마다 바로 앞 자세에서 이어 풀어 관절이 튀지 않게
    bool PlanLine(float[] fromRad, Vector3 pos, Quaternion rot, List<float[]> outDeg, bool addStart = true)
    {
        int n = fromRad.Length;
        ForwardKinematics(fromRad, out Vector3 p0, out Quaternion r0);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Vector3.Distance(p0, pos) / Mathf.Max(0.002f, lineStep), Quaternion.Angle(r0, rot) / 5f)));
        if (addStart) outDeg.Add(ToDeg(fromRad));
        var q = (float[])fromRad.Clone();
        for (int k = 1; k <= steps; k++)
        {
            float t = k / (float)steps;
            var seed = (float[])q.Clone();
            if (!SolveModel(seed, q, Vector3.Lerp(p0, pos, t), Quaternion.Slerp(r0, rot, t))) return false;
            if (JumpDeg(seed, q) > 25f) return false;      // 한 걸음에 관절이 크게 튀면 자세 계열이 바뀐 것
            outDeg.Add(ToGoalDeg(seed, q));
            for (int i = 0; i < n; i++) q[i] = outDeg[outDeg.Count - 1][i] * Mathf.Deg2Rad;
        }
        return true;
    }

    // 도착 후 실제 물리 자세로 미세 보정 (최대 1초)
    IEnumerator FineTune(Vector3 pos, Quaternion rot)
    {
        float start = Time.time;
        while (!IsReached(pos, rot) && Time.time - start < 1f)
        {
            yield return new WaitForFixedUpdate();
            if (Paused) { start += Time.fixedDeltaTime; continue; }
            SolveStepOnRobot(pos, rot, Time.fixedDeltaTime);
        }
        // 목표에 이미 닿은 채로 일시정지되면 루프를 빠져나오므로, 여기서 풀릴 때까지 기다린다
        // (안 기다리면 다음 단계(추출·놓기)가 일시정지 중에 진행됨)
        while (Paused) yield return new WaitForFixedUpdate();
        HoldCurrent();
        // FR-15 도달 허용 오차(2cm) 확인: 시연이 멈추지 않게 실패로 처리하지는 않고 경고만 남긴다
        float err = Vector3.Distance(tip.position, pos);
        if (err > reachTolerance)
            Debug.LogWarning($"[RobotArm] 도달 오차 {err * 100f:F1}cm (허용 {reachTolerance * 100f:F0}cm) 목표 {pos}", this);
    }

    // 계획한 동작을 3°마다 검사해 부딪히면 경고만 (어디서 부딪히는지 알려 주는 용도)
    void CheckPath(List<float[]> pathDeg, string what)
    {
        // 경고 전용이라 에디터에서만 검사 (빌드에서는 계산 비용 0)
        if (!Application.isEditor || !warnCollisions || !avoidCollisions || pathDeg.Count < 2) return;
        int n = joints.Length;
        var q = new float[n];
        // 출발·도착 지점 4cm 안은 건너뜀: 컵이 보관대·받침대·물받이에 닿아 있는 건 정상
        ForwardKinematics(Rad(pathDeg[0]), out Vector3 pStart, out _);
        ForwardKinematics(Rad(pathDeg[pathDeg.Count - 1]), out Vector3 pEnd, out _);
        for (int k = 0; k + 1 < pathDeg.Count; k++)
        {
            float d = 0f;
            for (int i = 0; i < n; i++) d = Mathf.Max(d, Mathf.Abs(pathDeg[k + 1][i] - pathDeg[k][i]));
            int m = Mathf.Max(1, Mathf.CeilToInt(d / 3f));
            for (int s = 1; s <= m; s++)
            {
                for (int i = 0; i < n; i++) q[i] = Mathf.Lerp(pathDeg[k][i], pathDeg[k + 1][i], s / (float)m) * Mathf.Deg2Rad;
                ForwardKinematics(q, out Vector3 tq, out _);
                if (Vector3.Distance(tq, pStart) < 0.04f || Vector3.Distance(tq, pEnd) < 0.04f) continue;
                if (!CollisionFree(q))
                {
                    Debug.LogWarning($"[RobotArm] 경로 충돌 경고: {what} 중 ({lastHit}) 손끝 {tq}", this);
                    return;
                }
            }
        }
    }

    // 경로 전체를 한 번에 부드럽게 (처음·끝만 감속) 따라간다
    IEnumerator DrivePath(List<float[]> pathDeg)
    {
        int n = joints.Length, m = pathDeg.Count;
        if (m == 0) yield break;
        var cum = new float[m];
        for (int k = 1; k < m; k++)
        {
            float d = 0f;
            for (int i = 0; i < n; i++) d = Mathf.Max(d, Mathf.Abs(pathDeg[k][i] - pathDeg[k - 1][i]));
            cum[k] = cum[k - 1] + d;
        }
        float total = cum[m - 1];
        float duration = Mathf.Max(0.1f, 1.5f * total / moveSpeed);
        float t = 0f;
        while (t < duration && total > 0.01f)
        {
            yield return new WaitForFixedUpdate();
            if (Paused) continue;            // 일시정지 중에는 시간을 멈춘다
            t += Time.fixedDeltaTime;
            float L = Mathf.SmoothStep(0f, 1f, t / duration) * total;
            int k = 1; while (k < m - 1 && cum[k] < L) k++;
            float seg = cum[k] - cum[k - 1];
            float u = seg > 1e-5f ? Mathf.Clamp01((L - cum[k - 1]) / seg) : 1f;
            for (int i = 0; i < n; i++) SetDrive(i, Mathf.Lerp(pathDeg[k - 1][i], pathDeg[k][i], u));
        }
        yield return DriveJoints(pathDeg[m - 1]);   // 끝 자세 맞추고 드라이브가 따라올 때까지 대기
    }

    float[] CurrentRad() { var q = new float[joints.Length]; for (int i = 0; i < q.Length; i++) q[i] = joints[i].jointPosition[0]; return q; }
    static float[] ToDeg(float[] rad) { var d = new float[rad.Length]; for (int i = 0; i < d.Length; i++) d[i] = rad[i] * Mathf.Rad2Deg; return d; }
    static float[] Rad(float[] deg) { var r = new float[deg.Length]; for (int i = 0; i < r.Length; i++) r[i] = deg[i] * Mathf.Deg2Rad; return r; }
    static float JumpDeg(float[] a, float[] b)
    {
        float d = 0f;
        for (int i = 0; i < a.Length; i++) d = Mathf.Max(d, Mathf.Abs(Mathf.DeltaAngle(a[i] * Mathf.Rad2Deg, b[i] * Mathf.Rad2Deg)));
        return d;
    }
    float FlatDist(Vector3 p) { Vector3 b = BasePosition; return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(b.x, b.z)); }

    // 운반 자세: 밑동 가까이(carryRadius), 높이(carryHeight)에 손을 수평으로 둔 팔꿈치 위 자세
    void BuildCarryPose()
    {
        Vector3 b = BasePosition;
        ForwardKinematics(Rad(homeDeg), out Vector3 ht, out _);
        Vector3 fwd = ht - b; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
        fwd.Normalize();
        Vector3 p = new Vector3(b.x, TableY + carryHeight, b.z) + fwd * carryRadius;
        // 홈 자세에서 출발해 푼다
        float[] seed = Rad(homeDeg);
        foreach (float pitch in new[] { 0f, 30f, 60f, -30f })
        {
            Quaternion rot = Quaternion.LookRotation(Quaternion.AngleAxis(pitch, Vector3.Cross(Vector3.up, fwd)) * fwd, Vector3.up);
            var q = (float[])seed.Clone();
            if (SolveModel(seed, q, p, rot) && AboveTable(q, tableClearance) && SameConfig(q) && (!avoidCollisions || CollisionFree(q)))
            {
                carryDeg = ToGoalDeg(seed, q);
                return;
            }
        }
        if (PlanIK(p, Quaternion.LookRotation(fwd, Vector3.up), out float[] g)) { carryDeg = g; return; }
        Debug.LogWarning("[RobotArm] 운반 자세를 못 구해 홈(일자) 자세를 운반 자세로 씀 (carryHeight·carryRadius 조정)", this);
        carryDeg = (float[])homeDeg.Clone();
    }

    // 그리퍼 닫기(amount 0~1, 1이 완전히 닫힘) / 열기(0)
    public IEnumerator SetGripRoutine(float amount)
    {
        if (gripper == null) yield break;
        float target = Mathf.Clamp01(amount);
        bool closing = target > gripper.grip;
        gripper.gripState = closing ? GripState.Closing : GripState.Opening;
        float t = 0f;
        while (t < 1.5f)
        {
            if (Paused) { gripper.gripState = GripState.Fixed; yield return null; continue; }
            gripper.gripState = closing ? GripState.Closing : GripState.Opening;
            if (closing ? gripper.grip >= target : gripper.grip <= target) break;
            t += Time.deltaTime;
            yield return null;
        }
        gripper.gripState = GripState.Fixed;
    }

    // 물체를 손끝에 고정한다 (WBS 3.13).
    // Rigidbody가 있는 물체를 관절(ArticulationBody) 아래 자식으로 넣으면 물리 계층이 꼬여서,
    // 부모를 바꾸는 대신 잡은 순간의 상대 위치를 기억해 매 프레임 손끝을 따라가게 한다.
    public void Grab(Transform obj)
    {
        if (obj == null) return;
        if (heldObject != null) Release();

        heldObject = obj;
        heldOffsetPos = tip.InverseTransformPoint(obj.position);
        CaptureHeldBox(obj);
        heldOffsetRot = Quaternion.Inverse(tip.rotation) * obj.rotation;
        heldBody = obj.GetComponent<Rigidbody>();
        if (heldBody != null)
        {
            heldWasKinematic = heldBody.isKinematic;
            heldBody.isKinematic = true;
        }
        disabledColliders.Clear();
        foreach (Collider c in obj.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled) continue;
            c.enabled = false;              // 들고 있는 동안 손가락·식탁과 물리 충돌 방지
            disabledColliders.Add(c);
        }
    }

    public void Release()
    {
        if (heldObject == null) return;
        foreach (Collider c in disabledColliders) if (c != null) c.enabled = true;
        disabledColliders.Clear();
        if (heldBody != null) heldBody.isKinematic = heldWasKinematic;
        heldObject = null;
        heldBody = null;
    }


    // 초기화(FR-26 3번): 기본 자세로 즉시 되돌리고 잡은 것을 놓는다 (자기 OnScenarioReset 구독)
    public void ResetArm()
    {
        if (!ready) return;                 // 시작 직후 축 확인 중에는 건너뜀
        StopAllCoroutines();
        IsMoving = false;
        Paused = false;
        exitPoints.Clear();
        Release();
        SnapToHome();
        if (gripper != null)
        {
            gripper.gripState = GripState.Fixed;
            gripper.grip = 0f;              // 손가락은 다음 물리 프레임에 열린다
        }
    }

    // 정지: 지금 하던 이동을 그 자리에서 멈춘다 (이동을 돌리던 코루틴은 부른 쪽이 멈춘다).
    // 들어온 길 기록은 남겨 두므로 다음 이동은 들어온 길을 거꾸로 빠져나온 뒤 시작한다 (머신 안에서 정지해도 안전)
    public void Stop()
    {
        IsMoving = false;
        Paused = false;
        if (ready) HoldCurrent();
    }

    [ContextMenu("현재 관절 각도 출력 (홈 자세 기록용)")]
    void PrintJointAngles()
    {
        if (joints == null) { Debug.Log("Play 중에 실행하세요."); return; }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < joints.Length; i++)
            sb.Append(joints[i].name).Append(' ').Append(ActualDeg(i).ToString("F1")).Append(i < joints.Length - 1 ? ", " : "");
        Debug.Log("[RobotArm] 관절 각도(도): " + sb);
    }

    // ── 계산용 모델 ──

    // 관절 값(rad)으로 손끝 위치·방향을 구하고, 관절별 월드 중심·축을 fkPivot/fkAxis에 채운다
    void ForwardKinematics(float[] q, out Vector3 tipPos, out Quaternion tipRot)
    {
        Vector3 p = rootLink.position;
        Quaternion r = rootLink.rotation;
        for (int k = 0; k < links.Length; k++)
        {
            int i = linkJoint[k];
            Vector3 localPos = relPos0[k];
            Quaternion localRot = relRot0[k];
            if (i >= 0)
            {
                Vector3 axis = axisLocal[k] * axisSign[i];
                fkPivot[i] = p + r * pivotLocal[k];
                fkAxis[i] = r * axis;
                Quaternion rj = Quaternion.AngleAxis((q[i] - restRad[i]) * Mathf.Rad2Deg, axis);
                localPos = pivotLocal[k] + rj * (relPos0[k] - pivotLocal[k]);
                localRot = rj * relRot0[k];
            }
            p = p + r * localPos;
            r = r * localRot;
            if (fkLinkPos != null) { fkLinkPos[k] = p; fkLinkRot[k] = r; }
        }
        tipPos = p + r * tipRelPos;
        tipRot = r * tipRelRot;
    }

    // 모델에서 목표 관절 각도(도)를 구한다. 지금 자세에서 먼저 풀고, 안 되면 시작점을 바꿔 다시 푼다.
    bool PlanIK(Vector3 pos, Quaternion rot, out float[] goalDeg)
    {
        int n = joints.Length;
        float[] seed = new float[n];
        for (int i = 0; i < n; i++) seed[i] = joints[i].jointPosition[0];

        goalDeg = null;
        float[] q = (float[])seed.Clone();
        if (SolveModel(seed, q, pos, rot))
        {
            float[] g0 = ToGoalDeg(seed, q);
            if (Score(seed, q, g0) == 2) { goalDeg = g0; return true; }
        }

        // 시작점을 바꿔 여러 해를 구하고 점수로 고른다
        //   2점: 도착 자세가 식탁 위 + 팔 마디끼리 안 겹침 + 가는 길에도 안 겹침
        //   1점: 도착 자세만 괜찮음 (가는 길에 잠깐 겹침)
        //   0점: 둘 다 안 됨 (시연이 멈추지 않게 마지막 수단)
        // 같은 점수면 관절이 덜 움직이는 해
        var rng = new System.Random(7);
        int bestScore = -1; float bestDelta = float.MaxValue;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            // 짝수 번째는 홈(대기) 자세 근처에서 시작 → 홈과 같은 자세 계열(팔꿈치 위) 해가 잘 나온다
            bool fromHome = homeDeg != null && homeDeg.Length == n && attempt % 2 == 0;
            for (int i = 0; i < n; i++)
            {
                float baseQ = fromHome ? homeDeg[i] * Mathf.Deg2Rad : seed[i];
                float spread = fromHome ? 0.35f : 0.6f;
                q[i] = baseQ + (float)(rng.NextDouble() * 2 - 1) * Mathf.PI * spread;
            }
            if (!SolveModel(seed, q, pos, rot)) continue;
            float[] g = ToGoalDeg(seed, q);
            float delta = 0f;
            for (int i = 0; i < n; i++) delta = Mathf.Max(delta, Mathf.Abs(g[i] - seed[i] * Mathf.Rad2Deg));
            int sc = Score(seed, q, g);
            if (sc > bestScore || (sc == bestScore && delta < bestDelta)) { bestScore = sc; bestDelta = delta; goalDeg = g; }
            if (bestScore == 2 && attempt >= 12) break;
        }
        if (goalDeg != null && bestScore < 2)
            Debug.LogWarning($"[RobotArm] 식탁·팔 마디와 안 부딪히는 자세를 못 찾아 가장 나은 자세를 씀 (점수 {bestScore}, 목표 {pos}, 로봇에서 수평 {Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(BasePosition.x, 0, BasePosition.z)):F2}m)", this);
        return goalDeg != null;
    }

    int Score(float[] seedRad, float[] qRad, float[] goalDeg)
    {
        if (!PoseOk(qRad, tableClearance)) return 0;
        // 밑동 먼저 → 나머지 관절 순서로 움직인다고 보고 중간 자세를 검사 (PlanIK 후보 점수용)
        int n = qRad.Length;
        var mid = new float[n];
        var goal = new float[n];
        for (int i = 0; i < n; i++) goal[i] = goalDeg[i] * Mathf.Deg2Rad;
        bool basePhase = baseFirst && n > 1 && Mathf.Abs(goalDeg[0] - seedRad[0] * Mathf.Rad2Deg) > baseFirstMinDeg;
        if (basePhase)
        {
            for (int k = 1; k <= 6; k++)
            {
                for (int i = 0; i < n; i++) mid[i] = seedRad[i];
                mid[0] = Mathf.Lerp(seedRad[0], goal[0], k / 6f);
                if (!PoseOk(mid, 0f)) return 1;
            }
        }
        for (int k = 1; k <= 8; k++)
        {
            float t = k / 9f;
            for (int i = 0; i < n; i++) mid[i] = Mathf.Lerp(i == 0 && basePhase ? goal[0] : seedRad[i], goal[i], t);
            if (!PoseOk(mid, 0f)) return 1;
        }
        return 2;
    }

    bool PoseOk(float[] q, float tableClear) =>
        AboveTable(q, tableClear) && SelfClear(q) && SameConfig(q) && (!avoidCollisions || CollisionFree(q));

    // ── 자세 계열 고정 (UR 컨트롤러의 어깨·팔꿈치·손목 설정과 같은 개념) ──
    // 같은 손 위치라도 팔꿈치 위/아래, 손목 위/아래, 앞으로/뒤로 뻗기 조합이 여러 개다.
    // 동작마다 다른 계열을 고르면 그 사이를 이동하면서 손목·팔꿈치가 반대편으로 넘어가며 마디끼리 뚫고 지나간다.
    // → 항상 "팔꿈치 위 + 앞으로 뻗기 + 홈과 같은 쪽 손목" 계열만 쓴다.
    int refWristSign;          // 홈 자세의 손목(4번 관절) 꺾임 방향. 0이면 검사 안 함
    bool SameConfig(float[] q)
    {
        int n = joints.Length;
        if (!lockConfiguration || n < 4) return true;
        ForwardKinematics(q, out Vector3 tipPos, out _);
        Vector3 s = fkPivot[1], e = fkPivot[2], w = fkPivot[3];

        // 팔꿈치 위: 팔꿈치가 어깨-손목을 잇는 선보다 위에 있어야 한다
        Vector3 sw = w - s;
        float t = sw.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(e - s, sw) / sw.sqrMagnitude) : 0f;
        if (e.y < (s + sw * t).y - 0.005f) return false;

        // 앞으로 뻗기: 팔꿈치가 손끝과 같은 쪽에 있어야 한다 (밑동 너머 뒤로 넘어가는 자세 금지)
        Vector3 b = fkPivot[0];
        Vector2 toE = new Vector2(e.x - b.x, e.z - b.z), toT = new Vector2(tipPos.x - b.x, tipPos.z - b.z);
        if (toT.sqrMagnitude > 0.0025f && Vector2.Dot(toE, toT) < -0.01f) return false;

        // 손목 방향: 홈 자세와 같은 쪽으로 꺾인 해만
        if (refWristSign != 0 && n >= 5)
        {
            float d = Mathf.DeltaAngle(restRad[4] * Mathf.Rad2Deg, q[4] * Mathf.Rad2Deg);
            if (Mathf.Abs(d) > 10f && (int)Mathf.Sign(d) != refWristSign) return false;
        }
        return true;
    }

    // 팔 마디끼리 겹치는지 (UR3는 마디가 옆으로 비켜 있어 관절 중심선만으로는 판단이 어려움 → 실제로 부딪히는 경우만 본다)
    //   1) 팔꿈치를 elbowFoldLimit보다 많이 접으면 아래팔이 위팔에 박힘
    //   2) 손(손목 끝~손끝)이 로봇 밑동 기둥에 가까우면 밑동을 뚫음
    bool SelfClear(float[] q)
    {
        if (joints.Length >= 3)
        {
            float fold = Mathf.Abs(Mathf.DeltaAngle(0f, (q[2] - restRad[2]) * Mathf.Rad2Deg));
            if (fold > elbowFoldLimit) return false;
        }
        return true;
    }

    // ═══════════ 실제 모양 기준 충돌 검사 (상자 = OBB) ═══════════
    // 로봇: 링크마다 그 링크에 붙은 메쉬 전체를 감싸는 상자 (그리퍼 손가락은 손 링크에 포함)
    // 장애물: 식탁 면(로봇 밑면 높이) + obstacleRoots 아래 렌더러 상자 (커피 머신·컵·받침대·트레이)
    // 지금 다루는 컵은 SetIgnore로 빼고, 들고 있는 컵은 손에 붙은 상자로 함께 검사
    struct Box { public Vector3 c; public Quaternion r; public Vector3 h; public string name; }
    string lastHit = "";
    Vector3[] fkLinkPos; Quaternion[] fkLinkRot;
    Box[] linkLocalBox;          // 링크 좌표계 기준 상자 (h가 0이면 메쉬 없음)
    Box rootLocalBox; bool hasRootBox;
    Box heldLocalBox; bool hasHeldBox;   // 마지막 링크 좌표계 기준
    readonly List<Box> obstacles = new List<Box>();
    readonly HashSet<Transform> ignoreRoots = new HashSet<Transform>();
    float lastObstacleBuild = -1f;

    public void SetIgnore(params Transform[] roots)
    {
        ignoreRoots.Clear();
        if (roots != null) foreach (Transform t in roots) if (t != null) ignoreRoots.Add(t);
        lastObstacleBuild = -1f;
    }

    void BuildCollisionModel()
    {
        int m = links.Length;
        fkLinkPos = new Vector3[m]; fkLinkRot = new Quaternion[m];
        linkLocalBox = new Box[m];
        var mins = new Vector3[m + 1]; var maxs = new Vector3[m + 1]; var has = new bool[m + 1];
        foreach (Renderer rd in GetComponentsInChildren<Renderer>())
        {
            if (!rd.enabled || !rd.gameObject.activeInHierarchy) continue;
            int k = OwnerIndex(rd.transform);           // -1 = 루트(밑동), -2 = 해당 없음
            if (k == -2) continue;
            Transform link = k < 0 ? rootLink : links[k].transform;
            Bounds lb = rd.localBounds;
            int slot = k + 1;
            foreach (Vector3 corner in Corners(lb))
            {
                Vector3 w = rd.transform.TransformPoint(corner);
                Vector3 l = Quaternion.Inverse(link.rotation) * (w - link.position);
                if (!has[slot]) { mins[slot] = maxs[slot] = l; has[slot] = true; }
                else { mins[slot] = Vector3.Min(mins[slot], l); maxs[slot] = Vector3.Max(maxs[slot], l); }
            }
        }
        for (int k = 0; k < m; k++)
            linkLocalBox[k] = has[k + 1] ? new Box { c = (mins[k + 1] + maxs[k + 1]) * 0.5f, r = Quaternion.identity, h = (maxs[k + 1] - mins[k + 1]) * 0.5f } : new Box();
        hasRootBox = has[0];
        if (hasRootBox) rootLocalBox = new Box { c = (mins[0] + maxs[0]) * 0.5f, r = Quaternion.identity, h = (maxs[0] - mins[0]) * 0.5f };
    }

    // 렌더러가 속한 체인 링크 번호 (체인 밖 손가락 등은 가장 가까운 체인 조상으로)
    int OwnerIndex(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (p == rootLink) return -1;
            ArticulationBody ab = p.GetComponent<ArticulationBody>();
            if (ab == null) continue;
            int idx = System.Array.IndexOf(links, ab);
            if (idx >= 0) return idx;
            if (ab.isRoot) return -1;
        }
        return -2;
    }

    static IEnumerable<Vector3> Corners(Bounds b)
    {
        Vector3 c = b.center, e = b.extents;
        for (int i = 0; i < 8; i++)
            yield return c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
    }

    void CaptureHeldBox(Transform obj)
    {
        hasHeldBox = false;
        if (obj == null || links.Length == 0) return;
        Transform last = links[links.Length - 1].transform;
        Vector3 mn = Vector3.zero, mx = Vector3.zero; bool any = false;
        foreach (Renderer rd in obj.GetComponentsInChildren<Renderer>())
            foreach (Vector3 corner in Corners(rd.localBounds))
            {
                Vector3 l = Quaternion.Inverse(last.rotation) * (rd.transform.TransformPoint(corner) - last.position);
                if (!any) { mn = mx = l; any = true; } else { mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l); }
            }
        if (any) { heldLocalBox = new Box { c = (mn + mx) * 0.5f, r = Quaternion.identity, h = (mx - mn) * 0.5f }; hasHeldBox = true; }
    }

    void BuildObstacles()
    {
        if (Time.time - lastObstacleBuild < 0.05f && lastObstacleBuild >= 0f) return;   // 한 번의 계획 안에서는 재사용
        lastObstacleBuild = Time.time;
        obstacles.Clear();
        var roots = new List<Transform>();
        if (obstacleRoots != null) foreach (Transform t in obstacleRoots) if (t != null) roots.Add(t);
        if (roots.Count == 0) { GameObject st = GameObject.Find("CoffeeStation"); if (st != null) roots.Add(st.transform); }
        foreach (Transform root in roots)
            foreach (Renderer rd in root.GetComponentsInChildren<Renderer>())
            {
                if (!rd.enabled || !rd.gameObject.activeInHierarchy) continue;
                if (rd.transform.IsChildOf(transform)) continue;                       // 로봇 자신
                if (heldObject != null && rd.transform.IsChildOf(heldObject)) continue;  // 들고 있는 컵
                bool skip = false;
                foreach (Transform ig in ignoreRoots) if (rd.transform.IsChildOf(ig)) { skip = true; break; }
                if (skip || rd is ParticleSystemRenderer) continue;
                Bounds lb = rd.localBounds;
                Vector3 h = Vector3.Scale(lb.extents, rd.transform.lossyScale);
                h = new Vector3(Mathf.Abs(h.x), Mathf.Abs(h.y), Mathf.Abs(h.z)) - Vector3.one * collisionMargin;
                if (h.x <= 0.002f || h.y <= 0.002f || h.z <= 0.002f) h = Vector3.Max(h, Vector3.one * 0.002f);
                obstacles.Add(new Box { c = rd.transform.TransformPoint(lb.center), r = rd.transform.rotation, h = h, name = rd.name });
            }
    }

    Box WorldLinkBox(int k, float shrink)
    {
        Box lb = linkLocalBox[k];
        return new Box { c = fkLinkPos[k] + fkLinkRot[k] * lb.c, r = fkLinkRot[k], h = Vector3.Max(lb.h - Vector3.one * shrink, Vector3.one * 0.003f) };
    }

    bool CollisionFree(float[] q)
    {
        if (linkLocalBox == null) return true;
        BuildObstacles();
        ForwardKinematics(q, out _, out _);
        int m = links.Length;
        float table = transform.position.y;
        var boxes = new Box[m];
        for (int k = 0; k < m; k++) boxes[k] = WorldLinkBox(k, 0f);

        // ① 식탁: 밑동 다음 링크부터 손목까지 상자 가장 아래가 식탁 면 아래로 내려가면 안 됨
        //    (마지막 링크 = 손목 끝 + 그리퍼는 컵을 잡으려면 식탁 가까이 내려가야 해서 제외, 장애물 검사는 함)
        for (int k = 1; k < m - 1; k++)
            if (linkLocalBox[k].h != Vector3.zero && MinY(boxes[k]) < table - 0.003f) { lastHit = $"{links[k].name} ↔ 식탁"; return false; }

        // ② 장애물(커피 머신·컵·받침대·트레이)
        foreach (Box ob in obstacles)
        {
            for (int k = 1; k < m; k++)
                if (linkLocalBox[k].h != Vector3.zero && Overlap(boxes[k], ob)) { lastHit = $"{links[k].name} ↔ {ob.name}"; return false; }
            if (heldObject != null && hasHeldBox)
            {
                Box hb = new Box { c = fkLinkPos[m - 1] + fkLinkRot[m - 1] * heldLocalBox.c, r = fkLinkRot[m - 1], h = heldLocalBox.h - Vector3.one * collisionMargin };
                if (Overlap(hb, ob)) { lastHit = $"든 컵 ↔ {ob.name}"; return false; }
            }
        }

        // ③ 로봇 자신: 서로 붙어 있지 않은 링크(번호 차이 3 이상)끼리, 밑동 ↔ 손목·손
        for (int a = 0; a < m; a++)
        {
            if (linkLocalBox[a].h == Vector3.zero) continue;
            Box ba = WorldLinkBox(a, selfShrink);
            for (int bIdx = a + 3; bIdx < m; bIdx++)
            {
                if (linkLocalBox[bIdx].h == Vector3.zero) continue;
                if (Overlap(ba, WorldLinkBox(bIdx, selfShrink))) { lastHit = $"{links[a].name} ↔ {links[bIdx].name}"; return false; }
            }
        }
        if (hasRootBox)
        {
            Box rb = new Box { c = rootLink.position + rootLink.rotation * rootLocalBox.c, r = rootLink.rotation, h = rootLocalBox.h - Vector3.one * selfShrink };
            for (int k = 3; k < m; k++)
                if (linkLocalBox[k].h != Vector3.zero && Overlap(rb, WorldLinkBox(k, selfShrink))) { lastHit = $"밑동 ↔ {links[k].name}"; return false; }
        }
        return true;
    }

    static float MinY(Box b)
    {
        Vector3 ax = b.r * Vector3.right, ay = b.r * Vector3.up, az = b.r * Vector3.forward;
        return b.c.y - (Mathf.Abs(ax.y) * b.h.x + Mathf.Abs(ay.y) * b.h.y + Mathf.Abs(az.y) * b.h.z);
    }

    // 회전된 두 상자가 겹치는지 (분리축 15개 검사)
    static bool Overlap(Box a, Box b)
    {
        Vector3[] A = { a.r * Vector3.right, a.r * Vector3.up, a.r * Vector3.forward };
        Vector3[] B = { b.r * Vector3.right, b.r * Vector3.up, b.r * Vector3.forward };
        float[] ea = { a.h.x, a.h.y, a.h.z }, eb = { b.h.x, b.h.y, b.h.z };
        Vector3 d = b.c - a.c;
        var R = new float[3, 3]; var AR = new float[3, 3];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) { R[i, j] = Vector3.Dot(A[i], B[j]); AR[i, j] = Mathf.Abs(R[i, j]) + 1e-5f; }
        float[] t = { Vector3.Dot(d, A[0]), Vector3.Dot(d, A[1]), Vector3.Dot(d, A[2]) };
        for (int i = 0; i < 3; i++)
            if (Mathf.Abs(t[i]) > ea[i] + eb[0] * AR[i, 0] + eb[1] * AR[i, 1] + eb[2] * AR[i, 2]) return false;
        for (int j = 0; j < 3; j++)
            if (Mathf.Abs(t[0] * R[0, j] + t[1] * R[1, j] + t[2] * R[2, j]) > ea[0] * AR[0, j] + ea[1] * AR[1, j] + ea[2] * AR[2, j] + eb[j]) return false;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                int i1 = (i + 1) % 3, i2 = (i + 2) % 3, j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                float ra = ea[i1] * AR[i2, j] + ea[i2] * AR[i1, j];
                float rb = eb[j1] * AR[i, j2] + eb[j2] * AR[i, j1];
                if (Mathf.Abs(t[i2] * R[i1, j] - t[i1] * R[i2, j]) > ra + rb) return false;
            }
        return true;
    }

    // DLS 반복. 남는 자유도는 seed(지금 자세) 쪽으로 당겨 팔이 크게 뒤집히지 않게 한다.
    bool SolveModel(float[] seed, float[] q, Vector3 pos, Quaternion rot)
    {
        int n = q.Length;
        float limit = jointLimitDeg * Mathf.Deg2Rad;
        var J = new double[6, n];
        var e = new double[6];
        var Jz = new double[6];
        var z = new double[n];
        for (int iter = 0; iter < 300; iter++)
        {
            ForwardKinematics(q, out Vector3 tipPos, out Quaternion tipRot);
            Vector3 ePos = pos - tipPos;
            Vector3 eRot = RotationError(rot, tipRot);
            if (ePos.magnitude <= reachTolerance * 0.5f && eRot.magnitude * Mathf.Rad2Deg <= angleTolerance * 0.5f)
                return true;

            BuildJacobian(J, tipPos, n);
            FillError(e, Vector3.ClampMagnitude(ePos, 0.05f), Vector3.ClampMagnitude(eRot, 0.35f));
            double[,] A = DampedJJt(J, n);
            double[] y = Solve6(A, e);
            if (y == null) return false;

            // 남는 자유도 항: (I - J⁺J) · k(seed - q)
            for (int i = 0; i < n; i++) z[i] = 0.3 * (seed[i] - q[i]);
            for (int r = 0; r < 6; r++) { double s = 0; for (int i = 0; i < n; i++) s += J[r, i] * z[i]; Jz[r] = s; }
            double[] y2 = Solve6(A, Jz);

            for (int i = 0; i < n; i++)
            {
                double dq = z[i];
                for (int r = 0; r < 6; r++) dq += J[r, i] * (y[r] - (y2 != null ? y2[r] : 0));
                q[i] = Mathf.Clamp(q[i] + (float)dq, -limit, limit);
            }
        }
        return false;
    }

    // 팔꿈치가 식탁 아래로 내려가는 "팔꿈치 아래" 자세를 거른다.
    // - 어깨·팔꿈치(1, 2번 관절): 식탁 면 + tableClearance 이상
    // - 첫 손목(3번): 식탁 면 이상
    // - 나머지 손목(4, 5번)은 컵을 옆에서 잡을 때 손끝 높이(식탁 위 5cm)까지 내려와야 하므로 검사하지 않는다
    bool AboveTable(float[] q, float clearance)
    {
        ForwardKinematics(q, out _, out _);
        float table = transform.position.y;
        int last = Mathf.Min(3, joints.Length - 1);
        for (int i = 1; i <= last; i++)
        {
            float floor = table + (i < 3 ? clearance : 0f);
            if (fkPivot[i].y < floor) return false;
        }
        return true;
    }

    // 같은 자세를 뜻하는 각도 중 지금 각도에서 가까운 쪽으로 (한 바퀴 돌지 않게)
    float[] ToGoalDeg(float[] seedRad, float[] qRad)
    {
        var g = new float[qRad.Length];
        for (int i = 0; i < qRad.Length; i++)
        {
            float cur = seedRad[i] * Mathf.Rad2Deg;
            float target = qRad[i] * Mathf.Rad2Deg;
            float near = cur + Mathf.DeltaAngle(cur, target);
            g[i] = Mathf.Abs(near) <= jointLimitDeg ? near : target;
        }
        return g;
    }

    // 관절을 목표 각도까지 함께 움직인다. 가장 많이 도는 관절도 moveSpeed를 넘지 않는다.
    IEnumerator DriveJoints(float[] goalDeg)
    {
        int n = joints.Length;
        var from = new float[n];
        float maxDelta = 0f;
        for (int i = 0; i < n; i++)
        {
            from[i] = ActualDeg(i);
            maxDelta = Mathf.Max(maxDelta, Mathf.Abs(goalDeg[i] - from[i]));
        }
        float duration = Mathf.Max(0.1f, 1.5f * maxDelta / moveSpeed);   // SmoothStep 최고 속도 = 평균의 1.5배
        float t = 0f;
        while (t < duration)
        {
            yield return new WaitForFixedUpdate();
            if (Paused) continue;
            t += Time.fixedDeltaTime;
            float s = Mathf.SmoothStep(0f, 1f, t / duration);
            for (int i = 0; i < n; i++) SetDrive(i, Mathf.Lerp(from[i], goalDeg[i], s));
        }
        for (int i = 0; i < n; i++) SetDrive(i, goalDeg[i]);

        // 드라이브가 따라올 때까지 잠깐 기다림
        float settle = 0f;
        while (settle < 0.5f)
        {
            if (Paused) { yield return new WaitForFixedUpdate(); continue; }
            for (int i = 0; i < n; i++) SetDrive(i, goalDeg[i]);   // 일시정지 뒤 다시 목표로
            bool done = true;
            for (int i = 0; i < n; i++) if (Mathf.Abs(ActualDeg(i) - goalDeg[i]) > 1f) { done = false; break; }
            if (done) break;
            settle += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    // 실제 로봇 자세로 DLS 한 걸음 (도착 후 미세 보정)
    void SolveStepOnRobot(Vector3 pos, Quaternion rot, float dt)
    {
        int n = joints.Length;
        var q = new float[n];
        for (int i = 0; i < n; i++) q[i] = joints[i].jointPosition[0];
        ForwardKinematics(q, out _, out _);          // fkPivot / fkAxis 채우기

        Vector3 tipPos = tip.position;
        var J = new double[6, n];
        var e = new double[6];
        BuildJacobian(J, tipPos, n);
        FillError(e, Vector3.ClampMagnitude(pos - tipPos, 0.05f), Vector3.ClampMagnitude(RotationError(rot, tip.rotation), 0.35f));
        double[] y = Solve6(DampedJJt(J, n), e);
        if (y == null) return;

        float maxStep = moveSpeed * dt;
        for (int i = 0; i < n; i++)
        {
            double dq = 0;
            for (int r = 0; r < 6; r++) dq += J[r, i] * y[r];
            float dDeg = Mathf.Clamp((float)(dq * Mathf.Rad2Deg), -maxStep, maxStep);
            SetDrive(i, Mathf.Clamp(ActualDeg(i) + dDeg, -jointLimitDeg, jointLimitDeg));
        }
    }

    // 자코비안: 관절 i를 돌리면 손끝이 axis × (손끝 - 관절 중심) 방향으로 움직인다
    void BuildJacobian(double[,] J, Vector3 tipPos, int n)
    {
        float w = orientationWeight;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = fkAxis[i];
            Vector3 lin = Vector3.Cross(a, tipPos - fkPivot[i]);
            J[0, i] = lin.x; J[1, i] = lin.y; J[2, i] = lin.z;
            J[3, i] = a.x * w; J[4, i] = a.y * w; J[5, i] = a.z * w;
        }
    }

    void FillError(double[] e, Vector3 ePos, Vector3 eRot)
    {
        float w = orientationWeight;
        e[0] = ePos.x; e[1] = ePos.y; e[2] = ePos.z;
        e[3] = eRot.x * w; e[4] = eRot.y * w; e[5] = eRot.z * w;
    }

    // J Jᵀ + λ²I (특이 자세 근처에서도 튀지 않게 감쇠)
    double[,] DampedJJt(double[,] J, int n)
    {
        var A = new double[6, 6];
        for (int r = 0; r < 6; r++)
            for (int c = 0; c < 6; c++)
            {
                double s = 0;
                for (int k = 0; k < n; k++) s += J[r, k] * J[c, k];
                A[r, c] = s + (r == c ? damping * damping : 0);
            }
        return A;
    }

    bool IsReached(Vector3 pos, Quaternion rot)
        => Vector3.Distance(tip.position, pos) <= reachTolerance && Quaternion.Angle(tip.rotation, rot) <= angleTolerance;

    static Vector3 RotationError(Quaternion target, Quaternion current)
    {
        Quaternion q = target * Quaternion.Inverse(current);
        if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
        q.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle < 0.01f || float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return Vector3.zero;
        if (angle > 180f) angle -= 360f;
        return axis.normalized * (angle * Mathf.Deg2Rad);
    }

    // 6x6 연립방정식 (부분 피벗 가우스 소거)
    static double[] Solve6(double[,] A, double[] b)
    {
        const int N = 6;
        var M = new double[N, N + 1];
        for (int r = 0; r < N; r++)
        {
            for (int c = 0; c < N; c++) M[r, c] = A[r, c];
            M[r, N] = b[r];
        }
        for (int col = 0; col < N; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < N; r++)
                if (System.Math.Abs(M[r, col]) > System.Math.Abs(M[pivot, col])) pivot = r;
            if (System.Math.Abs(M[pivot, col]) < 1e-12) return null;
            if (pivot != col)
                for (int c = col; c <= N; c++) { double tmp = M[col, c]; M[col, c] = M[pivot, c]; M[pivot, c] = tmp; }
            for (int r = col + 1; r < N; r++)
            {
                double f = M[r, col] / M[col, col];
                for (int c = col; c <= N; c++) M[r, c] -= f * M[col, c];
            }
        }
        var x = new double[N];
        for (int r = N - 1; r >= 0; r--)
        {
            double s = M[r, N];
            for (int c = r + 1; c < N; c++) s -= M[r, c] * x[c];
            x[r] = s / M[r, r];
        }
        return x;
    }

    // ── 관절 접근 ──

    Vector3 JointPivotNow(int i)
    {
        Transform parent = FindParentLink(joints[i]);
        return parent.TransformPoint(joints[i].parentAnchorPosition);
    }

    float ActualDeg(int i) => joints[i].jointPosition[0] * Mathf.Rad2Deg;

    void SetDrive(int i, float deg)
    {
        ArticulationDrive d = joints[i].xDrive;
        d.target = deg;
        joints[i].xDrive = d;
    }

    void SetJointImmediate(int i, float deg)
    {
        joints[i].jointPosition = new ArticulationReducedSpace(deg * Mathf.Deg2Rad);
        joints[i].jointVelocity = new ArticulationReducedSpace(0f);
        SetDrive(i, deg);
    }

    void HoldCurrent()
    {
        for (int i = 0; i < joints.Length; i++) SetDrive(i, ActualDeg(i));
    }

    void SnapToHome()
    {
        for (int i = 0; i < joints.Length; i++) SetJointImmediate(i, homeDeg[i]);
    }

    // 씬 뷰에서 로봇팔을 선택하면 팔이 닿는 범위를 원으로 보여 준다 (2.08 배치용)
    void OnDrawGizmosSelected()
    {
        // 밑동 중심에서 수평 reachRadius 원 (식탁 높이)
        Vector3 c = joints != null && joints.Length > 0 ? BasePosition : transform.position;
        c.y = transform.position.y;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
        const int seg = 48;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * reachRadius,
                            c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * reachRadius);
        }
        if (tip != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(tip.position, tip.position + tip.forward * 0.1f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(tip.position, tip.position + tip.up * 0.06f);
        }
    }
}
