using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// FR-01 1인칭 이동 (WBS 3.01 ~ 3.03)
/// - W A S D: 바라보는 방향 기준 이동
/// - 마우스 오른쪽 버튼을 누른 동안에만 시점 회전 (커서는 항상 보임)
/// - 패널(UI) 위에서 오른쪽 버튼을 누르면 회전하지 않음
/// - 바닥 아래로 떨어지면 현관 시작 위치로 복귀
///
/// 붙이는 곳: Player 오브젝트 (발바닥 위치가 pivot)
///   Player (CharacterController, PlayerController)
///    └ Main Camera (localPosition y = 1.6)
///
/// 주의: 프로젝트가 Input System 전용(activeInputHandler: 1)이라
///       옛날 Input.GetKey는 쓰면 에러가 난다. Keyboard.current / Mouse.current 사용.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform cameraPivot;      // 자식 Main Camera
    [SerializeField] private Transform spawnPoint;       // 현관 시작 위치 (빈 오브젝트)

    [Header("이동")]
    [SerializeField] private float moveSpeed = 2.5f;     // m/s
    [SerializeField] private float gravity = -9.81f;

    [Header("시점")]
    [SerializeField] private float eyeHeight = 1.6f;     // SRS: 바닥에서 1.6m
    [SerializeField] private float lookSensitivity = 0.15f;
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    [Header("낙하 복귀")]
    [SerializeField] private float fallLimitY = -5f;

    private CharacterController controller;
    private float pitch;            // 위아래 각도 (카메라)
    private float verticalVelocity; // 중력 누적
    private bool isLooking;         // 지금 오른쪽 버튼으로 회전 중인가

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        // 캡슐 크기: 키 1.8m, 바닥이 pivot이 되도록 center를 올린다
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.9f, 0f);

        if (cameraPivot != null)
            cameraPivot.localPosition = new Vector3(0f, eyeHeight, 0f);
    }

    // FR-26 6번: 초기화 사건을 받아 현관 시작 위치로 (ScenarioManager는 플레이어를 직접 참조하지 않음)
    private void OnEnable() => HomeEvents.OnScenarioReset += ResetPlayer;
    private void OnDisable() => HomeEvents.OnScenarioReset -= ResetPlayer;

    private void Start()
    {
        // 커서는 항상 보이고 잠그지 않음 (왼쪽 클릭은 버튼·옵션 선택용)
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (spawnPoint != null) ResetPlayer();
    }

    private void Update()
    {
        HandleLook();
        HandleMove();
        CheckFall();
    }

    // ---------------- 시점 ----------------
    private void HandleLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || cameraPivot == null) return;

        // 누르는 "순간"에 UI 위인지 판단 → UI 위에서 시작했으면 회전 안 함
        if (mouse.rightButton.wasPressedThisFrame)
            isLooking = !IsPointerOverUI();

        if (mouse.rightButton.wasReleasedThisFrame)
            isLooking = false;

        if (!isLooking) return;

        Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;

        // 좌우 = 몸 전체 회전, 위아래 = 카메라만 회전
        transform.Rotate(0f, delta.x, 0f);
        pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
        cameraPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    // ---------------- 이동 ----------------
    private void HandleMove()
    {
        Keyboard kb = Keyboard.current;
        Vector2 input = Vector2.zero;
        if (kb != null)
        {
            if (kb.wKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed) input.x -= 1f;
        }
        input = Vector2.ClampMagnitude(input, 1f); // 대각선이 더 빠르지 않게

        // 바라보는 방향 기준 (몸통 forward/right, 높이 성분 없음)
        Vector3 move = (transform.forward * input.y + transform.right * input.x) * moveSpeed;

        // 중력: 땅에 붙어 있으면 살짝 눌러 둠
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        // CharacterController.Move가 벽·가구 충돌을 알아서 막아 줌
        controller.Move(move * Time.deltaTime);
    }

    // ---------------- 낙하 복귀 ----------------
    private void CheckFall()
    {
        if (transform.position.y < fallLimitY)
            ResetPlayer();
    }

    /// <summary>
    /// 현관 시작 위치로 이동. FR-26 초기화(HomeEvents.OnScenarioReset)에서도 호출.
    /// </summary>
    public void ResetPlayer()
    {
        if (spawnPoint == null)
        {
            Debug.LogWarning("[PlayerController] spawnPoint가 비어 있음");
            return;
        }

        // CharacterController가 켜져 있으면 위치를 되돌려 버리므로 껐다 켠다
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPoint.position,
            Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f));
        controller.enabled = true;

        pitch = 0f;
        verticalVelocity = 0f;
        isLooking = false;
        if (cameraPivot != null) cameraPivot.localEulerAngles = Vector3.zero;
    }

    private static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
