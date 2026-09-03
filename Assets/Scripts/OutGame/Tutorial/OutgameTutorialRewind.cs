using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>디버그 되감기 예약의 단일 창구 — "다음 초기화에 세이브를 밀고 이 좌표에서 시작한다".
///
/// 예약을 <b>PlayerPrefs</b>에 두는 이유: 에디터 창(정지 상태)이 쓰고 런타임(초기화)이 읽어야 하는데
/// EditorPrefs는 런타임이 못 읽고, 세이브 스키마에 디버그 필드를 넣는 것은 오염이다.
///
/// 적용이 2단인 이유: 밀기는 클라우드 채택 뒤이면서 매니저들이 슬롯을 캐싱하기 <b>전</b>(InstallSaveDependent 맨 앞)이어야 하고,
/// 지급 재생은 카탈로그·덱·시퀀스가 전부 준비된 <b>뒤</b>(초기화(InitializationRunner) 끝)여야 한다.
/// 밀기가 서버 왕복이 된 뒤에도 이 경계는 그대로다 — 왕복이 끝나야 매니저 Init()들이 밀린 슬롯을 읽는다.
/// </summary>
public static class OutgameTutorialRewind
{
    const string PREF_KEY = "outgame.tutorial.rewind";

    // 1단이 끝났음을 넘겨받는 키. 밀기와 지급 재생 사이에서 초기화가 끊기면(예외·수동 정지) 예약이 남아
    // 다음 초기화가 세이브를 또 민다 — 그 사이에 유저가 플레이했다면 그 진행이 통째로 날아간다.
    // 1단이 예약을 이 키로 옮겨 두면 다음 초기화는 밀기 없이 재생만 마저 한다.
    const string PREF_REPLAY_KEY = "outgame.tutorial.rewind.replay";

    /// <summary>초기화가 아직 처리하지 않은 예약 좌표. 밀기가 이미 끝났으면(1단 소비 후) 재생 대기 좌표를 답한다 —
    /// 그 상태를 에디터 창이 못 보면 취소할 방법이 없어져, 유저가 한참 진행한 뒤 다음 초기화가 그 위에 지급을 덧씌운다.
    /// <paramref name="_wipePending"/>은 "세이브를 미는 단계가 아직 남았는가"로, 배너가 둘을 갈라 보여 준다.</summary>
    public static bool TryGetScheduled(out int _chapter, out int _step, out bool _wipePending)
    {
        _wipePending = true;
        if (TryGetCoord(PREF_KEY, out _chapter, out _step)) return true;

        _wipePending = false;
        return TryGetCoord(PREF_REPLAY_KEY, out _chapter, out _step);
    }

    static bool TryGetCoord(string _key, out int _chapter, out int _step)
    {
        _chapter = 0;
        _step    = 0;

        string t_raw = LocalPrefs.GetString(_key, string.Empty);
        if (string.IsNullOrEmpty(t_raw)) return false;

        string[] t_parts = t_raw.Split(',');
        if (t_parts.Length != 2) return false;

        return int.TryParse(t_parts[0], out _chapter) && int.TryParse(t_parts[1], out _step);
    }

    // 되감기 예약(다음 초기화에 1회 소비). 음수 좌표는 0으로 본다.
    public static void Schedule(int _chapter, int _step)
    {
        int t_chapter = Mathf.Max(0, _chapter);
        int t_step    = Mathf.Max(0, _step);

        LocalPrefs.SetString(PREF_KEY, $"{t_chapter},{t_step}");
        LocalPrefs.Save();
    }

    public static void Cancel()
    {
        LocalPrefs.DeleteKey(PREF_KEY);
        LocalPrefs.DeleteKey(PREF_REPLAY_KEY);
        LocalPrefs.Save();
    }

    /// <summary>1단 — 서버에 아웃게임 세이브 초기화를 요청하고 예약 좌표를 심는다.
    /// <b>SaveDependentManagersStep.InstallOnce() 맨 앞</b>에서만 호출한다 —
    /// 클라우드 채택보다 앞서면 채택이 슬롯을 그대로 덮고, 매니저 Init()들이 슬롯 참조를 캐싱한 뒤면 반영되지 않는다.</summary>
    // 미는 주체가 서버인 이유는 진행도의 단조성이다 — 클라가 세이브를 뒤로 쓸 수 있으면 보상 수령 표식을 비워 재수령이 뚫린다.
    public static async UniTask ApplyWipeIfScheduled()
    {
        // 밀기는 새 예약(PREF_KEY)에만 반응한다 — 재생만 남은 상태를 여기서 다시 집으면 매 초기화 반복 와이프다.
        if (!TryGetCoord(PREF_KEY, out int t_chapter, out int t_step)) return;

        try
        {
            // 슬롯 9개 채택은 이 창구가 한다(AdoptServerResult → AdoptServerSlots) — 클라가 따로 쓰거나 저장하지 않는다.
            // InvokeInitAsync는 Loading을 단언하는데 이 시점은 이미 Ready라 쓸 수 없다.
            await ServerSaveCommands.InvokeAsync<ServerCommandResult>(
                "devRewindTutorial",
                new
                {
                    env          = ContentProfileConfig.Active.CloudEnvId,
                    chapterIndex = t_chapter,
                    stepIndex    = t_step,
                });
        }
        catch (Exception t_exception)
        {
            // QA 경로라 실패가 게임 진입을 막으면 안 된다. 예약을 남기면 매 부팅이 같은 왕복을 반복하면서 원인은 안 보이므로
            // 걷어내고 로그만 남긴다 — QA가 에디터 창에서 다시 예약하면 된다.
            Cancel();
            Debug.LogError($"[TutorialRewind] 서버 되감기 실패 — 예약을 취소했다. 좌표 {t_chapter}-{t_step} · {t_exception.GetBaseException().Message}");
            return;
        }

        // 세이브 슬롯 밖에 사는 진행 흔적은 서버가 모른다 — 슬롯만 밀면 그 축이 이전 세션 값으로 남아
        // "되감았는데 그 연출만 안 나온다"가 된다(해금 연출 이력은 기기 로컬이라 슬롯 목록에 없다).
        AdventureUnlockSeenStore.Clear();

        // 밀기는 끝났다 — 예약을 재생 전용 키로 옮겨 다음 초기화가 세이브를 다시 밀지 않게 한다.
        LocalPrefs.DeleteKey(PREF_KEY);
        LocalPrefs.SetString(PREF_REPLAY_KEY, $"{t_chapter},{t_step}");
        LocalPrefs.Save();

        // 정지 판정은 세이브 밖(static)이라 슬롯을 갈아도 남는다. 보통은 다음 초기화의 도메인 리로드가
        // 알아서 내리므로 no-op이고, 리로드를 끈 에디터 세션에서만 실효가 있다 — 그 한 경우를 위한 방어다.
        OutgameFeatureLock.ClearStall();

        // 잔액은 되돌지 않는다 — 지갑은 세이브 문서 밖이고 되돌리는 경로가 서버에도 없다. test env 전용이라 그 차이를 수용한다.
        Debug.Log($"[TutorialRewind] 서버 세이브 초기화 — 좌표 {t_chapter}-{t_step}로 되감음(슬롯 전체 첫실행 · 해금 연출 이력 · 정지 판정 해제 · 잔액 유지).");
    }

    /// <summary>2단 — 예약 좌표 직전까지의 <b>결정적인</b> 지급만 재생하고 예약을 소비한다.
    /// <b>초기화 즉시 단계 끝(TutorialDataStep 뒤)</b>에서 호출한다(카탈로그·덱·성장·시퀀스가 모두 준비된 자리).
    ///
    /// 씬을 뺏는 액션(AutoBattle·AutoPurchase·BattleEntry)은 실행하지 않는다 — 초기화 중에 화면을 넘겨받는다.
    /// 팩 드로우는 랜덤이라 재현할 수 없어 풀 전량을 준다.</summary>
    public static void ApplyReplayIfScheduled()
    {
        // 1단이 넘겨준 좌표를 읽는다. 1단을 건너뛴 초기화(예약 없음)라면 재생할 것도 없다.
        if (!TryGetCoord(PREF_REPLAY_KEY, out int t_chapter, out int t_step)) return;

        Cancel();   // 1회 소비 — 재생이 실패해도 다음 초기화가 같은 지급을 겹쳐 주지 않게 먼저 걷는다.

        int t_decks = 0;
        int t_cards = 0;

        for (int t_c = 0; t_c <= t_chapter && t_c < OutgameTutorialRunner.ChapterCount; t_c++)
        {
            int t_count = OutgameTutorialRunner.StepCountOf(t_c);
            int t_end   = t_c < t_chapter ? t_count : Mathf.Min(t_step, t_count);

            for (int t_s = 0; t_s < t_end; t_s++)
            {
                if (!OutgameTutorialRunner.TryGetStepAt(t_c, t_s, out var t_row)) continue;

                // 덱 지급은 순수 세이브 작업이라 그대로 재생할 수 있다. sink를 비워 좌표 커밋·졸업 낙인만 무력화한다.
                if (t_row.Action == EOutgameTutorialAction.DeckGrant)
                {
                    TutorialStepExecutor.Enter(t_row, new OutgameTutorialStepContext(t_c, t_s, t_c, t_s, false, null));
                    t_decks++;
                    continue;
                }

                // 보상 카드도 순수 세이브 작업이다 — 초기화 중에는 연출을 세울 무대가 없으니 소유권만 준다.
                if (t_row.Action == EOutgameTutorialAction.CardGrant)
                {
                    if (t_row.CardId > 0 && OwnershipManager.Grant(t_row.CardId)) t_cards++;
                    continue;
                }

                if (t_row.Action == EOutgameTutorialAction.CardSetGrant)
                {
                    t_cards += GrantCardSet(t_row.CardIds);
                    continue;
                }


                // 조건은 "팩 필드를 저작하는가"(UsesPack)가 아니라 "실제로 소유가 생기는가"다 — PackNotice는 팩을
                // 가리키기만 하고 아무것도 주지 않는데, 전자로 물으면 그 풀 전량이 딸려 와 열지도 않은 팩의 카드를
                // 이미 가진 채 개봉 스텝에 들어선다. 답은 액션 테이블이 갖고 있다.
                if (!string.IsNullOrEmpty(t_row.PackId) && TutorialActionMeta.Of(t_row.Action).GrantsPackPool)
                    t_cards += GrantPackPool(t_row.PackId);
            }
        }

        Debug.Log($"[TutorialRewind] 좌표 {t_chapter}-{t_step}까지 지급 재생 — 덱 스텝 {t_decks}개 / 팩 풀 카드 {t_cards}장 · 소유 {OwnershipManager.OwnedCount}장");
    }

    static int GrantCardSet(IReadOnlyList<int> _cardIds)
    {
        return _cardIds == null ? 0 : OwnershipManager.GrantAll(_cardIds);
    }

    static int GrantPackPool(string _packId)
    {
        var t_pool = PackSpec.ResolveCardIds(_packId, RankManager.CurrentGrade);
        if (t_pool == null || t_pool.Count == 0) return 0;

        return OwnershipManager.GrantAll(t_pool);
    }
}
