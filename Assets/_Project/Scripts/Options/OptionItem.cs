using System;
using UnityEngine;

/// <summary>
/// 옵션 하나의 데이터 (SRS 6장 "옵션", 부록 A-7)
/// - useColor가 켜져 있으면 색상만 교체 (벽지: 팀 결정으로 색상만)
/// - material이 있으면 재질 교체 (바닥재, 상판, 소파, 러그)
/// - prefab이 있으면 모델 교체 (그림 액자, 화분)
/// - OptionCatalog(김찬중, WBS 3.09)의 목록에 이 형태로 들어감
/// </summary>
[Serializable]
public class OptionItem
{
    public string optionId;          // 예: "wall_greige"
    public PartType part;
    public string displayName;       // 예: "그레이지 실크 벽지"
    public bool useColor;            // 색상만 바꾸기 (기본 옵션이면 원래 색으로 되돌림)
    public Color color = Color.white;
    public Material material;        // 재질 교체용
    public GameObject prefab;        // 모델 교체용
    public Sprite preview;           // 옵션 패널 미리보기
    public int extraCost;            // 기본 대비 추가 비용(원), 기본은 0
    public bool isDefault;
}
