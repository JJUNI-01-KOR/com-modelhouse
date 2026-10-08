using UnityEngine;

/// <summary>
/// 자식 박스의 트리거 신호를 부모 ZoneVolume으로 전달 (자동으로 붙음, 직접 안 붙여도 됨)
/// 트리거 신호는 콜라이더가 붙은 오브젝트에만 오기 때문에 필요한 중계기.
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

    private void OnTriggerEnter(Collider other)
    {
        if (owner != null && IsPlayer(other)) owner.BoxEnter(box);
    }

    private void OnTriggerExit(Collider other)
    {
        if (owner != null && IsPlayer(other)) owner.BoxExit(box);
    }

    // CharacterController도 Collider라서 트리거에 걸림
    private static bool IsPlayer(Collider other) => other.GetComponent<PlayerController>() != null;
}
