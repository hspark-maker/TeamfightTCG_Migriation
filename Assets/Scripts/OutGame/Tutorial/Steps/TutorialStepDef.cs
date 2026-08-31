using System;
using System.Collections.Generic;
using UnityEngine;

// 튜토리얼 시퀀스의 한 행(런타임 상태를 갖지 않는다 — 세이브가 붙잡는 것은 stepId 하나뿐이다)
[Serializable]
public class TutorialStepDef
{
    // 세이브가 이 스텝을 붙잡는 불변 번호(0 = 미부여). 시퀀스 SO의 [스텝 ID 부여]만 값을 만진다 —
    // 드로어가 필드로 노출하지 않고 요약 줄에 #N으로만 보여 주므로 [Tooltip]을 달지 않는다(뜰 자리가 없다).
    // 저작자용 안내는 TutorialStepDefDrawer.StepIdLabel의 툴팁에 있다.
    [SerializeField] int stepId;

    [Tooltip("이 스텝이 무엇을 하는가. 완료 조건·씬 이탈 여부가 여기서 파생된다")]
    [SerializeField] EOutgameTutorialAction action;

    [Tooltip("안내 타깃 위젯. 액션에 따라 누를 대상이거나 강조만 할 영역이다")]
    [SerializeField] EOutgameTutorialAnchor anchor;

    [Tooltip("누를 타깃과 **함께** 딤 위로 올려 보여 줄 영역(눌리지는 않는다 — 클릭은 위 anchor 하나로만 끝난다).\n"
           + "\"이걸 보고 저걸 눌러라\"가 성립해야 하는 자리에 쓴다 — 예: 완성된 덱 6칸을 보여 주며 전투 버튼을 누르게 한다.\n"
           + "영역 안에 카드가 있으면 영역째가 아니라 카드만 올라온다(패널 프레임·수치가 딸려 올라오면 무엇을 보라는지 흐려진다).\n"
           + "· 비우면(None) 종전대로 타깃만 올라온다.\n"
           + "· 아직 등록되지 않은 영역을 골라도 진행은 막지 않는다 — 강조 없이 그대로 흐른다.\n"
           + "⚠ 스크롤 안쪽 영역은 고르지 마라 — 딤 위로 끌어올리면 뷰포트 잘라내기가 끊겨 내용이 화면 밖으로 샌다.\n"
           + "⚠ 내용이 도중에 갱신되는 영역도 지목하지 마라 — 승격은 첫 프레임에 한 번만 걸려, 그 뒤 새로 그려진 것은 딤 아래에 남는다")]
    [SerializeField] EOutgameTutorialAnchor spotlight;

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

    [Tooltip("WaitPurchase: 상점 진열·판매 대상 / AutoPurchase: 자동 구매할 팩 / DeckAutoEquip: 자동 편성이 채울 풀 / "
           + "PackNotice: 예고 팝업에 세울 팩(아트·이름만 읽는다 — 지급도 구매도 하지 않는다) / "
           + "DeckGrant·CardGrant·CardSetGrant: 무엇을 줄지의 정본. 서버가 이 팩의 카드 전량을 확정 지급한다.\n"
           + "⚠ 지급 팩은 가격이 0이어야 한다 — 값이 붙은 팩은 서버가 거절해 화면만 서고 소유는 늘지 않는다.\n"
           + "⚠ 아래 카드 저작(cardId·cardIds)과 시나리오 덱은 화면에 무엇을 그릴지만 정한다 — 실지급은 이 팩이 정한다")]
    [SerializeField] CardPackData pack;

    [Tooltip("이 스텝 동안 상점 가격 자리에 대신 띄울 문구(예: \"무료\"). 비우면 팩의 실제 가격이 숫자로 나온다.\n"
           + "문구를 넣으면 재화 아이콘도 함께 숨는다 — 값을 치르는 물건이 아니라고 말하는 자리이기 때문이다.\n"
           + "⚠ 표기만 바꾼다. 실제 결제는 팩 SO의 price가 그대로 한다 — 무료로 보이게 하려면 그 팩의 가격이 0이어야 한다")]
    [SerializeField] string packPriceLabel;

    [Tooltip("도감 앵커(테마 칸·카드 칸)가 **어느 것**을 가리킬지. 앵커 키는 자리 종류만 말하고 도감엔 그 자리가 여럿이라, "
           + "이 카드가 든 테마·칸을 화면이 찾아 지목한다.\n"
           + "비우면 화면이 대신 고른다 — 아직 안 꽂은 카드가 있는 첫 테마 / 그 페이지의 첫 소유 칸.\n"
           + "⚠ 유저가 그 카드를 아직 소유하지 않았으면 가리킬 칸이 없어 폴백으로 떨어진다(안내는 멈추지 않는다)")]
    [SerializeField, CardId] int anchorCardId;

    [Tooltip("이 스텝이 시키는 성장 한 방을 안내가 대신 내준다(값 0).\n"
           + "표시·활성 판정·실제 소모가 모두 이 값을 함께 본다 — 화면엔 100골드가 뜨는데 0이 나가는 일은 없다.\n"
           + "성공하면 그 자리에서 소진된다(실패는 소진하지 않는다 — 안내가 시킨 강화를 유저 돈으로 다시 하게 두지 않는다).\n"
           + "다음 스텝으로 넘어가면 저절로 풀린다")]
    [SerializeField] bool freeOfCharge;

    [Tooltip("이 스텝을 끝내는 것이 강화 \"성공\"이 아니라, 그 성공이 연 해금 연출이 **모두 끝난 순간**이 된다.\n"
           + "(잠금판이 걷히고 → 내용이 들어오고 → 전면 해금 안내까지 닫히는 한 줄이다)\n"
           + "켜면 강화 결과판을 유저 탭 대신 안내가 잠깐 뒤 대신 걷는다 — 무대를 돌려줘야 해금 연출이 설 자리가 생긴다.\n"
           + "이번 강화로 열릴 것이 하나도 없으면 기다리지 않고 종전처럼 곧장 넘어간다.\n"
           + "⚠ 대상 카드의 keywordUnlockLevel에 실제로 걸리는 강화여야 의미가 있다 — 아니면 켜도 아무 차이가 없다")]
    [SerializeField] bool waitUnlockIntro;

    [Tooltip("CardGrant·CardSetGrant: 보상 화면에 띄울 제목. 비우면 기본 문구를 쓴다")]
    [SerializeField] string rewardTitle;

    [Tooltip("획득 연출(카드가 도감 탭으로 빨려드는 비행)이 끝나기를 기다리지 않고 다음 스텝으로 넘어간다.\n"
           + "뒤이을 안내가 그 비행과 나란히 서서, 흡수가 다 끝난 뒤에야 손가락이 뜨는 빈 구간이 사라진다.\n"
           + "⚠ 다음 스텝이 또 보상 화면을 세우는 자리에는 켜지 마라 — 아직 날고 있는 카드 위를 그 화면이 덮는다")]
    [SerializeField] bool parallelGain;

    [Tooltip("BattleEntry·AutoBattle: 전투에 넘길 시나리오 / DeckGrant: 지급할 덱의 정본")]
    [SerializeField] TutorialScenarioData scenario;

    [Tooltip("CardGrant: 지급할 카드 한 장. 이미 소유한 카드를 꽂아도 안전하지만 획득 연출은 그대로 돈다")]
    [SerializeField, CardId] int cardId;

    [Tooltip("CardSetGrant: 한 묶음으로 지급할 카드들. 순서 = 패널 격자에 놓이는 순서.\n"
           + "· 이미 소유한 카드를 넣어도 안전하다. 다만 획득 연출은 그대로 돈다(중복 표시 없이 새 카드처럼 보인다).\n"
           + "· 빈 칸(None)은 건너뛴다 — 격자 자리도 차지하지 않는다.\n"
           + "· 소유권만 준다. 덱에는 편성되지 않는다(덱 저작은 DeckGrant 몫이다).")]
    [SerializeField, CardId] List<int> cardIds = new List<int>();

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
           + "⚠ 실패 분기가 있는 액션(AutoPurchase·DeckGrant·CardGrant·CardSetGrant·PackNotice)에서만 뜬다. "
           + "다른 액션은 실패해도 이 값을 보지 않는다")]
    [SerializeField] EOutgameTutorialFailure onFailure;

    // 세이브가 이 스텝을 지목하는 불변 번호(0 = 미부여). 좌표는 런타임 커서일 뿐 세이브의 앵커는 이것이다.
    public int StepId => stepId;

    public EOutgameTutorialAction Action => action;

#if UNITY_EDITOR
    // 부여 도구 전용(OutgameTutorialData.AssignMissingStepIds). 런타임에서 번호를 바꿀 일은 없다.
    public void SetStepIdForEditor(int _id) => stepId = _id;
#endif

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

    public int CardId => cardId;

    // 안내가 지목한 카드(그 자리를 카드로 고르는 앵커에서만 — 아니면 저작값이 남아 있어도 없는 것으로 본다)
    public int AnchorCardId => UsesAnchorCard(Anchor) ? anchorCardId : 0;

    // 이 스텝이 시키는 성장 한 방이 무료인가(값 저작이 없는 액션은 저작값이 남아 있어도 유료로 본다)
    public bool FreeOfCharge => UsesFreeOfCharge(action) && freeOfCharge;

    // 강화 성공이 연 해금 연출까지 기다리는가(연출을 트지 않는 액션은 저작값이 남아 있어도 없는 것으로 본다)
    public bool WaitUnlockIntro => UsesWaitUnlockIntro(action) && waitUnlockIntro;

    // 보상 화면 제목(비우면 호출자가 기본 문구를 쓴다)
    public string RewardTitle => UsesRewardTitle(action) ? rewardTitle : null;

    // 획득 연출의 종료를 기다리지 않고 넘어가는가(지급 액션이 아니면 저작값이 남아 있어도 없는 것으로 본다)
    public bool ParallelGain => UsesParallelGain(action) && parallelGain;

    public IReadOnlyList<int> CardIds => cardIds;

    public bool ShowDeckGate => showDeckGate;

    public string DeckName => deckName;

    // 실패했을 때의 결말(실패 분기가 없는 액션은 저작값이 남아 있어도 Skip으로 본다)
    public EOutgameTutorialFailure OnFailure => UsesFailurePolicy(action) ? onFailure : EOutgameTutorialFailure.Skip;

    // 안내 타깃(앵커를 쓰지 않는 액션은 저작값이 남아 있어도 None으로 본다)
    public EOutgameTutorialAnchor Anchor => UsesAnchor(action) ? anchor : EOutgameTutorialAnchor.None;

    // 타깃과 함께 밝힐 영역(누르는 자리가 아니다 — 강조를 얹지 않는 액션은 저작값이 남아 있어도 None으로 본다)
    public EOutgameTutorialAnchor Spotlight => UsesSpotlight(action) ? spotlight : EOutgameTutorialAnchor.None;

    // 이 액션의 메타(완료 조건·씬 이탈·저작 필드) — 아래 파생값의 유일한 출처다.
    TutorialActionMeta Meta => TutorialActionMeta.Of(action);

    // 무엇이 이 스텝을 완료시키는가(액션에서 파생)
    public EOutgameTutorialCompletion Completion => Meta.Completion;

    // 완료 뒤 이 씬에서 이어 걸 스텝이 없다(씬 전환·전투가 화면을 넘겨받는 경우).
    // 덱 게이트를 켠 전투 진입만 예외다 — 그때는 덱 화면이 같은 씬에 서므로 안내를 이어 걸어야 한다.
    // 테이블로 접히지 않는 유일한 파생값이다(액션이 아니라 이 행의 저작값에 달렸다).
    public bool LeavesScene => Meta.LeavesScene
                            && !(action == EOutgameTutorialAction.BattleEntry && showDeckGate);


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
    public bool TryGetForcedDeck(out IReadOnlyList<int> _cardIds)
    {
        _cardIds = action == EOutgameTutorialAction.DeckAutoEquip && pack != null && pack.PoolCount > 0
            ? pack.Pool
            : null;

        return _cardIds != null;
    }

    // ── 액션별 저작 필드 ────────────────────────────────────────────────────
    // 답은 전부 TutorialActionMeta의 테이블 한 곳에서 나온다. 술어 이름을 남겨 둔 이유는
    // 호출부(드로어·실행기·화면 6파일)가 이 이름으로 묻기 때문이다 — 판정은 여기서 하지 않는다.

    // 이 액션이 앵커를 쓰는가(런타임 판정과 드로어의 필드 노출이 공유)
    public static bool UsesAnchor(EOutgameTutorialAction _action) => Uses(_action, EStepField.Anchor);

    // 이 액션이 타깃 외에 "읽을 영역"을 함께 밝히는가(딤 위로 올릴 딤이 있는 클릭 게이트만 — 설명 스텝은 앵커 자체가 그 영역이다).
    // 다른 Uses*와 달리 밖에서 묻는 곳이 없다 — 노출 목록은 드로어의 표가, 검증은 위 게터가 답한다.
    static bool UsesSpotlight(EOutgameTutorialAction _action) => Uses(_action, EStepField.Spotlight);

    // 이 액션이 안내 문구를 띄우는가(자동 스텝은 화면에 아무것도 그리지 않는다)
    public static bool ShowsGuideMessage(EOutgameTutorialAction _action) => Uses(_action, EStepField.GuideMessage);

    // 이 액션이 딤을 걸 수 있는가(개봉 대기는 문구만 띄운다 — 딤이 스와이프 제스처를 삼킨다)
    public static bool UsesDim(EOutgameTutorialAction _action) => Uses(_action, EStepField.Dim);

    // 이 액션이 문구 자리를 저작하는가(딤 탭으로 넘기는 설명 스텝뿐 — 나머지는 타깃을 피해 자리가 정해진다)
    public static bool UsesMessagePlacement(EOutgameTutorialAction _action) => Uses(_action, EStepField.MessagePlacement);

    // 이 앵커가 "그 자리 중 어느 것"까지 저작받아야 하는가.
    // 도감은 같은 종류의 자리가 여럿이라 키만으로는 대상이 정해지지 않는다(버튼 하나짜리 앵커는 물을 것이 없다).
    // 축이 액션이 아니라 앵커라 테이블 밖에 남는다.
    public static bool UsesAnchorCard(EOutgameTutorialAnchor _anchor)
        => _anchor == EOutgameTutorialAnchor.AlbumThemeCell
        || _anchor == EOutgameTutorialAnchor.AlbumCardSlot
        || _anchor == EOutgameTutorialAnchor.DeckEditCollectionCard;

    // 이 액션이 값을 무는가(안내가 대신 내줄 수 있는 자리 = 성장 한 방을 시키는 스텝)
    public static bool UsesFreeOfCharge(EOutgameTutorialAction _action) => Uses(_action, EStepField.FreeOfCharge);

    // 이 액션이 해금 연출을 여는가(카드 강화만 — 키워드 강화는 잠금판을 여는 자리가 아니다)
    public static bool UsesWaitUnlockIntro(EOutgameTutorialAction _action) => Uses(_action, EStepField.WaitUnlockIntro);

    // 이 액션이 보상 화면을 세우는가(예고 팝업도 같은 자리에 제목을 쓴다)
    public static bool UsesRewardTitle(EOutgameTutorialAction _action) => Uses(_action, EStepField.RewardTitle);

    // 이 액션이 획득 연출을 트는가(그 연출을 기다릴지 말지를 저작받는 자리)
    public static bool UsesParallelGain(EOutgameTutorialAction _action) => Uses(_action, EStepField.ParallelGain);

    // 이 액션이 팩을 쓰는가(진열 고정·자동 구매·자동 편성 풀·예고·지급 목록의 정본)
    public static bool UsesPack(EOutgameTutorialAction _action) => Uses(_action, EStepField.Pack);

    // 이 액션이 가격 표기 문구를 쓰는가(상점 진열을 덮어쓰는 액션만 — 화면에 가격 자리가 있는 경우다)
    public static bool UsesPackPriceLabel(EOutgameTutorialAction _action) => Uses(_action, EStepField.PackPriceLabel);

    // 이 액션이 시나리오를 쓰는가(전투 주입 또는 덱 정본)
    public static bool UsesScenario(EOutgameTutorialAction _action) => Uses(_action, EStepField.Scenario);

    // 이 액션이 덱 게이트 노출을 정하는가(전투에 넣는 액션만)
    public static bool UsesShowDeckGate(EOutgameTutorialAction _action) => Uses(_action, EStepField.ShowDeckGate);

    // 이 액션이 덱 이름을 쓰는가
    public static bool UsesDeckName(EOutgameTutorialAction _action) => Uses(_action, EStepField.DeckName);

    // 이 액션이 실패 정책을 쓰는가 — 실행기가 실제로 실패 분기를 갖는 액션만.
    // 대기형은 실패 개념이 없고, 전투 진입 계열은 시나리오가 비어도 일반 전투로 그냥 들어간다(실패로 치지 않는다).
    public static bool UsesFailurePolicy(EOutgameTutorialAction _action) => Uses(_action, EStepField.FailurePolicy);

    // 이 액션이 카드 한 장을 쓰는가(지급 대상)
    public static bool UsesCard(EOutgameTutorialAction _action) => Uses(_action, EStepField.Card);

    // 이 액션이 카드 묶음을 쓰는가(한 번에 지급하는 세트)
    public static bool UsesCards(EOutgameTutorialAction _action) => Uses(_action, EStepField.Cards);

    /// <summary>이 액션이 저작받는 필드 축의 목록. 드로어가 노출 목록을 만들 때 이 값을 순회한다
    /// — 술어를 하나씩 다시 부르지 않아야 새 축을 늘려도 드로어를 고칠 일이 없다.</summary>
    public static EStepField FieldsOf(EOutgameTutorialAction _action) => TutorialActionMeta.Of(_action).Fields;

    static bool Uses(EOutgameTutorialAction _action, EStepField _field) => TutorialActionMeta.Of(_action).Uses(_field);
}
