using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>튜토리얼 시퀀스의 한 행. 스텝 SO 계층(클래스 10종 · 자산 33개)을 대체한다.
/// 자산으로 가른 두 근거가 실제 저작에서 성립하지 않았다 — "종류별 필드만 노출"은 드로어가 대신하고
/// (TutorialStepDefDrawer), "에셋 재사용"은 33개 중 2건뿐이었다. 챕터를 인라인한 것과 같은 판단이다.
///
/// 런타임 상태를 갖지 않는다 — 진행도는 실행기가 넘긴 컨텍스트로만 건드리므로 같은 행을 여러 자리에 복제해도 안전하다.</summary>
[Serializable]
public class TutorialStepDef
{
    [Tooltip("이 스텝이 무엇을 하는가. 완료 조건·씬 이탈 여부가 여기서 파생된다")]
    [SerializeField] EOutgameTutorialAction action;

    [Tooltip("안내 타깃 위젯. 액션에 따라 누를 대상이거나 강조만 할 영역이다")]
    [SerializeField] EOutgameTutorialAnchor anchor;

    [Tooltip("게이트 배너 문구. 비우면 배너를 띄우지 않는다")]
    [TextArea][SerializeField] string guideMessage;

    [Tooltip("이 스텝에 도달하면 열리는 기능(누적). 이 스텝이 지목하는 앵커의 기능은 반드시 여기까지 포함되어야 한다")]
    [SerializeField] List<EOutgameFeature> unlocks = new List<EOutgameFeature>();

    [Tooltip("타깃 외 입력을 딤으로 막을지. 잠금만으로 흐름이 잡히는 스텝은 꺼서 화면을 어둡게 하지 않는다")]
    [SerializeField] bool useDim = true;

    [Tooltip("WaitPurchase: 상점 진열·판매 대상 / AutoPurchase: 자동 구매할 팩 / DeckAutoEquip: 자동 편성이 채울 풀")]
    [SerializeField] CardPackData pack;

    [Tooltip("중복 카드 1장당 환급 골드")]
    [SerializeField] long duplicateRefundGold;

    [Tooltip("BattleEntry·AutoBattle: 전투에 넘길 시나리오 / DeckGrant: 지급할 덱의 정본")]
    [SerializeField] TutorialScenarioData scenario;

    [Tooltip("전투 전 덱 확인/편집 화면(MatchDeckRoot)을 띄운다. 전투 덱은 켜든 끄든 시나리오 고정이다.\n"
           + "저장된 유효 덱이 없으면 이 화면에서 전투를 시작할 수 없으니, 덱이 생긴 뒤 챕터에만 켠다")]
    [SerializeField] bool showDeckGate;

    [Tooltip("덱 목록에 표시할 이름")]
    [SerializeField] string deckName;

    public EOutgameTutorialAction Action => action;

    public string GuideMessage => guideMessage;

    /// <summary>이 스텝까지 진행하면 열리는 기능. 해금은 누적이라 한 번 열린 것은 다시 잠기지 않는다.</summary>
    public IReadOnlyList<EOutgameFeature> Unlocks => unlocks;

    /// <summary>딤으로 타깃 외 입력을 막는가. false면 링·손가락·문구만 띄우고 차단은 잠금에 맡긴다.</summary>
    public bool UseDim => useDim;

    public CardPackData Pack => pack;

    public long DuplicateRefundGold => duplicateRefundGold;

    public TutorialScenarioData Scenario => scenario;

    public bool ShowDeckGate => showDeckGate;

    public string DeckName => deckName;

    /// <summary>안내 타깃. 앵커를 쓰지 않는 액션은 저작값이 남아 있어도 None으로 본다
    /// — 액션을 바꾼 뒤 남은 값이 엉뚱한 게이트를 켜지 않게(자산이 아니라 행이라 값이 그대로 남는다).</summary>
    public EOutgameTutorialAnchor Anchor => UsesAnchor(action) ? anchor : EOutgameTutorialAnchor.None;

    /// <summary>무엇이 이 스텝을 완료시키는가. 액션에서 파생되므로 저작에서 둘이 어긋날 수 없다.</summary>
    public EOutgameTutorialCompletion Completion => action switch
    {
        EOutgameTutorialAction.Message      => EOutgameTutorialCompletion.Confirm,
        EOutgameTutorialAction.WaitPurchase => EOutgameTutorialCompletion.Purchase,
        EOutgameTutorialAction.WaitPackOpen => EOutgameTutorialCompletion.PackOpen,

        EOutgameTutorialAction.WaitClick     or
        EOutgameTutorialAction.DeckAutoEquip or
        EOutgameTutorialAction.BattleEntry   or
        EOutgameTutorialAction.BattleStart   => EOutgameTutorialCompletion.Click,

        _ => EOutgameTutorialCompletion.Auto,
    };

    /// <summary>완료 뒤 이 씬에서 이어 걸 스텝이 없다 → 같은 씬에서 다음 스텝을 진입시키지 않는다.
    /// 씬 전환(AutoBattle)뿐 아니라 전투가 화면을 넘겨받는 경우(BattleStart)도 포함한다.
    /// BattleEntry는 덱 게이트를 켜면 클릭이 로비 오버레이를 열 뿐이라 씬이 그대로다.</summary>
    public bool LeavesScene => action switch
    {
        EOutgameTutorialAction.AutoBattle  => true,
        EOutgameTutorialAction.BattleStart => true,
        EOutgameTutorialAction.BattleEntry => !showDeckGate,

        _ => false,
    };

    /// <summary>이 스텝이 상점 진열·판매 대상을 덮어쓰면 true — 튜토리얼 중 구매 결과를 저작대로 고정한다.</summary>
    public bool TryGetForcedPack(out CardPackData _pack, out long _refundGold)
    {
        _pack       = action == EOutgameTutorialAction.WaitPurchase ? pack : null;
        _refundGold = _pack != null ? duplicateRefundGold : 0;

        return _pack != null;
    }

    /// <summary>이 스텝이 덱 자동 편성으로 채울 카드를 지정하면 true — 튜토리얼 중 편성 결과를 저작대로 고정한다.
    /// 앞의 6장만 쓰이는 셈이다(빈 칸이 떨어지면 채우기가 스스로 멈춘다 — DeckEditController.AutoEquip).
    /// 풀을 잘라 넘기지 않는 이유: 덱 크기를 여기서 한 번 더 정의하면 진실원이 둘이 된다.</summary>
    public bool TryGetForcedDeck(out IReadOnlyList<CardData> _cards)
    {
        // 빈 팩은 "지정 없음"과 같다 — 그대로 넘기면 지정이 있는 셈 치고 일반 규칙이 밀린다.
        _cards = action == EOutgameTutorialAction.DeckAutoEquip && pack != null && pack.PoolCount > 0
            ? pack.Pool
            : null;

        return _cards != null;
    }

    /// <summary>이 액션이 앵커를 쓰는가. 런타임 판정과 드로어의 필드 노출이 이 하나를 공유한다.</summary>
    public static bool UsesAnchor(EOutgameTutorialAction _action) => _action switch
    {
        EOutgameTutorialAction.WaitPackOpen or
        EOutgameTutorialAction.AutoBattle   or
        EOutgameTutorialAction.AutoPurchase or
        EOutgameTutorialAction.DeckGrant    => false,

        _ => true,
    };

    /// <summary>이 액션이 안내 문구를 띄우는가. 자동 스텝은 화면에 아무것도 그리지 않는다.</summary>
    public static bool ShowsGuideMessage(EOutgameTutorialAction _action) => _action switch
    {
        EOutgameTutorialAction.AutoBattle   or
        EOutgameTutorialAction.AutoPurchase or
        EOutgameTutorialAction.DeckGrant    => false,

        _ => true,
    };

    /// <summary>이 액션이 딤을 걸 수 있는가. WaitPackOpen은 배너만 띄우므로 딤 선택지가 없다.</summary>
    public static bool UsesDim(EOutgameTutorialAction _action) =>
        ShowsGuideMessage(_action) && _action != EOutgameTutorialAction.WaitPackOpen;

    /// <summary>이 액션이 팩을 쓰는가(진열 고정·자동 구매·자동 편성 풀).</summary>
    public static bool UsesPack(EOutgameTutorialAction _action) => _action switch
    {
        EOutgameTutorialAction.WaitPurchase  or
        EOutgameTutorialAction.AutoPurchase  or
        EOutgameTutorialAction.DeckAutoEquip => true,

        _ => false,
    };

    /// <summary>이 액션이 중복 환급 골드를 쓰는가(실제로 구매하는 액션만).</summary>
    public static bool UsesRefundGold(EOutgameTutorialAction _action) =>
        _action == EOutgameTutorialAction.WaitPurchase || _action == EOutgameTutorialAction.AutoPurchase;

    /// <summary>이 액션이 시나리오를 쓰는가(전투 주입 또는 덱 정본).</summary>
    public static bool UsesScenario(EOutgameTutorialAction _action) => _action switch
    {
        EOutgameTutorialAction.BattleEntry or
        EOutgameTutorialAction.AutoBattle  or
        EOutgameTutorialAction.DeckGrant   => true,

        _ => false,
    };

    /// <summary>이 액션이 덱 게이트 노출을 정하는가(전투에 넣는 액션만).</summary>
    public static bool UsesShowDeckGate(EOutgameTutorialAction _action) =>
        _action == EOutgameTutorialAction.BattleEntry || _action == EOutgameTutorialAction.AutoBattle;

    /// <summary>이 액션이 덱 이름을 쓰는가.</summary>
    public static bool UsesDeckName(EOutgameTutorialAction _action) =>
        _action == EOutgameTutorialAction.DeckGrant;
}
