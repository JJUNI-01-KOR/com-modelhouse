using UnityEngine;

/// <summary>
/// 자식 박스의 트리거 신호를 부모 ZoneVolume으로 전달 (자동으로 붙음, 직접 안 붙여도 됨)
/// 트리거 신호는 콜라이더가 붙은 오브젝트에만 오기 때문에 필요한 중계기.
///
/// 재실 판정은 "체험자 시점(카메라) 위치가 박스 안에 있는가"로 한다 (SRS 2.5, FR-04 예외).
///   - 캡슐(반지름 0.3m)만 걸쳐 있는 경계에서는 두 구역이 동시에 입실되지 않는다.
///   - OnTriggerStay로 계속 확인하므로 초기화(ResetZone) 뒤에도 다시 입실이 판정된다 (FR-26 9번).
/// </summary>
public class ZoneVolumeBox : MonoBehaviour
{
    private ZoneVolume owner;
    private Collider box;

    public void Init(ZoneVolume zone, Collider col)
    {
        owner = zone;
        box = col;
    }

    private void OnTriggerEnter(Collider other) => Check(other);
    private void OnTriggerStay(Collider other) => Check(other);

    private void OnTriggerExit(Collider other)
    {
        if (owner != null && IsPlayer(other)) owner.BoxExit(box);
    }

    private void Check(Collider other)
    {
        if (owner == null || !IsPlayer(other)) return;
        if (Contains(Viewpoint(other.transform))) owner.BoxEnter(box);
        else owner.BoxExit(box);
    }

    // 박스 안의 점이면 ClosestPoint가 그 점을 그대로 돌려준다 (회전·크기 반영)
    private bool Contains(Vector3 point) => (box.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;

    // 플레이어 자식 카메라 = 시점. 없으면 발 위치 + 1.6m
    private static Vector3 Viewpoint(Transform player)
    {
        Camera cam = player.GetComponentInChildren<Camera>();
        return cam != null ? cam.transform.position : player.position + Vector3.up * 1.6f;
    }

    // CharacterController도 Collider라서 트리거에 걸림
    private static bool IsPlayer(Collider other) => other.GetComponent<PlayerController>() != null;
}
