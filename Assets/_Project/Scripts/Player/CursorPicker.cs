using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// FR-03 부분 클릭 (WBS 3.08)
/// - 커서 위치로 5m 레이캐스트 → IClickable이면 강조
/// - 왼쪽 클릭하면 OnClick()
/// - 커서가 UI(패널·버튼) 위면 강조·클릭 모두 막음
///
/// 붙이는 곳: Main Camera
/// 레이어: 클릭 대상은 "Interactable" 레이어, 구역·게이트 트리거는 "Ignore Raycast"
/// </summary>
[RequireComponent(typeof(Camera))]
public class CursorPicker : MonoBehaviour
{
    [SerializeField] private float maxDistance = 5f;        // SRS FR-03: 5m 이내
    [SerializeField] private LayerMask interactableMask;    // Inspector에서 Interactable 체크

    private Camera cam;
    private IClickable current;   // 지금 강조 중인 대상

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        // 이동(WASD)·시점 회전(우클릭) 중에는 강조하지 않음 → 멈췄을 때만 밝게 표시
        bool moving = IsMoving(mouse);
        IClickable hovered = (moving || IsPointerOverUI()) ? null : Pick(mouse.position.ReadValue());

        // 강조 대상이 바뀌었을 때만 끄고 켬
        if (!ReferenceEquals(hovered, current))
        {
            current?.Highlight(false);
            current = hovered;
            current?.Highlight(true);
        }

        // 오른쪽 버튼으로 시점 돌리는 중엔 왼쪽 클릭 무시
        if (mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.isPressed)
            current?.OnClick();
    }

    private static bool IsMoving(Mouse mouse)
    {
        if (mouse.rightButton.isPressed) return true;
        Keyboard kb = Keyboard.current;
        return kb != null && (kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed);
    }

    private IClickable Pick(Vector2 screenPos)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);

        // Interactable 레이어에만 맞음. 트리거 콜라이더는 무시.
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, interactableMask,
                             QueryTriggerInteraction.Ignore))
            return null;

        // 1) 맞은 오브젝트나 부모에 IClickable이 붙어 있으면 그걸 사용 (로봇팔, 컵, 머신, 소품)
        IClickable direct = hit.collider.GetComponentInParent<IClickable>();
        if (direct != null) return direct;

        // 2) 벽·바닥처럼 집 전체에 흩어진 경우: 렌더러로 OptionPart 찾기
        Renderer r = hit.collider.GetComponentInParent<Renderer>();
        return r != null ? OptionPart.FindByRenderer(r, GetSubMeshIndex(hit)) : null;
    }

    /// <summary>
    /// MeshCollider에 맞았으면 몇 번째 재질 칸(subMesh)인지 계산.
    /// 벽과 바닥이 한 메쉬에 재질만 다르게 들어 있는 경우를 구분하기 위함.
    /// 모르면 -1.
    /// </summary>
    private static int GetSubMeshIndex(RaycastHit hit)
    {
        if (!(hit.collider is MeshCollider mc) || mc.sharedMesh == null || hit.triangleIndex < 0)
            return -1;

        Mesh mesh = mc.sharedMesh;
        int index = hit.triangleIndex * 3;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            var sub = mesh.GetSubMesh(i);   // Read/Write 꺼져 있어도 읽을 수 있는 정보
            if (index >= sub.indexStart && index < sub.indexStart + sub.indexCount)
                return i;
        }
        return -1;
    }

    /// <summary>패널을 열었을 때 등, 강조를 강제로 끄고 싶을 때</summary>
    public void ClearHighlight()
    {
        current?.Highlight(false);
        current = null;
    }

    private static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void OnDisable() => ClearHighlight();
}
