using System.Collections;
using UnityEngine;

// 중문 자동 개폐 (FR-07, TC-09, TC-10)
// - 중문 감지 영역(Zone, isSensorOnly)에 들어오면 방향과 상관없이 바로 열기 시작, 1초 후 열림
// - 영역을 벗어나면 3초 기다린 뒤 닫기 시작, 1초 후 닫힘 (퇴실 4초 후 완전히 닫힘)
// - 3초 안에 재진입: 연 채로 둠 / 닫히는 중 재진입: 멈추고 다시 열기
public class AutoDoor : MonoBehaviour
{
    [Tooltip("중문 감지 영역 (Zone에서 isSensorOnly 켜기)")]
    [SerializeField] Zone zone;
    [SerializeField] string doorName = "중문";
    [Tooltip("미닫이 문짝")]
    [SerializeField] Transform doorLeaf;
    [Tooltip("양개문이면 반대쪽 문짝 (반대 방향으로 움직임)")]
    [SerializeField] Transform secondLeaf;
    [Tooltip("열렸을 때 문짝이 움직이는 거리 (문짝의 로컬 좌표)")]
    [SerializeField] Vector3 openOffset = new Vector3(0.9f, 0f, 0f);
    [SerializeField] float moveTime = 1f;
    [SerializeField] float closeDelay = 3f;

    Vector3 closedPos, openPos, closedPos2, openPos2;
    float openAmount;          // 0 닫힘 ~ 1 열림
    Coroutine routine;

    void Awake()
    {
        if (doorLeaf == null) doorLeaf = transform;
        closedPos = doorLeaf.localPosition;
        openPos = closedPos + openOffset;
        if (secondLeaf != null)
        {
            closedPos2 = secondLeaf.localPosition;
            openPos2 = closedPos2 - openOffset;
        }
        Apply();
    }

    void OnEnable()
    {
        HomeEvents.OnZoneEntered += OnZoneEntered;
        HomeEvents.OnZoneExited += OnZoneExited;
        HomeEvents.OnScenarioReset += ResetDoor;
    }

    void OnDisable()
    {
        HomeEvents.OnZoneEntered -= OnZoneEntered;
        HomeEvents.OnZoneExited -= OnZoneExited;
        HomeEvents.OnScenarioReset -= ResetDoor;
    }

    void OnZoneEntered(Zone z)
    {
        if (z != zone) return;
        StopRoutine();
        if (openAmount < 1f)
        {
            HomeEvents.Log(doorName, "문 열기 시작");
            routine = StartCoroutine(MoveTo(1f));
        }
    }

    void OnZoneExited(Zone z)
    {
        if (z != zone) return;
        StopRoutine();
        routine = StartCoroutine(CloseAfterDelay());
    }

    IEnumerator CloseAfterDelay()
    {
        yield return new WaitForSeconds(closeDelay);
        if (openAmount > 0f)
        {
            HomeEvents.Log(doorName, "문 닫기 시작");
            yield return MoveTo(0f);
        }
        routine = null;
    }

    IEnumerator MoveTo(float target)
    {
        while (!Mathf.Approximately(openAmount, target))
        {
            openAmount = Mathf.MoveTowards(openAmount, target, Time.deltaTime / moveTime);
            Apply();
            yield return null;
        }
        if (target >= 1f) routine = null;
    }

    void Apply()
    {
        doorLeaf.localPosition = Vector3.Lerp(closedPos, openPos, openAmount);
        if (secondLeaf != null)
            secondLeaf.localPosition = Vector3.Lerp(closedPos2, openPos2, openAmount);
    }

    void StopRoutine()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
    }

    public float OpenAmount => openAmount;

    public void ResetDoor()
    {
        StopRoutine();
        openAmount = 0f;
        Apply();
    }
}
