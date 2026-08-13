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

    /// <summary>안내 배너가 붙을 자리. 포커스가 아래(아군)로 내려가면 배너는 위로 비켜야 가려지지 않는다.
    /// 값 순서 = 화면 위→아래 순서다(인스펙터 드롭다운이 그대로 위아래로 읽힌다).
    /// 실제 좌표는 <c>TutorialOverlayUI</c>의 자리별 Vector2가 정한다 — 여기선 어느 자리인지만 고른다.
    ///
    /// ⚠ SO는 int 직렬화다. <b>2026-08-12에 한 번 재배열했다</b>(구 Center 1→2, 구 Bottom 2→4) —
    /// 보유처가 TutorialScenario*.asset 다섯 개뿐이라 그 파일들의 값을 같이 옮겼다.
    /// 앞으로 값을 더 넣을 땐 반드시 끝에 추가할 것(같은 마이그레이션을 다시 하지 않으려면).</summary>
    public enum BannerAnchor { Top, UpperMiddle, Center, LowerMiddle, Bottom }

    /// <summary>카드 낱장 포커스 대상 진영. None(0)이 기본이라 기존 시나리오는 자동으로 꺼진 상태다 —
    /// 슬롯 번호만 두면 기본값 0이 "0번 슬롯 포커스 켜짐"이 돼버려서 진영 자체를 스위치로 쓴다.
    /// (SO는 int 직렬화 → 새 값은 반드시 끝에 추가.)</summary>
    public enum CardFocusSide { None, Player, Enemy }

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

        [Tooltip("이 스텝에 허용할 조작. Any = 제한 없음. 지정하면 그 제스처 외 조작은 완전 무반응 " +
                 "(한 조작법을 배우는 동안 다른 조작법 차단). Attack 스텝 전용")]
        public InputGesture allowedGesture;   // 기본값 0 = Any → 기존 시나리오 호환

        // ── 필드 포커스 자유 선택 (자유 스텝 전용: attackerSlot/targetSlot 둘 다 -1) ──
        // 슬롯을 지정하지 않고 "아무 아군 → 아무 적"을 고르게 하되, 지금 어느 진영을 고를 차례인지
        // 딤 구멍으로 알려준다. 강제는 하지 않는다(자유도 유지, 도와만 주는 안내).

        [Tooltip("자유 선택 안내. 켜면 1단계=아군 필드만 남기고 딤, 아군을 고르면 2단계=적 필드만 남기고 딤. " +
                 "자유 스텝(attackerSlot=-1, targetSlot=-1)에서만 동작")]
        public bool guidedFreeSelect;

        [Tooltip("2단계(적 고르기) 안내 문구. 비우면 1단계 문구를 그대로 유지")]
        [TextArea] public string targetGuideMessage;

        [Tooltip("1단계(아군 고르기) 배너 위치")]
        public BannerAnchor bannerAnchor;          // 기본값 0 = Top → 기존 배너 위치와 동일

        [Tooltip("2단계(적 고르기) 배너 위치")]
        public BannerAnchor targetBannerAnchor;

        // ── 카드 낱장 포커스 ──
        // 카드 한 장만 남기고 배경·나머지 카드를 딤. Message/Inspect/Attack 어느 스텝에서든 쓸 수 있다.
        // Message 스텝이면 탭으로 진행하고, 그 동안 딤이 유지된다.

        [Tooltip("카드 낱장 포커스 대상 진영. None = 사용 안 함")]
        public CardFocusSide cardFocusSide;

        [Tooltip("포커스할 슬롯(0~2). cardFocusSide가 None이면 무시")]
        public int cardFocusSlot;

        // ── 가이드 핸드 위치 지정 ──
        // 기본은 자동(포커스한 카드 / 지정 공격자 / 포커스 영역 중앙)이다. 자동이 가리키는 곳과
        // 실제로 눌러야 할 곳이 다른 스텝에서만 여기서 슬롯을 직접 찍는다.
        // 자유 선택 스텝에서는 진영이 곧 단계다 — Player면 1단계(아군 고르기), Enemy면 2단계(적 고르기)에 적용.

        [Tooltip("가이드 핸드를 띄울 진영. None = 자동 배치")]
        public CardFocusSide handSide;

        [Tooltip("핸드를 띄울 슬롯(0~2). handSide가 None이면 무시")]
        public int handSlot;
    }

    [Header("시너지 표시/적용 (기본 off — 초반 튜토리얼은 시너지 개념 미도입, 3편부터 on)")]
    public bool enableSynergy;

    [Header("카드 레벨 (0 = Lv1 호환값)")]
    [Min(0)] [Tooltip("플레이어 카드에 적용할 성장 레벨. 0은 기존 에셋 호환을 위해 Lv1로 취급")]
    public int playerCardLevel;
    [Min(0)] [Tooltip("적 카드에 적용할 성장 레벨. 0은 기존 에셋 호환을 위해 Lv1로 취급")]
    public int enemyCardLevel;

    public int PlayerCardLevel => Mathf.Max(CardGrowth.BaseLevel, this.playerCardLevel);
    public int EnemyCardLevel  => Mathf.Max(CardGrowth.BaseLevel, this.enemyCardLevel);

    [Header("고정 덱 (순서 = 등장 순서, 셔플 없음, 6장 이하 허용)")]
    public List<CardData> playerDeck;
    public List<CardData> enemyDeck;

    [Header("플레이어 강제 공격 순서 (턴당 1건, 처형 재공격 시 연속 소비)")]
    public List<ScriptedAttack> playerScript;

    [Header("적 강제 공격 순서 (턴당 1건, 처형 재공격 시 연속 소비)")]
    public List<ScriptedAttack> enemyScript;

    [Header("스크립트 소진 후 자유 공격 전환 (기본 off — on이면 큐 소진 시 플레이어가 자유롭게 공격, 안내 없음)")]
    public bool freePlayAfterScript;

    [Header("적 체력 상한 (0 = off. >0이면 적 카드 현재 체력을 이 값 이하로 클램프 — 확정승/스텝 소진 보장용)")]
    public int enemyMaxHpOverride;
}
