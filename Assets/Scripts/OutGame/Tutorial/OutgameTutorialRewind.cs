using System.Collections.Generic;
using UnityEngine;

/// <summary>디버그 되감기 예약의 단일 창구 — "다음 부트에 세이브를 밀고 이 좌표에서 시작한다".
///
/// 예약을 <b>PlayerPrefs</b>에 두는 이유: 에디터 창(정지 상태)이 쓰고 런타임(부트)이 읽어야 하는데
/// EditorPrefs는 런타임이 못 읽고, 세이브 스키마에 디버그 필드를 넣는 것은 오염이다.
///
/// 적용이 2단인 이유: 밀기는 매니저들이 슬롯을 캐싱하기 <b>전</b>(GameManager.Boot)이어야 하고,
/// 지급 재생은 카탈로그·덱·시퀀스가 전부 준비된 <b>뒤</b>(BootInstaller 끝)여야 한다.
/// </summary>
public static class OutgameTutorialRewind
{
    const string PREF_KEY = "outgame.tutorial.rewind";

    public static bool IsScheduled => PlayerPrefs.HasKey(PREF_KEY);

    // 예약 좌표(없으면 false)
    public static bool TryGetScheduled(out int _chapter, out int _step)
    {
        _chapter = 0;
        _step    = 0;

        string t_raw = PlayerPrefs.GetString(PREF_KEY, string.Empty);
        if (string.IsNullOrEmpty(t_raw)) return false;

        string[] t_parts = t_raw.Split(',');
        if (t_parts.Length != 2) return false;

        return int.TryParse(t_parts[0], out _chapter) && int.TryParse(t_parts[1], out _step);
    }

    // 되감기 예약(다음 부트에 1회 소비). 음수 좌표는 0으로 본다.
    public static void Schedule(int _chapter, int _step)
    {
        int t_chapter = Mathf.Max(0, _chapter);
        int t_step    = Mathf.Max(0, _step);

        PlayerPrefs.SetString(PREF_KEY, $"{t_chapter},{t_step}");
        PlayerPrefs.Save();
    }

    public static void Cancel()
    {
        PlayerPrefs.DeleteKey(PREF_KEY);
        PlayerPrefs.Save();
    }

    /// <summary>1단 — 아웃게임 세이브를 첫실행으로 밀고 예약 좌표를 심는다.
    /// <b>GameManager.Boot의 DataSaveManager.Load() 직후</b>에만 호출한다 —
    /// 매니저 Init()들이 슬롯 참조를 캐싱하고 나면 갈아끼운 슬롯이 반영되지 않는다.</summary>
    public static void ApplyWipeIfScheduled()
    {
        if (!TryGetScheduled(out int t_chapter, out int t_step)) return;

        var t_data = DataSaveManager.Data;

        // 슬롯을 통째로 새 인스턴스로 — 첫실행 기본값이 곧 값 객체의 초기값이다(골드 100 등).
        t_data.currency    = new CurrencySaveData();
        t_data.ownership   = new OwnershipSaveData();
        t_data.deck        = new DeckSaveData();
        t_data.collection  = new CollectionSaveData();
        t_data.rank        = new RankSaveData();
        t_data.cardGrowth  = new CardGrowthSaveData();
        t_data.albumReward = new AlbumRewardSaveData();
        t_data.tutorial    = new TutorialSaveData();

        t_data.tutorial.outgameChapterIndex     = t_chapter;
        t_data.tutorial.outgameChapterStepIndex = t_step;

        // 레거시 판정은 이미 끝난 것으로 둔다 — 소유가 빈 세이브라 어차피 통과하지만,
        // 판정을 남겨 두면 "첫실행 마이그레이션"이 되감기마다 한 번씩 도는 잡음이 된다.
        t_data.tutorial.migrationChecked = true;

        DataSaveManager.Save();

        Debug.Log($"[TutorialRewind] 세이브 초기화 — 좌표 {t_chapter}-{t_step}로 되감음(소유·강화·재화·덱·랭크·도감보상 전부 첫실행).");
    }

    /// <summary>2단 — 예약 좌표 직전까지의 <b>결정적인</b> 지급만 재생하고 예약을 소비한다.
    /// <b>BootInstaller.Install() 끝</b>에서 호출한다(카탈로그·덱·성장·시퀀스가 모두 준비된 자리).
    ///
    /// 씬을 뺏는 액션(AutoBattle·AutoPurchase·BattleEntry)은 실행하지 않는다 — 부트 중에 화면을 넘겨받는다.
    /// 팩 드로우는 랜덤이라 재현할 수 없어 풀 전량을 준다.</summary>
    public static void ApplyReplayIfScheduled()
    {
        if (!TryGetScheduled(out int t_chapter, out int t_step)) return;

        Cancel();   // 1회 소비 — 재생이 실패해도 다음 부트마다 세이브를 다시 밀지 않게 먼저 걷는다.

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

                // 보상 카드도 순수 세이브 작업이다 — 부트 중에는 연출을 세울 무대가 없으니 소유권만 준다.
                if (t_row.Action == EOutgameTutorialAction.CardGrant)
                {
                    if (t_row.Card != null && OwnershipManager.Grant(CardCatalog.IdOf(t_row.Card))) t_cards++;
                    continue;
                }

                if (t_row.Action == EOutgameTutorialAction.CardSetGrant)
                {
                    t_cards += GrantCardSet(t_row.Cards);
                    continue;
                }


                if (t_row.Pack != null && TutorialStepDef.UsesPack(t_row.Action)) t_cards += GrantPackPool(t_row.Pack);
            }
        }

        Debug.Log($"[TutorialRewind] 좌표 {t_chapter}-{t_step}까지 지급 재생 — 덱 스텝 {t_decks}개 / 팩 풀 카드 {t_cards}장 · 소유 {OwnershipManager.OwnedCount}장");
    }

    static int GrantCardSet(IReadOnlyList<CardData> _cards)
    {
        if (_cards == null || _cards.Count == 0) return 0;

        var t_ids = new List<int>(_cards.Count);
        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            if (_cards[t_i] != null) t_ids.Add(CardCatalog.IdOf(_cards[t_i]));
        }

        return OwnershipManager.GrantAll(t_ids);
    }

    static int GrantPackPool(CardPackData _pack)
    {
        var t_pool = _pack.Pool;
        if (t_pool == null || t_pool.Count == 0) return 0;

        var t_ids = new List<int>(t_pool.Count);
        for (int t_i = 0; t_i < t_pool.Count; t_i++)
        {
            if (t_pool[t_i] != null) t_ids.Add(CardCatalog.IdOf(t_pool[t_i]));
        }

        return OwnershipManager.GrantAll(t_ids);
    }
}
