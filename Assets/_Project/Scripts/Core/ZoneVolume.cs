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
///   → HomeEvents.OnZoneEntered / OnZoneExited로도 보냄 (ZoneLight·AutoDoor가 구독)
///   → 이벤트 기록(FR-25): "방 진입" / "방 퇴실"
///
/// isSensorOnly: 중문 감지 영역처럼 "감지만" 하는 박스 (ZoneData 없어도 됨)
///   → 구역 정보 창(Entered/Exited)은 안 보내고 HomeEvents만 보냄, 기록은 "감지 영역 진입/퇴실"
/// 초기화(FR-26): HomeEvents.OnScenarioReset을 받아 재실 상태를 비움 → 다음 물리 프레임에
///   시점이 들어 있는 구역(현관)이 다시 입실로 판정된다 (FR-26 9번)
/// </summary>
public class ZoneVolume : MonoBehaviour
{
    [SerializeField] private ZoneData data;
    [Tooltip("중문 감지 영역이면 켠다 (구역 정보 창·조명 대상 아님)")]
    [SerializeField] private bool isSensorOnly;

    public static event Action<ZoneVolume> Entered;
    public static event Action<ZoneVolume> Exited;

    public ZoneData Data => data;
    public bool IsSensorOnly => isSensorOnly;
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
        if (data == null && !isSensorOnly) Debug.LogWarning($"[ZoneVolume] {name}: ZoneData가 비어 있음", this);
    }

    private void OnEnable() => HomeEvents.OnScenarioReset += ResetZone;
    private void OnDisable() => HomeEvents.OnScenarioReset -= ResetZone;

    internal void BoxEnter(Collider box)
    {
        bool wasEmpty = insideBoxes.Count == 0;
        insideBoxes.Add(box);
        if (wasEmpty)
        {
            // 센서 감지 기록을 먼저 남기고 사건을 보낸다 → 기기 동작 기록이 뒤에 온다 (FR-25 2번)
            HomeEvents.Log(DisplayName, isSensorOnly ? "감지 영역 진입" : "방 진입");
            if (!isSensorOnly) Entered?.Invoke(this);
            HomeEvents.RaiseZoneEntered(this);
        }
    }

    internal void BoxExit(Collider box)
    {
        if (!insideBoxes.Remove(box)) return;
        if (insideBoxes.Count == 0)
        {
            HomeEvents.Log(DisplayName, isSensorOnly ? "감지 영역 퇴실" : "방 퇴실");
            if (!isSensorOnly) Exited?.Invoke(this);
            HomeEvents.RaiseZoneExited(this);
        }
    }

    /// <summary>초기화(FR-26)·순간이동 때 상태 비우기. 퇴실 사건은 보내지 않는다 (조명·문은 각자 초기화).
    /// ZoneVolumeBox.OnTriggerStay가 다음 물리 프레임에 다시 입실을 알려 줌.</summary>
    public void ResetZone()
    {
        insideBoxes.Clear();
    }

    public string DisplayName => data != null ? data.displayName : name;
}
