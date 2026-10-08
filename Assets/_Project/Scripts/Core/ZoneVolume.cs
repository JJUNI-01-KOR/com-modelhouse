using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 구역 하나 = 박스 여러 개 (FR-02 / WBS 2.05, 3.07)
/// 꺾인 거실처럼 박스가 2개 이상이어도 "박스 중 하나라도 안에 있으면 재실".
/// 박스 사이를 오갈 때는 퇴실/입실이 찍히지 않는다.
///
/// 구조:
///   Zone_Living   (이 컴포넌트 + ZoneData, 콜라이더 없음)
///    ├ Box_Main   (BoxCollider, Is Trigger, Ignore Raycast)
///    └ Box_Corner (BoxCollider, Is Trigger, Ignore Raycast)
/// 자식 박스에는 Play 시작 때 ZoneVolumeBox가 자동으로 붙는다.
///
/// 이벤트: ZoneVolume.Entered / Exited (static)
///   → ZoneInfoUI가 구독해서 정보 창 표시
///   → 김찬중 Zone/HomeEvents와 합칠 때 여기서 HomeEvents.OnZoneEntered로 넘기면 됨
/// </summary>
public class ZoneVolume : MonoBehaviour
{
    [SerializeField] private ZoneData data;

    public static event Action<ZoneVolume> Entered;
    public static event Action<ZoneVolume> Exited;

    public ZoneData Data => data;
    public bool IsOccupied => insideBoxes.Count > 0;

    // 플레이어가 지금 들어가 있는 박스들
    private readonly HashSet<Collider> insideBoxes = new HashSet<Collider>();

    private void Awake()
    {
        int boxCount = 0;
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (!c.isTrigger)
            {
                Debug.LogWarning($"[ZoneVolume] {name}/{c.name}: Is Trigger가 꺼져 있음 → 플레이어를 막는 벽이 됨", c);
                continue;
            }
            ZoneVolumeBox box = c.GetComponent<ZoneVolumeBox>();
            if (box == null) box = c.gameObject.AddComponent<ZoneVolumeBox>();
            box.Init(this, c);
            boxCount++;
        }

        if (boxCount == 0) Debug.LogWarning($"[ZoneVolume] {name}: 자식에 트리거 박스가 없음", this);
        if (data == null) Debug.LogWarning($"[ZoneVolume] {name}: ZoneData가 비어 있음", this);
    }

    internal void BoxEnter(Collider box)
    {
        bool wasEmpty = insideBoxes.Count == 0;
        insideBoxes.Add(box);
        if (wasEmpty)
        {
            Debug.Log($"[ZoneVolume] 입실: {DisplayName}");
            Entered?.Invoke(this);
        }
    }

    internal void BoxExit(Collider box)
    {
        if (!insideBoxes.Remove(box)) return;
        if (insideBoxes.Count == 0)
        {
            Debug.Log($"[ZoneVolume] 퇴실: {DisplayName}");
            Exited?.Invoke(this);
        }
    }

    /// <summary>초기화(FR-26)·순간이동 때 상태 비우기. 다시 겹치면 트리거가 다시 입실을 알려 줌.</summary>
    public void ResetZone()
    {
        insideBoxes.Clear();
    }

    private string DisplayName => data != null ? data.displayName : name;
}
