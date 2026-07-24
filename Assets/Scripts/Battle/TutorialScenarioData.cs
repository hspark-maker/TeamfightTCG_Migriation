using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼(가이드형 스크립트 전투) 시나리오 — 디자이너 저작 단위(SO).
/// 고정 덱(양측, 순서=등장 순서, 6장 이하 허용) + 스크립트 공격 순서(공격자→타깃).
/// 런타임 단일 진실원은 <see cref="TutorialConfig"/>. 이 SO는 저작 데이터일 뿐.
/// 카드는 CardRegistry.All 의 CardData 참조(선례: CardData/SynergyData/AIDeckConfig).
/// </summary>
[CreateAssetMenu(fileName = "TutorialScenario", menuName = "Card Battle/Tutorial Scenario")]
public class TutorialScenarioData : ScriptableObject
{
    /// <summary>스텝 종류. Attack = 스크립트 공격 1건. Message = 공격 없는 순수 설명(탭으로 진행).
    /// Inspect = 적 카드 롱프레스(정보확인) 대기 후 진행. (SO는 int 직렬화 → 새 값은 반드시 끝에 추가.)</summary>
    public enum StepKind { Attack, Message, Inspect }

    /// <summary>
    /// 튜토리얼 스텝 1개. Attack이면 공격자 슬롯 → 타깃 슬롯(발동 시점 보드 슬롯 0~2).
    /// Message면 공격 없이 안내 문구만 띄우고 탭으로 넘어간다. (이름은 호환 위해 유지.)
    /// </summary>
    [System.Serializable]
    public struct ScriptedAttack
    {
        [Tooltip("Attack = 공격 스텝, Message = 설명 전용(공격 없음, 탭으로 진행)")]
        public StepKind kind;   // 기본값 0 = Attack → 기존 시나리오 호환

        [Tooltip("공격자 슬롯 인덱스 (0~2). Attack 전용")] public int attackerSlot;
        [Tooltip("타깃 슬롯 인덱스 (0~2). Attack 전용")]   public int targetSlot;

        [Tooltip("오버레이에 띄울 안내 문구(비우면 배너 숨김). 순서 = 스텝 순서")]
        [TextArea] public string guideMessage;

        [Tooltip("진행에 화면 탭 필요. Message는 항상 탭 대기. Attack은 공격 전 설명을 탭으로 넘긴 뒤 입력 허용")]
        public bool waitForTap;

        [Tooltip("안내 중 배경 어둡게 + 입력 차단(탭만 허용). 설명 집중용")]
        public bool dimBackground;
    }

    [Header("고정 덱 (순서 = 등장 순서, 셔플 없음, 6장 이하 허용)")]
    public List<CardData> playerDeck;
    public List<CardData> enemyDeck;

    [Header("플레이어 강제 공격 순서 (턴당 1건, 처형 재공격 시 연속 소비)")]
    public List<ScriptedAttack> playerScript;

    [Header("적 강제 공격 순서 (턴당 1건, 처형 재공격 시 연속 소비)")]
    public List<ScriptedAttack> enemyScript;
}
