using UnityEngine;

// 구역 정보 (SRS 6장 구역 데이터, FR-02, 부록 A-8)
// Project 창 우클릭 → Create → COM → Zone Data 로 구역마다 하나씩 만든다.
[CreateAssetMenu(menuName = "COM/Zone Data", fileName = "ZoneData")]
public class ZoneData : ScriptableObject
{
    public string zoneId = "entrance";       // entrance, living, kitchen, bedroom
    public string displayName = "현관";
    public float area;                        // ㎡
    public string direction = "";             // 예: 남향
    [TextArea] public string feature = "";
    [Tooltip("부록 A-8 구역 계수: 거실·주방 1.0, 침실 0.6, 현관 0.3")]
    public float luxFactor = 1f;
}
