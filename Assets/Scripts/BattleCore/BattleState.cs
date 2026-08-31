using System;

namespace TeamfightTCG.BattleCore
{
    public struct BattleCardState
    {
        public int CardId;
        public int OwnerIndex;
        public int SlotIndex;
        public int BaseMaxHp;
        public int Hp;
        public int MaxHp;
        public int BonusHp;
        public int AttackCount;
        public int FlowBonus;
        public int LegacyStack;
        public int SynergyDamageReduction;
        public int EvolutionStage;
        public global::CardKeyword UnlockedKeywords;
        public global::CardKeyword RuntimeKeywords;
        public global::CardKeyword SynergyKeywords;
        public bool SynergyEnabled;
        public bool ReviveUsed;
        public bool HasShield;
        public bool ReturnedFromField;
        public bool JustSpawned;
        public bool IsRevealed;
        public bool WasEverRevealed;
        public bool CinematicShown;
        public bool CinemaAttackUsed;
    }

    /// <summary>Unity 오브젝트와 연출 참조가 없는 서버 재시뮬 상태 봉투.</summary>
    public sealed class BattleState
    {
        public ulong Seed;
        public int Turn = 1;
        public int FirstOwner;
        /// <summary>이 스냅샷 시점까지의 공용 스트림 소비 횟수. 대조용 값이지 난수원이 아니다.
        ///
        /// <para>스트림 <b>사본</b>을 들고 있지 않는 이유: <see cref="DeterministicRandom"/>은 가변 구조체라
        /// 사본을 받은 쪽이 독립적으로 전진시킬 수 있고, 그렇게 생긴 두 번째 난수원은
        /// DrawCount 카나리아가 잡지 못한다. 재시뮬은 <see cref="Seed"/>부터 다시 돌리므로
        /// 중간 스트림 상태가 애초에 필요 없다.</para></summary>
        public int RandomDrawCount;
        /// <summary>한 진영의 필드 슬롯 수. <b>이 값이 진실원이다</b> —
        /// Unity 쪽 <c>BattleField.SLOT_COUNT</c>가 여기를 참조한다. 코어는 게임 어셈블리를
        /// 참조할 수 없으므로 방향이 반대면 두 상수가 조용히 어긋난다.</summary>
        public const int SlotCount = 3;

        /// <summary>진영 수(0=선공 owner, 1=후공 owner).</summary>
        public const int SideCount = 2;

        public BattleCardState?[][] Slots =
        {
            new BattleCardState?[SlotCount],
            new BattleCardState?[SlotCount],
        };
        public BattleCardState[][] Waiting =
        {
            Array.Empty<BattleCardState>(),
            Array.Empty<BattleCardState>(),
        };
        public int[][] FallenCardIds =
        {
            Array.Empty<int>(),
            Array.Empty<int>(),
        };
        public int[] FlowStacks = new int[2];
    }
}
