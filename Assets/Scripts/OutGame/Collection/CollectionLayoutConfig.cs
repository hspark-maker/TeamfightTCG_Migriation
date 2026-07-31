using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도감 배치 데이터 
/// </summary>
[CreateAssetMenu(fileName = "CollectionLayoutConfig", menuName = "Card Battle/Collection Layout Config")]
public class CollectionLayoutConfig : ScriptableObject
{
    [Header("전역 기본 튜닝 (행 값이 0이면 이 값 사용)")]
    [SerializeField] ECurrencyType defaultRewardType = ECurrencyType.Gold; // 기본 보상 종류. 행 rewardType 미설정(=Gold)이면 이 값과 동일.
    [Min(0f)] [SerializeField] float defaultProductionCycleSeconds = 15f; // 기본 생산 사이클 시간(초). 한 사이클마다 재화 1단위. 기본 15초. 행 productionCycleSeconds가 0일 때 적용. 음수 오설정 방지.
    [Min(0)] [SerializeField] long defaultCap = 240;                 // 기본 누적 상한(수확 전 최대 누적량). 행 cap이 0일 때 적용. 음수 오설정 방지.
    [Min(1)] [SerializeField] int defaultCardsPerRow = 3;            // 행이 authoring되지 않았을 때 카탈로그를 몇 장씩 끊어 행으로 만들지. authoring된 행은 각자 카드 수를 따른다.

    // ── 행 목록 (순서 = 도감 표시 순서) ──
    [Header("도감 행 목록 (순서 = 위→아래). 행마다 카드 수는 자유 — 행 프리팹이 개수에 맞춰 배치한다.")]
    [SerializeField] List<CollectionRowDef> rows = new List<CollectionRowDef>();

    // 전역 기본값 읽기전용 노출.
    public ECurrencyType DefaultRewardType => defaultRewardType;
    public float DefaultProductionCycleSeconds => defaultProductionCycleSeconds;
    public long DefaultCap => defaultCap;
    public int DefaultCardsPerRow => Mathf.Max(1, defaultCardsPerRow); // 0 저장된 구 에셋이 무한 루프를 만들지 않도록 하한 보정.

    // 행 정의 총 개수. null 방어.
    public int RowDefCount => rows != null ? rows.Count : 0;

    // 읽기 전용 행 목록. null이면 빈 목록(미authoring 상태 안전 처리).
    public IReadOnlyList<CollectionRowDef> Rows
        => rows != null ? rows : (IReadOnlyList<CollectionRowDef>)System.Array.Empty<CollectionRowDef>();
}

/// <summary>
/// 도감 행 하나의 authoring 데이터(인스펙터 입력용)
/// </summary>
[System.Serializable]
public struct CollectionRowDef
{
    [Header("행 카드 (순서 = 좌→우, 개수 자유)")]
    public List<CardData> cards;

    [Header("생산 튜닝 (0 = 전역 기본값 사용)")]
    [Tooltip("수확 시 지급할 재화 종류. 이 행에서는 이 값이 그대로 정본(Gold=0 센티널 부재라 오버라이드 신호 없음).")]
    public ECurrencyType rewardType;
    [Min(0f)] [Tooltip("완성 행의 생산 사이클 시간(초). 0이면 전역 기본. >0이면 이 값으로 오버라이드.")]
    public float productionCycleSeconds;
    [Min(0)] [Tooltip("수확 전 누적 상한(방치 상한). 0이면 전역 기본 상한. >0이면 이 값으로 오버라이드.")]
    public long cap;
}