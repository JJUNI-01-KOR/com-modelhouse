/// <summary>
/// 옵션 파트 7종 (FR-29-01 ~ 07, SRS 부록 A-7)
/// </summary>
public enum PartType
{
    Wall,        // 벽지       m_walls
    Floor,       // 바닥재     m_floor
    Countertop,  // 주방 상판  m_countertop
    SofaFabric,  // 소파 패브릭 m_white_fabric
    Rug,         // 러그       m_carpet
    Painting,    // 그림 액자  sm_painting 1~3 (모델 교체)
    Plant        // 화분       sm_strelitzia, sm_plant_fern, sm_broadleaf (모델 교체)
}
