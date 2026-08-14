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

    [Tooltip("문구를 화면 중앙이 아니라 하단에 놓는다.\n"
           + "무대 한가운데에 보여 줘야 할 것이 있는 자리에 켠다 — 예: 강화 결과 화면에 얹는 말은 카드를 가리면 안 된다.\n"
           + "설명(Message) 스텝에만 뜬다. 다른 스텝의 문구 자리는 타깃을 피해 스스로 정해진다")]
    [SerializeField] bool messageAtBottom;

    [Tooltip("이 스텝에 도달하면 열리는 기능(누적). 이 스텝이 지목하는 앵커의 기능은 반드시 여기까지 포함되어야 한다")]
    [SerializeField] List<EOutgameFeature> unlocks = new List<EOutgameFeature>();

    [Tooltip("이 스텝에 도달하면 남은 기능을 **전부** 연다(누적 — 이후 스텝에서도 유지된다).\n"
           + "안내는 계속 돌지만 게임의 문은 여기서 열린다는 뜻이다. 졸업까지 기다리지 않고 미리 여는 자리에 켠다.\n"
           + "위 unlocks를 하나하나 채우는 대신 쓰는 스위치라, 나중에 기능이 늘어도 저작을 고칠 필요가 없다.\n"
           + "⚠ 아래 locks는 여전히 이긴다 — 그 스텝 동안 막아 둔 옆길은 전체 해금 뒤에도 막힌다.")]
    [SerializeField] bool unlocksAll;

    [Tooltip("이 스텝 동안만 다시 잠그는 기능(일시).\n"
           + "unlocks와 달리 누적되지 않는다 — 다음 스텝으로 넘어가면 저절로 원래 해금 상태로 돌아간다.\n"
           + "이미 열린 기능에도 걸리고 해금보다 우선한다. 딤을 켜지 않고 옆길만 막고 싶을 때 쓴다.\n"
           + "⚠ 이 스텝이 지목하는 앵커의 기능을 여기 넣으면 눌러야 할 버튼을 스스로 막아 진행이 멎는다.\n"
           + "⚠ 잠그려는 위젯에 잠금 키가 배선돼 있어야 한다(탭이면 LobbyTabController의 unlockFeature) — "
           + "None인 위젯은 잠글 대상이 없어 아무 일도 일어나지 않는다")]
    [SerializeField] List<EOutgameFeature> locks = new List<EOutgameFeature>();

    [Tooltip("타깃 외 입력을 딤으로 막을지. 끄면 화면이 어두워지지 않고 입력도 막지 않는다(차단은 아래 locks가 맡는다)")]
    [SerializeField] bool useDim = true;

    [Tooltip("WaitPurchase: 상점 진열·판매 대상 / AutoPurchase: 자동 구매할 팩 / DeckAutoEquip: 자동 편성이 채울 풀")]
    [SerializeField] CardPackData pack;

    [Tooltip("이 스텝 동안 상점 가격 자리에 대신 띄울 문구(예: \"무료\"). 비우면 팩의 실제 가격이 숫자로 나온다.\n"
           + "문구를 넣으면 재화 아이콘도 함께 숨는다 — 값을 치르는 물건이 아니라고 말하는 자리이기 때문이다.\n"
           + "⚠ 표기만 바꾼다. 실제 결제는 팩 SO의 price가 그대로 한다 — 무료로 보이게 하려면 그 팩의 가격이 0이어야 한다")]
    [SerializeField] string packPriceLabel;

    [Tooltip("도감 앵커(테마 칸·카드 칸)가 **어느 것**을 가리킬지. 앵커 키는 자리 종류만 말하고 도감엔 그 자리가 여럿이라, "
           + "이 카드가 든 테마·칸을 화면이 찾아 지목한다.\n"
           + "비우면 화면이 대신 고른다 — 아직 안 꽂은 카드가 있는 첫 테마 / 그 페이지의 첫 소유 칸.\n"
           + "⚠ 유저가 그 카드를 아직 소유하지 않았으면 가리킬 칸이 없어 폴백으로 떨어진다(안내는 멈추지 않는다)")]
    [SerializeField] CardData anchorCard;

    [Tooltip("이 스텝이 시키는 성장 한 방을 안내가 대신 내준다(값 0).\n"
           + "표시·활성 판정·실제 소모가 모두 이 값을 함께 본다 — 화면엔 100골드가 뜨는데 0이 나가는 일은 없다.\n"
           + "성공하면 그 자리에서 소진된다(실패는 소진하지 않는다 — 안내가 시킨 강화를 유저 돈으로 다시 하게 두지 않는다).\n"
           + "다음 스텝으로 넘어가면 저절로 풀린다")]
    [SerializeField] bool freeOfCharge;

    [Tooltip("CardGrant·CardSetGrant: 보상 화면에 띄울 제목. 비우면 기본 문구를 쓴다")]
    [SerializeField] string rewardTitle;

    [Tooltip("BattleEntry·AutoBattle: 전투에 넘길 시나리오 / DeckGrant: 지급할 덱의 정본")]
    [SerializeField] TutorialScenarioData scenario;

    [Tooltip("CardGrant: 지급할 카드 한 장. 이미 소유한 카드를 꽂아도 안전하지만 획득 연출은 그대로 돈다")]
    [SerializeField] CardData card;

    [Tooltip("CardSetGrant: 한 묶음으로 지급할 카드들. 순서 = 패널 격자에 놓이는 순서.\n"
           + "· 이미 소유한 카드를 넣어도 안전하다. 다만 획득 연출은 그대로 돈다(중복 표시 없이 새 카드처럼 보인다).\n"
           + "· 빈 칸(None)은 건너뛴다 — 격자 자리도 차지하지 않는다.\n"
           + "· 소유권만 준다. 덱에는 편성되지 않는다(덱 저작은 DeckGrant 몫이다).")]
    [SerializeField] List<CardData> cards = new List<CardData>();

    [Tooltip("전투 전 덱 확인/편집 화면(MatchDeckRoot)을 띄운다. 전투 덱은 켜든 끄든 시나리오 고정이다.\n"
           + "저장된 유효 덱이 없으면 이 화면에서 전투를 시작할 수 없으니, 덱이 생긴 뒤 챕터에만 켠다.\n"
           + "⚠ 이걸 켠 BattleEntry는 반드시 같은 챕터 안에서 BattleStart(또는 AutoBattle)로 닫는다.\n"
           + "· 그 사이 스텝들은 이 화면이 열려 있어야만 성립하는데, 화면을 여는 시나리오는 앱을 끄면 사라진다 —\n"
           + "  그래서 그 구간에서 재부팅하면 좌표가 이 진입 스텝으로 되감기고 사이 스텝이 다시 재생된다.\n"
           + "· 따라서 그 사이에는 되풀이돼도 안전한 스텝(안내·클릭 대기)만 둔다. 재화를 쓰거나 보상을 주는\n"
           + "  스텝(AutoPurchase·CardGrant 등)을 끼우면 재개할 때마다 중복 실행된다")]
    [SerializeField] bool showDeckGate;

    [Tooltip("덱 목록에 표시할 이름")]
    [SerializeField] string deckName;

    [Tooltip("이 스텝의 실행이 실패했을 때(참조 미배선·화면 부재·구매 실패 등) 어떻게 끝낼지.\n"
           + "· Skip = 실패한 일만 생략하고 다음 칸으로 넘어간다. 안내는 계속 흐른다.\n"
           + "· Halt = 좌표를 이 칸으로 되돌려 그 자리에 세운다. 재시도는 다음 부팅이다.\n"
           + "  멈춘다고 게임까지 막히지는 않는다 — 정지로 판정되는 즉시 남은 기능이 전부 열린다(안내만 끝난다).\n"
           + "⚠ 뒤 스텝이 이 스텝의 결과에 기대는 경우에만 Halt를 쓴다(예: 여기서 산 팩을 다음 스텝이 개봉).\n"
           + "  그렇지 않으면 Skip이 낫다 — 안내가 끝까지 흐르는 편이 유저에게 이롭다.\n"
           + "⚠ 실패 분기가 있는 액션(AutoPurchase·DeckGrant·CardGrant·CardSetGrant)에서만 뜬다. "
           + "다른 액션은 실패해도 이 값을 보지 않는다")]
    [SerializeField] EOutgameTutorialFailure onFailure;

    public EOutgameTutorialAction Action => action;

    public string GuideMessage => guideMessage;

    // 문구를 하단에 두는가(자리 저작이 없는 액션은 저작값이 남아 있어도 중앙으로 본다)
    public bool MessageAtBottom => UsesMessagePlacement(action) && messageAtBottom;

    // 이 스텝까지 진행하면 열리는 기능(해금은 누적)
    public IReadOnlyList<EOutgameFeature> Unlocks => unlocks;

    // 이 스텝부터 남은 기능을 전부 여는가(졸업을 기다리지 않고 미리 여는 자리)
    public bool UnlocksAll => unlocksAll;

    // 이 스텝 동안만 닫히는 기능(누적되지 않는다 — 다음 스텝에서 저절로 풀린다)
    public IReadOnlyList<EOutgameFeature> Locks => locks;

    // 딤으로 타깃 외 입력을 막는가(false면 차단은 잠금에 맡긴다)
    public bool UseDim => useDim;

    public CardPackData Pack => pack;

    public TutorialScenarioData Scenario => scenario;

    public CardData Card => card;

    // 안내가 지목한 카드(그 자리를 카드로 고르는 앵커에서만 — 아니면 저작값이 남아 있어도 없는 것으로 본다)
    public CardData AnchorCard => UsesAnchorCard(Anchor) ? anchorCard : null;

    // 이 스텝이 시키는 성장 한 방이 무료인가(값 저작이 없는 액션은 저작값이 남아 있어도 유료로 본다)
    public bool FreeOfCharge => UsesFreeOfCharge(action) && freeOfCharge;

    // 보상 화면 제목(비우면 호출자가 기본 문구를 쓴다)
    public string RewardTitle => UsesRewardTitle(action) ? rewardTitle : null;

    public IReadOnlyList<CardData> Cards => cards;

    public bool ShowDeckGate => showDeckGate;

    public string DeckName => deckName;

    // 실패했을 때의 결말(실패 분기가 없는 액션은 저작값이 남아 있어도 Skip으로 본다)
    public EOutgameTutorialFailure OnFailure => UsesFailurePolicy(action) ? onFailure : EOutgameTutorialFailure.Skip;

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
        EOutgameTutorialAction.WaitKeywordEnhance => EOutgameTutorialCompletion.KeywordEnhance,
        EOutgameTutorialAction.EnterFirstRank  => EOutgameTutorialCompletion.RankEffect,
        EOutgameTutorialAction.WaitLobbyReturn => EOutgameTutorialCompletion.LobbyReturn,
        EOutgameTutorialAction.WaitCardDetailReturn => EOutgameTutorialCompletion.CardDetailReturn,
        EOutgameTutorialAction.CardGrant       or
        EOutgameTutorialAction.CardSetGrant    => EOutgameTutorialCompletion.CardGain,

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

    // 이 스텝이 상점 진열·판매 대상을 덮어쓰면 true(가격 자리 문구도 함께 — 비었으면 실제 가격을 쓰라는 뜻)
    public bool TryGetForcedPack(out CardPackData _pack, out string _priceLabel)
    {
        bool t_forces = action == EOutgameTutorialAction.WaitPurchase;

        _pack       = t_forces ? pack : null;
        _priceLabel = t_forces ? packPriceLabel : null;

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
        EOutgameTutorialAction.WaitLobbyReturn or
        EOutgameTutorialAction.WaitCardDetailReturn or
        EOutgameTutorialAction.CardGrant       or
        EOutgameTutorialAction.CardSetGrant    => false,

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
        EOutgameTutorialAction.WaitLobbyReturn or
        EOutgameTutorialAction.WaitCardDetailReturn or
        EOutgameTutorialAction.CardGrant       or
        EOutgameTutorialAction.CardSetGrant    => false,

        _ => true,
    };

    // 이 액션이 문구 자리를 저작하는가(딤 탭으로 넘기는 설명 스텝뿐 — 나머지는 타깃을 피해 자리가 정해진다)
    public static bool UsesMessagePlacement(EOutgameTutorialAction _action)
        => _action == EOutgameTutorialAction.Message;

    // 이 앵커가 "그 자리 중 어느 것"까지 저작받아야 하는가.
    // 도감은 같은 종류의 자리가 여럿이라 키만으로는 대상이 정해지지 않는다(버튼 하나짜리 앵커는 물을 것이 없다).
    public static bool UsesAnchorCard(EOutgameTutorialAnchor _anchor)
        => _anchor == EOutgameTutorialAnchor.AlbumThemeCell
        || _anchor == EOutgameTutorialAnchor.AlbumCardSlot;

    // 이 액션이 값을 무는가(안내가 대신 내줄 수 있는 자리 = 성장 한 방을 시키는 스텝)
    public static bool UsesFreeOfCharge(EOutgameTutorialAction _action)
        => _action == EOutgameTutorialAction.WaitEnhance
        || _action == EOutgameTutorialAction.WaitKeywordEnhance;

    // 이 액션이 보상 화면을 세우는가
    public static bool UsesRewardTitle(EOutgameTutorialAction _action)
        => _action == EOutgameTutorialAction.CardGrant || _action == EOutgameTutorialAction.CardSetGrant;

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

    // 이 액션이 가격 표기 문구를 쓰는가(상점 진열을 덮어쓰는 액션만 — 화면에 가격 자리가 있는 경우다)
    public static bool UsesPackPriceLabel(EOutgameTutorialAction _action) =>
        _action == EOutgameTutorialAction.WaitPurchase;

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

    // 이 액션이 실패 정책을 쓰는가 — 실행기가 실제로 실패 분기를 갖는 액션만.
    // 대기형은 실패 개념이 없고, 전투 진입 계열은 시나리오가 비어도 일반 전투로 그냥 들어간다(실패로 치지 않는다).
    public static bool UsesFailurePolicy(EOutgameTutorialAction _action) => _action switch
    {
        EOutgameTutorialAction.AutoPurchase or
        EOutgameTutorialAction.DeckGrant    or
        EOutgameTutorialAction.CardGrant    or
        EOutgameTutorialAction.CardSetGrant => true,

        _ => false,
    };

    // 이 액션이 카드 한 장을 쓰는가(지급 대상)
    public static bool UsesCard(EOutgameTutorialAction _action) =>
        _action == EOutgameTutorialAction.CardGrant;

    // 이 액션이 카드 묶음을 쓰는가(한 번에 지급하는 세트)
    public static bool UsesCards(EOutgameTutorialAction _action) =>
        _action == EOutgameTutorialAction.CardSetGrant;
}
