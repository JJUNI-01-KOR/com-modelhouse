using UnityEngine;

/// <summary>
/// 구역 고정 정보 (FR-02, SRS 6장 "구역", 부록 A-8)
/// 만드는 법: Project 창 우클릭 → Create → COM → Zone Data
/// 저장 위치: Assets/_Project/Data/ZoneData_Entrance.asset 등 4개
/// </summary>
[CreateAssetMenu(fileName = "ZoneData_", menuName = "COM/Zone Data")]
public class ZoneData : ScriptableObject
{
    public string zoneId;          // "entrance", "living", "kitchen", "bedroom" (중복 금지)
    public string displayName;     // 현관, 거실, 주방, 침실
    public float area;             // ㎡
    public string direction;       // 남향 등
    [TextArea] public string feature;
    [Tooltip("거실·주방 1.0, 침실 0.6, 현관 0.3 (부록 A-8)")]
    public float luxFactor = 1f;
}
