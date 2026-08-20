using System;
using UnityEngine;
using A = EOutgameTutorialAction;
using C = EOutgameTutorialCompletion;
using F = EStepField;

// 스텝 행이 저작받는 값의 축. 액션마다 "이 중 무엇을 쓰는가"가 다르고, 그 답이 곧 인스펙터 노출 목록이자
// 런타임 게터의 통과 조건이다. 축을 늘리면 여기와 테이블 한 줄만 늘어난다.
[Flags]
public enum EStepField
{
    None             = 0,
    Anchor           = 1 << 0,
    GuideMessage     = 1 << 1,
    Dim              = 1 << 2,
    MessagePlacement = 1 << 3,
    FreeOfCharge     = 1 << 4,
    WaitUnlockIntro  = 1 << 5,
    RewardTitle      = 1 << 6,
    ParallelGain     = 1 << 7,
    Pack             = 1 << 8,
    PackPriceLabel   = 1 << 9,
    Scenario         = 1 << 10,
    ShowDeckGate     = 1 << 11,
    DeckName         = 1 << 12,
    FailurePolicy    = 1 << 13,
    Card             = 1 << 14,
    Cards            = 1 << 15,
}

/// <summary>액션 하나가 "무엇인가"를 답하는 단일 테이블 — 완료 조건 · 씬 이탈 · 저작 필드가 한 줄에 모여 있다.
///
/// 이 테이블이 있기 전에는 같은 질문이 다섯 곳(Completion 스위치 · LeavesScene 스위치 · Uses* 술어 15종 ·
/// TutorialStepExecutor.Enter · TutorialStepDefDrawer.VisibleFields)에 복제돼 있어서, 액션 하나를 추가하려면
/// 코드 5~10파일을 동시에 고쳐야 했고 하나를 빠뜨려도 컴파일은 통과했다(런타임에 조용히 멈춘다).
///
/// 새 액션을 추가하는 법: EOutgameTutorialAction 끝에 값을 더하고, 이 테이블 끝에 그 행을 더하고,
/// 자동 실행이면 TutorialStepExecutor.Enter에 case를 더한다. 그게 전부다 — 드로어는 손대지 않는다.</summary>
public readonly struct TutorialActionMeta
{
    public readonly EOutgameTutorialAction Action;         // 자기 행의 액션(테이블 정렬 검증용 — 아래 static 생성자)
    public readonly C                      Completion;
    public readonly F                      Fields;
    public readonly bool                   LeavesScene;    // 진입만으로 이 씬이 끝나는가(전투·씬 전환이 화면을 넘겨받는다)
    public readonly bool                   GrantsPackPool; // 실플레이에서 이 액션이 팩 카드의 소유를 만드는가(되감기 재생 조건)

    public TutorialActionMeta(EOutgameTutorialAction _action, C _completion, F _fields,
                              bool _leavesScene = false, bool _grantsPackPool = false)
    {
        Action         = _action;
        Completion     = _completion;
        Fields         = _fields;
        LeavesScene    = _leavesScene;
        GrantsPackPool = _grantsPackPool;
    }

    public bool Uses(F _field) => (Fields & _field) != 0;

    /// <summary>액션의 메타. 테이블에 없는 값은 "아무 필드도 안 쓰는 자동 스텝"으로 본다(진행은 막지 않는다).
    /// 여기 닿는 것은 아래 static 생성자가 이미 오류로 잡은 뒤다.</summary>
    public static TutorialActionMeta Of(EOutgameTutorialAction _action)
    {
        int t_index = (int)_action;

        return t_index >= 0 && t_index < s_table.Length
            ? s_table[t_index]
            : new TutorialActionMeta(_action, C.Auto, F.None);
    }

    // 인덱스 = (int)EOutgameTutorialAction. 첫 칸의 액션 이름이 그 계약을 눈에 보이게 하고, static 생성자가 검증한다.
    static readonly TutorialActionMeta[] s_table =
    {
        new(A.WaitClick,            C.Click,            F.Anchor | F.GuideMessage | F.Dim),
        new(A.Message,              C.Confirm,          F.Anchor | F.GuideMessage | F.Dim | F.MessagePlacement),
        new(A.WaitPurchase,         C.Purchase,         F.Anchor | F.GuideMessage | F.Dim | F.Pack | F.PackPriceLabel, false, true),
        new(A.WaitPackOpen,         C.PackOpen,         F.GuideMessage),
        new(A.DeckAutoEquip,        C.Click,            F.Anchor | F.GuideMessage | F.Dim | F.Pack),
        new(A.BattleEntry,          C.Click,            F.Anchor | F.GuideMessage | F.Dim | F.Scenario | F.ShowDeckGate, true),
        new(A.BattleStart,          C.Click,            F.Anchor | F.GuideMessage | F.Dim, true),
        new(A.AutoBattle,           C.Auto,             F.Scenario | F.ShowDeckGate, true),
        new(A.AutoPurchase,         C.Auto,             F.Pack | F.FailurePolicy, false, true),
        new(A.DeckGrant,            C.Auto,             F.Scenario | F.DeckName | F.FailurePolicy),
        new(A.WaitAlbumInsert,      C.AlbumInsert,      F.None),
        new(A.WaitEnhance,          C.Enhance,          F.Anchor | F.GuideMessage | F.Dim | F.FreeOfCharge | F.WaitUnlockIntro),
        new(A.CloseCardDetail,      C.Auto,             F.None),
        new(A.EnterFirstRank,       C.RankEffect,       F.None),
        new(A.WaitLobbyReturn,      C.LobbyReturn,      F.None),
        new(A.CardGrant,            C.CardGain,         F.RewardTitle | F.ParallelGain | F.FailurePolicy | F.Card),
        new(A.WaitCardDetailReturn, C.CardDetailReturn, F.None),
        new(A.CardSetGrant,         C.CardGain,         F.RewardTitle | F.ParallelGain | F.FailurePolicy | F.Cards),
        new(A.WaitKeywordEnhance,   C.KeywordEnhance,   F.Anchor | F.GuideMessage | F.Dim | F.FreeOfCharge),
        new(A.PackNotice,           C.CardGain,         F.RewardTitle | F.ParallelGain | F.Pack | F.FailurePolicy),
        new(A.CloseAlbumPage,       C.Auto,             F.None),
    };

    // 이 구조의 조용한 실패 두 가지를 부팅 때 한 번 소리내어 잡는다.
    //  (1) 액션만 늘리고 행을 안 늘림 → 그 액션이 폴백으로 떨어진다.
    //  (2) 액션을 중간에 끼워 넣고 행은 끝에 붙임 → 개수는 맞는데 삽입 지점 이후가 통째로 한 칸씩 밀린다.
    //      행마다 자기 액션을 싣고 인덱스와 대조하는 것이 (2)를 잡는 유일한 방법이다.
    static TutorialActionMeta()
    {
        int t_actions = Enum.GetValues(typeof(EOutgameTutorialAction)).Length;
        if (t_actions != s_table.Length)
            Debug.LogError($"[TutorialActionMeta] 액션 {t_actions}개 / 테이블 {s_table.Length}행 — 새 액션의 행을 테이블에 추가하세요.");

        for (int t_i = 0; t_i < s_table.Length; t_i++)
        {
            if (s_table[t_i].Action == (EOutgameTutorialAction)t_i) continue;

            Debug.LogError($"[TutorialActionMeta] 테이블 {t_i}번 행이 {s_table[t_i].Action}입니다 — 행 순서가 액션 순서와 어긋났습니다(그 뒤 전부가 밀립니다).");
        }
    }
}
