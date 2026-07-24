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
    /// <summary>스크립트 공격 1건: 공격자 슬롯 → 타깃 슬롯. 슬롯은 발동 시점의 보드 슬롯(0~2).</summary>
    [System.Serializable]
    public struct ScriptedAttack
    {
        [Tooltip("공격자 슬롯 인덱스 (0~2)")] public int attackerSlot;
        [Tooltip("타깃 슬롯 인덱스 (0~2)")]   public int targetSlot;
    }

    [Header("고정 덱 (순서 = 등장 순서, 셔플 없음, 6장 이하 허용)")]
    public List<CardData> playerDeck;
    public List<CardData> enemyDeck;

    [Header("플레이어 강제 공격 순서 (턴당 1건, 처형 재공격 시 연속 소비)")]
    public List<ScriptedAttack> playerScript;

    [Header("적 강제 공격 순서 (턴당 1건, 처형 재공격 시 연속 소비)")]
    public List<ScriptedAttack> enemyScript;
}
