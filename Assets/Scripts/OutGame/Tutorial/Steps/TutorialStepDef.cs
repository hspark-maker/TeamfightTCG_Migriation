using System;
using System.Collections.Generic;
using UnityEngine;

// 튜토리얼 시퀀스의 한 행(런타임 상태를 갖지 않아 같은 행을 여러 자리에 복제해도 안전)
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

    [Tooltip("이 스텝에 도달하면 남은 기능을 **전부** 연다(누적 — 이후 스텝에서도 유지된다).\n"
           + "안내는 계속 돌지만 게임의 문은 여기서 열린다는 뜻이다. 졸업까지 기다리지 않고 미리 여는 자리에 켠다.\n"
           + "위 unlocks를 하나하나 채우는 대신 쓰는 스위치라, 나중에 기능이 늘어도 저작을 고칠 필요가 없다.\n"
           + "⚠ 아래 locks는 여전히 이긴다 — 그 스텝 동안 막아 둔 옆길은 전체 해금 뒤에도 막힌다.")]
    [SerializeField] bool unlocksAll;

    [Tooltip("이 스텝 동안만 다시 잠그는 기능(일시).\n"
           + "unlocks와 달리 누적되지 않는다 — 다음 스텝으로 넘어가면 저절로 원래 해금 상태로 돌아간다.\n"
           + "이미 열린 기능에도 걸리고 해금보다 우선한다. 게이트 판이 닿지 않는 자리(팝업·다른 화면)의 옆길을 막을 때 쓴다.\n"
           + "⚠ 이 스텝이 지목하는 앵커의 기능을 여기 넣으면 눌러야 할 버튼을 스스로 막아 진행이 멎는다.\n"
           + "⚠ 잠그려는 위젯에 잠금 키가 배선돼 있어야 한다(탭이면 LobbyTabController의 unlockFeature) — "
           + "None인 위젯은 잠글 대상이 없어 아무 일도 일어나지 않는다")]
    [SerializeField] List<EOutgameFeature> locks = new List<EOutgameFeature>();

    [Tooltip("화면을 어둡게 할지. 끄면 딤 판이 투명해질 뿐 그대로 깔린다 — 타깃 외 입력 차단도 타깃 강조(승격)도 켰을 때와 같다")]
    [SerializeField] bool useDim = true;

    [Tooltip("WaitPurchase: 상점 진열·판매 대상 / AutoPurchase: 자동 구매할 팩 / DeckAutoEquip: 자동 편성이 채울 풀")]
    [SerializeField] CardPackData pack;

    [Tooltip("BattleEntry·AutoBattle: 전투에 넘길 시나리오 / DeckGrant: 지급할 덱의 정본")]
    [SerializeField] TutorialScenarioData scenario;

    [Tooltip("전투 전 덱 확인/편집 화면(MatchDeckRoot)을 띄운다. 전투 덱은 켜든 끄든 시나리오 고정이다.\n"
           + "저장된 유효 덱이 없으면 이 화면에서 전투를 시작할 수 없으니, 덱이 생긴 뒤 챕터에만 켠다")]
    [SerializeField] bool showDeckGate;

    [Tooltip("덱 목록에 표시할 이름")]
    [SerializeField] string deckName;

    public EOutgameTutorialAction Action => action;

    public string GuideMessage => guideMessage;

    // 이 스텝까지 진행하면 열리는 기능(해금은 누적)
    public IReadOnlyList<EOutgameFeature> Unlocks => unlocks;

    // 이 스텝부터 남은 기능을 전부 여는가(졸업을 기다리지 않고 미리 여는 자리)
    public bool UnlocksAll => unlocksAll;

    // 이 스텝 동안만 닫히는 기능(누적되지 않는다 — 다음 스텝에서 저절로 풀린다)
    public IReadOnlyList<EOutgameFeature> Locks => locks;

    // 딤 판을 어둡게 보일지(false여도 판은 깔린다 — 룩만 정하는 값)
    public bool UseDim => useDim;

    public CardPackData Pack => pack;

    public TutorialScenarioData Scenario => scenario;

    public bool ShowDeckGate => showDeckGate;

    public string DeckName => deckName;

    // 안내 타깃(앵커를 쓰지 않는 액션은 저작값이 남아 있어도 None으로 본다)
    public EOutgameTutorialAnchor Anchor => UsesAnchor(action) ? anchor : EOutgameTutorialAnchor.None;

    // 무엇이 이 스텝을 완료시키는가(액션에서 파생)
    public EOutgameTutorialCompletion Completion => action switch
    {
        EOutgameTutorialAction.Message         => EOutgameTutorialCompletion.Confirm,
        EOutgameTutorialAction.WaitPurchase    => EOutgameTutorialCompletion.Purchase,
        EOutgameTutorialAction.WaitPackOpen    => EOutgameTutorialCompletion.PackOpen,
        EOutgameTutorialAction.WaitAlbumInsert => EOutgameTutorialCompletion.AlbumInsert,
        EOutgameTutorialAction.WaitEnhance     => EOutgameTutorialCompletion.Enhance,
        EOutgameTutorialAction.EnterFirstRank  => EOutgameTutorialCompletion.RankEffect,
        EOutgameTutorialAction.WaitLobbyReturn => EOutgameTutorialCompletion.LobbyReturn,

        EOutgameTutorialAction.WaitClick     or
        EOutgameTutorialAction.DeckAutoEquip or
        EOutgameTutorialAction.BattleEntry   or
        EOutgameTutorialAction.BattleStart   => EOutgameTutorialCompletion.Click,

        _ => EOutgameTutorialCompletion.Auto,
    };

    // 완료 뒤 이 씬에서 이어 걸 스텝이 없다(씬 전환·전투가 화면을 넘겨받는 경우)
    public bool LeavesScene => action switch
    {
        EOutgameTutorialAction.AutoBattle  => true,
        EOutgameTutorialAction.BattleStart => true,
        EOutgameTutorialAction.BattleEntry => !showDeckGate,

        _ => false,
    };

    // 이 스텝이 상점 진열·판매 대상을 덮어쓰면 true
    public bool TryGetForcedPack(out CardPackData _pack)
    {
        _pack = action == EOutgameTutorialAction.WaitPurchase ? pack : null;

        return _pack != null;
    }

    // 이 스텝이 덱 자동 편성으로 채울 카드를 지정하면 true(풀 전체를 넘긴다 — 덱 크기는 편성 쪽이 정의)
    // 기본 pool 직독: 튜토리얼 팩은 rankPools 미저작 전제(저작 시 실제 드로우 ResolvePool과 어긋남)
    public bool TryGetForcedDeck(out IReadOnlyList<CardData> _cards)
    {
        _cards = action == EOutgameTutorialAction.DeckAutoEquip && pack != null && pack.PoolCount > 0
            ? pack.Pool
            : null;

        return _cards != null;
    }

    // 이 액션이 앵커를 쓰는가(런타임 판정과 드로어의 필드 노출이 공유)
    public static bool UsesAnchor(EOutgameTutorialAction _action) => _action switch
    {
        EOutgameTutorialAction.WaitPackOpen    or
        EOutgameTutorialAction.WaitAlbumInsert or
        EOutgameTutorialAction.AutoBattle      or
        EOutgameTutorialAction.AutoPurchase    or
        EOutgameTutorialAction.DeckGrant       or
        EOutgameTutorialAction.CloseCardDetail or
        EOutgameTutorialAction.EnterFirstRank  or
        EOutgameTutorialAction.WaitLobbyReturn => false,

        _ => true,
    };

    // 이 액션이 안내 문구를 띄우는가(자동 스텝은 화면에 아무것도 그리지 않는다)
    // 삽입 대기는 자동 스텝이 아니지만 연출 자체가 손가락·문구를 띄운다 — 겹쳐 그리지 않는다
    public static bool ShowsGuideMessage(EOutgameTutorialAction _action) => _action switch
    {
        EOutgameTutorialAction.WaitAlbumInsert or
        EOutgameTutorialAction.AutoBattle      or
        EOutgameTutorialAction.AutoPurchase    or
        EOutgameTutorialAction.DeckGrant       or
        EOutgameTutorialAction.CloseCardDetail or
        EOutgameTutorialAction.EnterFirstRank  or
        EOutgameTutorialAction.WaitLobbyReturn => false,

        _ => true,
    };

    // 이 액션이 딤을 걸 수 있는가
    public static bool UsesDim(EOutgameTutorialAction _action) =>
        ShowsGuideMessage(_action) && _action != EOutgameTutorialAction.WaitPackOpen;

    // 이 액션이 팩을 쓰는가(진열 고정·자동 구매·자동 편성 풀)
    public static bool UsesPack(EOutgameTutorialAction _action) => _action switch
    {
        EOutgameTutorialAction.WaitPurchase  or
        EOutgameTutorialAction.AutoPurchase  or
        EOutgameTutorialAction.DeckAutoEquip => true,

        _ => false,
    };

    // 이 액션이 시나리오를 쓰는가(전투 주입 또는 덱 정본)
    public static bool UsesScenario(EOutgameTutorialAction _action) => _action switch
    {
        EOutgameTutorialAction.BattleEntry or
        EOutgameTutorialAction.AutoBattle  or
        EOutgameTutorialAction.DeckGrant   => true,

        _ => false,
    };

    // 이 액션이 덱 게이트 노출을 정하는가(전투에 넣는 액션만)
    public static bool UsesShowDeckGate(EOutgameTutorialAction _action) =>
        _action == EOutgameTutorialAction.BattleEntry || _action == EOutgameTutorialAction.AutoBattle;

    // 이 액션이 덱 이름을 쓰는가
    public static bool UsesDeckName(EOutgameTutorialAction _action) =>
        _action == EOutgameTutorialAction.DeckGrant;
}
