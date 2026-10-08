using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 로봇팔 상태 (SRS 6장 상태 전이)
public enum ArmState { Idle, Picking, Carrying, WaitingBrew, Placing, Failed }

public static class ArmStateExtensions
{
    // 상호작용 창(FR-34)과 이벤트 기록에 쓰는 이름
    public static string ToKorean(this ArmState state)
    {
        switch (state)
        {
            case ArmState.Idle: return "대기";
            case ArmState.Picking: return "집는 중";
            case ArmState.Carrying: return "옮기는 중";
            case ArmState.WaitingBrew: return "커피 받는 중";
            case ArmState.Placing: return "놓는 중";
            case ArmState.Failed: return "작업 실패";
            default: return state.ToString();
        }
    }
}

// ArticulationBody 6축 로봇팔 + IK (FR-15, TC-18)
// 1) 시작할 때 관절 구조를 읽어 계산용 모델을 만든다.
// 2) MoveTo: 모델에서 감쇠 최소제곱(DLS) IK로 목표 관절 각도를 먼저 구한다. 못 구하면 바로 실패(팔이 안 닿음).
// 3) 관절마다 최대 회전 속도(moveSpeed)를 넘지 않게 목표 각도까지 움직인 뒤, 실제 자세로 미세 보정한다.
// 4) 손끝이 목표에서 2cm 이내면 도착. 5초(ikTimeout) 안에 못 닿으면 실패.
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
    [Tooltip("테스트용: 지정하면 대기 중에 손끝이 이 오브젝트를 따라간다")]
    [SerializeField] Transform ikTarget;
    [Tooltip("홈 자세 관절 각도(도), Base부터 순서대로. 비우면 시작 자세가 홈")]
    [SerializeField] float[] homeAnglesDeg = new float[0];

    [Header("IK (FR-15)")]
    [SerializeField] float reachTolerance = 0.02f;
    [SerializeField] float angleTolerance = 10f;
    [SerializeField] float ikTimeout = 5f;
    [Tooltip("어깨에서 손끝까지 닿는 거리 (m). UR3는 팔 0.5 + 그리퍼 길이")]
    [SerializeField] float maxReach = 0.6f;
    [Tooltip("관절 최대 회전 속도 (도/초)")]
    [SerializeField] float moveSpeed = 90f;
    [SerializeField] float orientationWeight = 0.2f;
    [SerializeField] float damping = 0.02f;
    [SerializeField] float jointLimitDeg = 270f;

    [Header("충돌")]
    [Tooltip("로봇팔이 부딪히지 않고 지나갈 레이어 (식탁·커피 머신이 있는 레이어)")]
    [SerializeField] LayerMask passThroughLayers = 1;

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

    ArmState state = ArmState.Idle;
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
        SnapToHome();
        ready = true;
    }

    void OnEnable() => HomeEvents.OnScenarioReset += ResetArm;
    void OnDisable() => HomeEvents.OnScenarioReset -= ResetArm;

    void FixedUpdate()
    {
        if (ready && ikTarget != null && !IsMoving && state == ArmState.Idle)
            SolveStepOnRobot(ikTarget.position, ikTarget.rotation, Time.fixedDeltaTime);
    }

    void LateUpdate()
    {
        if (heldObject != null)
            heldObject.SetPositionAndRotation(tip.TransformPoint(heldOffsetPos), tip.rotation * heldOffsetRot);
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
        Vector3 shoulder = joints.Length > 1 ? JointPivotNow(1) : BasePosition;
        return Vector3.Distance(shoulder, pos) <= maxReach;
    }

    // 이동을 시작한다. 팔이 닿지 않으면 false. 끝났는지는 IsMoving / LastMoveSucceeded로 본다.
    public bool MoveTo(Vector3 pos, Quaternion rot)
    {
        if (!CanReach(pos)) { LastMoveSucceeded = false; return false; }
        StartCoroutine(MoveToRoutine(pos, rot));
        return true;
    }

    // 작업 코드에서는 yield return arm.MoveToRoutine(...) 후 LastMoveSucceeded를 확인한다.
    public IEnumerator MoveToRoutine(Vector3 pos, Quaternion rot)
    {
        LastMoveSucceeded = false;
        while (!ready) yield return null;
        if (!CanReach(pos)) yield break;

        IsMoving = true;
        float start = Time.time;

        float[] goalDeg;
        if (!PlanIK(pos, rot, out goalDeg)) { IsMoving = false; yield break; }   // 해가 없음 = 닿지 못함

        yield return DriveJoints(goalDeg);

        // 실제 물리 자세 기준으로 미세 보정
        while (true)
        {
            if (IsReached(pos, rot)) { LastMoveSucceeded = true; break; }
            if (Time.time - start > ikTimeout) break;
            yield return new WaitForFixedUpdate();
            SolveStepOnRobot(pos, rot, Time.fixedDeltaTime);
        }
        HoldCurrent();
        IsMoving = false;
    }

    public void GoHome() => StartCoroutine(GoHomeRoutine());

    public IEnumerator GoHomeRoutine()
    {
        while (!ready) yield return null;
        IsMoving = true;
        yield return DriveJoints(homeDeg);
        IsMoving = false;
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

    public void SetState(ArmState s)
    {
        if (state == s) return;
        state = s;
        HomeEvents.Log(displayName, "로봇팔 상태 변경: " + s.ToKorean());
        HomeEvents.RaiseArmStateChanged(this, s);
    }

    public ArmState GetState() => state;

    // 초기화(FR-26 3번): 기본 자세와 대기 상태로 즉시 되돌린다.
    // 상호작용 창이 "완료"를 띄우지 않게 상태 사건은 보내지 않는다.
    public void ResetArm()
    {
        if (!ready) return;                 // 시작 직후 축 확인 중에는 건너뜀
        StopAllCoroutines();
        IsMoving = false;
        Release();
        SnapToHome();
        if (gripper != null)
        {
            gripper.gripState = GripState.Fixed;
            gripper.grip = 0f;              // 손가락은 다음 물리 프레임에 열린다
        }
        state = ArmState.Idle;
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
        if (SolveModel(seed, q, pos, rot)) { goalDeg = ToGoalDeg(seed, q); return true; }

        var rng = new System.Random(7);
        float bestDelta = float.MaxValue;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            for (int i = 0; i < n; i++) q[i] = seed[i] + (float)(rng.NextDouble() * 2 - 1) * Mathf.PI * 0.5f;
            if (!SolveModel(seed, q, pos, rot)) continue;
            float[] g = ToGoalDeg(seed, q);
            float delta = 0f;
            for (int i = 0; i < n; i++) delta = Mathf.Max(delta, Mathf.Abs(g[i] - seed[i] * Mathf.Rad2Deg));
            if (delta < bestDelta) { bestDelta = delta; goalDeg = g; }
        }
        return goalDeg != null;
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
            t += Time.fixedDeltaTime;
            float s = Mathf.SmoothStep(0f, 1f, t / duration);
            for (int i = 0; i < n; i++) SetDrive(i, Mathf.Lerp(from[i], goalDeg[i], s));
        }
        for (int i = 0; i < n; i++) SetDrive(i, goalDeg[i]);

        // 드라이브가 따라올 때까지 잠깐 기다림
        float settle = 0f;
        while (settle < 0.5f)
        {
            bool done = true;
            for (int i = 0; i < n; i++) if (Mathf.Abs(ActualDeg(i) - goalDeg[i]) > 1f) { done = false; break; }
            if (done) break;
            settle += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    // 실제 로봇 자세로 DLS 한 걸음 (미세 보정, ikTarget 따라가기)
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

    // 씬 뷰에서 로봇팔을 선택하면 팔이 닿는 범위를 구로 보여 준다 (2.08 배치용)
    void OnDrawGizmosSelected()
    {
        ArticulationBody[] abs = GetComponentsInChildren<ArticulationBody>();
        Vector3 center = abs.Length > 2 ? abs[2].transform.position : transform.position;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(center, maxReach);
        if (tip != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(tip.position, tip.position + tip.forward * 0.1f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(tip.position, tip.position + tip.up * 0.06f);
        }
    }
}
