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
    Spotlight        = 1 << 16,
}

/// <summary>스텝 한 행이 비트(유저가 겪는 사건 하나) 안에서 맡는 자리.
/// 한 줄 = 한 액션이라는 데이터 모양은 그대로 두고, "무엇이 사건이고 무엇이 그 사건에 딸린 잡일인가"만
/// 액션에서 파생시킨다 — 저작 도구가 평평한 목록을 사람이 읽는 순서로 접는 근거다.
/// 지금 이 값을 읽는 것은 저작 도구뿐이다(런타임 실행 순서는 접기와 무관하게 행 순서 그대로다).</summary>
public enum EBeatSlot
{
    Beat = 0,   // 유저가 겪는 사건. 비트의 머리이자 목록에 서는 줄이다
    Pre,        // 사건이 서기 전에 무대를 갖추는 일(지급·자동 구매·연출 진입) — 뒤따르는 사건에 매달린다
    Post,       // 사건이 남긴 것을 치우는 일(오버레이 닫기·연출 종료 대기) — 앞선 사건에 매달린다
}

/// <summary>액션 하나가 "무엇인가"를 답하는 단일 테이블 — 완료 조건 · 씬 이탈 · 저작 필드 · 비트 자리가 한 줄에 모여 있다.
///
/// 이 테이블이 있기 전에는 같은 질문이 다섯 곳(Completion 스위치 · LeavesScene 스위치 · Uses* 술어 15종 ·
/// TutorialStepExecutor.Enter · TutorialStepDefDrawer.VisibleFields)에 복제돼 있어서, 액션 하나를 추가하려면
/// 코드 5~10파일을 동시에 고쳐야 했고 하나를 빠뜨려도 컴파일은 통과했다(런타임에 조용히 멈춘다).
///
/// 새 액션을 추가하는 법: EOutgameTutorialAction 끝에 값을 더하고, 이 테이블 끝에 그 행을 더하고,
/// 자동 실행이면 TutorialStepExecutor.Enter에 case를 더한다. 그게 전부다 — 드로어는 손대지 않는다.
/// (유저가 겪는 사건이 아니라 그 앞뒤의 잡일이면 _beatSlot도 함께 지정한다 — 기본은 사건이다.)</summary>
public readonly struct TutorialActionMeta
{
    public readonly EOutgameTutorialAction Action;         // 자기 행의 액션(테이블 정렬 검증용 — 아래 static 생성자)
    public readonly C                      Completion;
    public readonly F                      Fields;
    public readonly bool                   LeavesScene;    // 진입만으로 이 씬이 끝나는가(전투·씬 전환이 화면을 넘겨받는다)
    public readonly bool                   GrantsPackPool; // 실플레이에서 이 액션이 팩 카드의 소유를 만드는가(되감기 재생 조건)
    public readonly EBeatSlot              BeatSlot;       // 비트 안에서 이 행이 맡는 자리(사건인가, 그 앞뒤의 잡일인가)

    public TutorialActionMeta(EOutgameTutorialAction _action, C _completion, F _fields,
                              bool _leavesScene = false, bool _grantsPackPool = false,
                              EBeatSlot _beatSlot = EBeatSlot.Beat)
    {
        Action         = _action;
        Completion     = _completion;
        Fields         = _fields;
        LeavesScene    = _leavesScene;
        GrantsPackPool = _grantsPackPool;
        BeatSlot       = _beatSlot;
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
        new(A.WaitClick,            C.Click,            F.Anchor | F.GuideMessage | F.Dim | F.Spotlight),
        new(A.Message,              C.Confirm,          F.Anchor | F.GuideMessage | F.Dim | F.MessagePlacement | F.Spotlight),
        new(A.WaitPurchase,         C.Purchase,         F.Anchor | F.GuideMessage | F.Dim | F.Spotlight | F.Pack | F.PackPriceLabel, false, true),
        new(A.WaitPackOpen,         C.PackOpen,         F.GuideMessage),
        new(A.DeckAutoEquip,        C.Click,            F.Anchor | F.GuideMessage | F.Dim | F.Spotlight | F.Pack),
        new(A.BattleEntry,          C.Click,            F.Anchor | F.GuideMessage | F.Dim | F.Spotlight | F.Scenario | F.ShowDeckGate, true),
        new(A.BattleStart,          C.Click,            F.Anchor | F.GuideMessage | F.Dim | F.Spotlight, true),
        new(A.AutoBattle,           C.Auto,             F.Scenario | F.ShowDeckGate, true),
        new(A.AutoPurchase,         C.Auto,             F.Pack | F.FailurePolicy, false, true, EBeatSlot.Pre),
        new(A.DeckGrant,            C.Auto,             F.Pack | F.Scenario | F.DeckName | F.FailurePolicy, _beatSlot: EBeatSlot.Pre),
        new(A.WaitAlbumInsert,      C.AlbumInsert,      F.None, _beatSlot: EBeatSlot.Post),
        new(A.WaitEnhance,          C.Enhance,          F.Anchor | F.GuideMessage | F.Dim | F.Spotlight | F.FreeOfCharge | F.WaitUnlockIntro),
        new(A.CloseCardDetail,      C.Auto,             F.None, _beatSlot: EBeatSlot.Post),
        new(A.EnterFirstRank,       C.RankEffect,       F.None, _beatSlot: EBeatSlot.Pre),
        new(A.WaitLobbyReturn,      C.LobbyReturn,      F.None, _beatSlot: EBeatSlot.Post),
        new(A.CardGrant,            C.CardGain,         F.RewardTitle | F.ParallelGain | F.Pack | F.FailurePolicy | F.Card, _beatSlot: EBeatSlot.Pre),
        new(A.WaitCardDetailReturn, C.CardDetailReturn, F.None, _beatSlot: EBeatSlot.Post),
        new(A.CardSetGrant,         C.CardGain,         F.RewardTitle | F.ParallelGain | F.Pack | F.FailurePolicy | F.Cards, _beatSlot: EBeatSlot.Pre),
        new(A.WaitKeywordEnhance,   C.KeywordEnhance,   F.Anchor | F.GuideMessage | F.Dim | F.Spotlight | F.FreeOfCharge),
        new(A.PackNotice,           C.CardGain,         F.RewardTitle | F.ParallelGain | F.Pack | F.FailurePolicy),
        new(A.CloseAlbumPage,       C.Auto,             F.None, _beatSlot: EBeatSlot.Post),
        // 장착으로 슬롯 내용이 갱신되는 스텝이라 F.Spotlight를 주지 않는다 — 승격은 첫 프레임에 한 번만 걸린다.
        new(A.WaitDeckEquip,        C.DeckEquip,        F.Anchor | F.GuideMessage | F.Dim),
        // 저장 버튼은 바꾼 게 없으면 잠긴다 — 누른 순간이 아니라 저장이 확정된 순간이 완료다(WaitEnhance와 같은 규약).
        new(A.WaitDeckSave,         C.DeckSave,         F.Anchor | F.GuideMessage | F.Dim | F.Spotlight),
        new(A.CloseDeckEdit,        C.Auto,             F.None, _beatSlot: EBeatSlot.Post),
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
