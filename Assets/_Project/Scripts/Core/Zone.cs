using System.Collections.Generic;
using UnityEngine;

// 구역 영역 기반 재실 센서 (FR-04, FR-05, FR-07, TC-54)
// - 구역 전체를 덮는 BoxCollider(트리거) 안에 체험자 시점 위치가 있으면 입실로 본다.
// - 트리거 이벤트 대신 매 프레임 시점 위치를 검사한다. 그래서
//   · 두 구역 경계에 서 있어도 시점 위치가 든 구역만 입실이 된다 (FR-04 예외).
//   · 시작·초기화 직후에도 현관 입실이 다시 판정된다 (FR-26 9번).
// - isSensorOnly를 켜면 중문 감지 영역처럼 감지만 하고, 정보 창·조명은 반응하지 않는다.
[RequireComponent(typeof(BoxCollider))]
public class Zone : MonoBehaviour
{
    [SerializeField] ZoneData data;
    [SerializeField] bool isStartZone;
    [Tooltip("중문 감지 영역이면 켠다 (구역 정보·조명 대상 아님)")]
    [SerializeField] bool isSensorOnly;
    [Tooltip("체험자 시점. 비우면 Main Camera를 쓴다")]
    [SerializeField] Transform viewpoint;

    BoxCollider roomTrigger;
    bool isOccupied;

    public static readonly List<Zone> All = new List<Zone>();

    public bool IsSensorOnly => isSensorOnly;
    public bool IsStartZone => isStartZone;
    public string DisplayName => data != null ? data.displayName : gameObject.name;

    void Awake()
    {
        roomTrigger = GetComponent<BoxCollider>();
        roomTrigger.isTrigger = true;
    }

    void OnEnable()
    {
        All.Add(this);
        HomeEvents.OnScenarioReset += ResetZone;
    }

    void OnDisable()
    {
        All.Remove(this);
        HomeEvents.OnScenarioReset -= ResetZone;
    }

    void Update()
    {
        if (viewpoint == null)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            viewpoint = cam.transform;
        }

        bool inside = Contains(viewpoint.position);
        if (inside != isOccupied) SetOccupied(inside);
    }

    // 점이 구역 상자 안에 있는가 (회전·크기 반영)
    public bool Contains(Vector3 worldPoint)
    {
        Vector3 p = transform.InverseTransformPoint(worldPoint) - roomTrigger.center;
        Vector3 h = roomTrigger.size * 0.5f;
        return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
    }

    public void SetOccupied(bool value)
    {
        if (isOccupied == value) return;
        isOccupied = value;

        // 센서 감지 기록을 먼저 남기고 사건을 보낸다 → 기기 동작 기록이 뒤에 온다 (NFR-02 측정용)
        if (value)
        {
            HomeEvents.Log(DisplayName, isSensorOnly ? "감지 영역 진입" : "방 진입");
            HomeEvents.RaiseZoneEntered(this);
        }
        else
        {
            HomeEvents.Log(DisplayName, isSensorOnly ? "감지 영역 퇴실" : "방 퇴실");
            HomeEvents.RaiseZoneExited(this);
        }
    }

    public bool IsOccupied() => isOccupied;
    public ZoneData GetData() => data;

    // 초기화: 퇴실 사건 없이 재실만 지운다. 조명·문은 각자 초기화되고,
    // 다음 프레임 검사에서 시점이 있는 구역(보통 현관)이 다시 입실된다.
    public void ResetZone()
    {
        isOccupied = false;
    }

    void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = isSensorOnly ? new Color(1f, 0.6f, 0f, 0.15f) : new Color(0.2f, 0.6f, 1f, 0.12f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = isSensorOnly ? new Color(1f, 0.6f, 0f, 0.8f) : new Color(0.2f, 0.6f, 1f, 0.8f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
