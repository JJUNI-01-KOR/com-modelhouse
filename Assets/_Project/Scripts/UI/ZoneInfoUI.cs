using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// FR-02 구역 정보 창 (WBS 3.07)
/// - 구역에 들어가면 이름·면적·방향·특징 4개를 화면 위쪽에 3초 표시
/// - 열린 채로 다른 구역에 들어가면 새 구역 정보로 바로 교체 (타이머 다시 3초)
///
/// 붙이는 곳: Canvas 아래 ZoneInfoPanel (CanvasGroup 필요)
/// 호출: Show(zoneData)
///   ZoneVolume.Entered를 구독해서 자동 표시. HomeEvents가 합류하면
///   HomeEvents.OnZoneEntered 구독으로 바꿔도 됨
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ZoneInfoUI : MonoBehaviour
{
    [SerializeField] private Text nameText;
    [SerializeField] private Text areaText;
    [SerializeField] private Text directionText;
    [SerializeField] private Text featureText;
    [SerializeField] private float showTime = 3f;     // SRS: 3초

    private CanvasGroup group;
    private Coroutine hideRoutine;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        SetVisible(false);
    }

    // 구역 박스(ZoneVolume)에 들어가면 자동으로 표시
    private void OnEnable() => ZoneVolume.Entered += HandleZoneEntered;
    private void OnDisable() => ZoneVolume.Entered -= HandleZoneEntered;
    private void HandleZoneEntered(ZoneVolume zone) => Show(zone.Data);

    public void Show(ZoneData data)
    {
        if (data == null) return;

        nameText.text = data.displayName;
        areaText.text = $"면적 {data.area:0.#}㎡";
        directionText.text = data.direction;
        featureText.text = data.feature;

        SetVisible(true);

        // 이미 떠 있으면 타이머만 다시 시작 → 기존 창이 새 구역으로 교체됨
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfter(showTime));
    }

    public void Hide()
    {
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = null;
        SetVisible(false);
    }

    private IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        hideRoutine = null;
        SetVisible(false);
    }

    private void SetVisible(bool on)
    {
        group.alpha = on ? 1f : 0f;
        group.blocksRaycasts = false;  // 정보 창은 클릭을 막지 않음
        group.interactable = false;
    }
}
