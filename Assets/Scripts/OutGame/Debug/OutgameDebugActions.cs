using System.Collections.Generic;
using UnityEngine;

// 아웃게임 디버그 조작의 단일 창구 (인스펙터 ContextMenu·런타임 오버레이 공용)
public static class OutgameDebugActions
{
    // 디버그 지급 단위
    public const long DEBUG_GOLD_AMOUNT    = 1000;
    public const long DEBUG_DIAMOND_AMOUNT = 1000;

    public static void GrantGold() => GrantCurrency(ECurrencyType.Gold, DEBUG_GOLD_AMOUNT);

    public static void GrantDiamond() => GrantCurrency(ECurrencyType.Diamond, DEBUG_DIAMOND_AMOUNT);

    // 재화 즉시 지급 + 즉시 영속
    public static void GrantCurrency(ECurrencyType _type, long _amount)
    {
        CurrencyManager.Earn(_type, _amount);
        CurrencyManager.Save();

        Debug.Log($"[OutgameDebug] {_type} +{_amount} — 잔액 {CurrencyManager.GetBalance(_type)}");
    }

    // 강화 레벨·진화 단계 초기화 (소유·재화는 유지)
    public static void ResetCardGrowth()
    {
        CardGrowthManager.DebugResetAll();

        Debug.Log("[OutgameDebug] 카드 성장 초기화 — 전 카드 Lv0 · 미진화");
    }

    // 카탈로그 전량 지급
    public static void UnlockAllCards()
    {
        int t_added = OwnershipManager.GrantEntireCatalog();
        Debug.Log($"[OutgameDebug] 전체 해금 — 신규 {t_added}장 / 소유 {OwnershipManager.OwnedCount}장");
    }

    public static void RevokeAllCards()
    {
        int t_removed = OwnershipManager.RevokeAll();
        Debug.Log($"[OutgameDebug] 전체 회수 — {t_removed}장 제거 / 소유 {OwnershipManager.OwnedCount}장");
    }

    public static void LogOwnership()
    {
        Debug.Log($"[OutgameDebug] 소유 {OwnershipManager.OwnedCount}장: {string.Join(", ", OwnershipManager.OwnedIds)}");
    }

    // 튜토리얼 완료 낙인 + 떠 있는 게이트 해제
    public static void SkipTutorial()
    {
        OutgameTutorialRunner.CompleteSequence();   // 스킵도 졸업 — 첫 랭크 진입을 동일하게 받는다
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log("[OutgameDebug] 튜토리얼 완료 처리 — 게이트 해제");
    }

    // 튜토리얼 진행도만 초기화 (소유는 유지)
    public static void ResetTutorial() => RestartTutorialFromStep(0, 0, false);

    // 트리거 튜토리얼(탭 첫 진입 등) 낙인만 초기화
    public static void ResetTriggeredTutorials()
    {
        TriggeredTutorialRunner.Abort();
        OutgameTutorialProgress.ClearTriggersForDebug();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log("[OutgameDebug] 트리거 튜토리얼 낙인 초기화 — 탭에 다시 들어가면 재생됩니다");
    }

    /// <summary>튜토리얼을 임의 좌표(N편 M번째 스텝)로 되감아 그 스텝부터 다시 검증한다.
    ///
    /// _prepare = 그 스텝이 성립하려면 앞선 스텝이 지급했어야 할 것(덱·카드 소유)을 같이 채운다.
    /// 끄면 좌표만 움직이므로, 예를 들어 덱이 없는 세이브로 전투 스텝에 서면 진행이 막힌다.</summary>
    public static void RestartTutorialFromStep(int _chapterIndex, int _stepIndex, bool _prepare)
    {
        if (OutgameTutorialRunner.ChapterCount == 0)
        {
            Debug.LogWarning("[OutgameDebug] 저작된 튜토리얼 챕터가 없어 되감을 좌표가 없습니다 — 브리지의 시퀀스 배선을 확인하세요.");
            return;
        }

        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        // 좌표를 옮기기 "전"에 채운다 — 지급 로그와 되감기 로그가 섞이지 않고, 목표 스텝은 진입 순간 이미 조건이 갖춰져 있다.
        if (_prepare) PrepareStepPrerequisites(_chapterIndex, _stepIndex);

        OutgameTutorialRunner.RewindForDebug(_chapterIndex, _stepIndex);

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_step    = OutgameTutorialProgress.StepIndex;
        string t_action = OutgameTutorialRunner.TryGetStepAt(t_chapter, t_step, out var t_def) ? t_def.Action.ToString() : "빈 칸";

        Debug.Log($"[OutgameDebug] 튜토리얼 되감기 → {t_chapter}-{t_step} ({t_action}) / 사전지급 {(_prepare ? "ON" : "OFF")}");
    }

    // 목표 좌표 "직전"까지의 스텝이 지급했어야 할 것을 재생한다(좌표는 건드리지 않는다).
    // 씬 로드·오버레이를 여는 액션(AutoBattle·AutoPurchase·BattleEntry)은 실행하지 않는다 — 되감기 도중 화면을 뺏는다.
    static void PrepareStepPrerequisites(int _chapterIndex, int _stepIndex)
    {
        int t_decks = 0;
        int t_cards = 0;

        // EnumerateUpTo는 좌표를 돌려주지 않는다 — 실행 로그가 엉뚱한 칸을 가리키지 않게 여기서는 좌표째 훑는다.
        for (int t_c = 0; t_c <= _chapterIndex && t_c < OutgameTutorialRunner.ChapterCount; t_c++)
        {
            int t_count = OutgameTutorialRunner.StepCountOf(t_c);
            int t_end   = t_c < _chapterIndex ? t_count : Mathf.Min(_stepIndex, t_count);

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

                // 팩에서 나왔어야 할 카드. 실제 드로우는 랜덤이라 재현할 수 없으니 검증용으로 풀 전체를 준다.
                if (t_row.Pack != null && TutorialStepDef.UsesPack(t_row.Action)) t_cards += GrantPackPool(t_row.Pack);
            }
        }

        if (t_decks == 0 && t_cards == 0) return;

        Debug.Log($"[OutgameDebug] 튜토리얼 사전지급 — 덱 스텝 {t_decks}개 재생 / 팩 풀 카드 {t_cards}장 신규 지급");
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

    // 티어 1단계 올리기/내리기. AI 카드 레벨이 티어에서 나오므로 난이도 곡선을 이걸로 확인한다.
    public static void RaiseTier() => StepTier(+1);

    public static void LowerTier() => StepTier(-1);

    static void StepTier(int _step)
    {
        int t_before  = RankManager.GetInfo().TierIndex;
        long t_points = RankManager.Points;
        int t_after   = RankManager.StepTierForDebug(_step);

        RankInfo t_info = RankManager.GetInfo();

        // 캐리어에 실어 두면 씬 재진입 때 로비 디렉터가 소비해 승급·강등 연출을 그대로 재생한다 —
        // 이 버튼은 포인트만 옮기므로, 싣지 않으면 연출을 볼 방법이 전투밖에 없다.
        RankResultHandoff.Set(new RankApplyResult(t_info.Points - t_points, t_before, t_after));

        Debug.Log($"[OutgameDebug] 티어 {t_before} → {t_after} ({t_info.DisplayName}) / 포인트 {t_info.Points} / AI 카드 레벨 {RankManager.AiCardLevel} — 씬 재진입 시 연출 재생");
    }

    // 랭크 포인트 초기화(브론즈 1로)
    public static void ResetTier()
    {
        RankManager.ResetForDebug();

        RankInfo t_info = RankManager.GetInfo();
        Debug.Log($"[OutgameDebug] 랭크 초기화 — {t_info.DisplayName} / AI 카드 레벨 {RankManager.AiCardLevel}");
    }

    // 잠긴 기능 전체 해금 토글 (튜토리얼 딤은 별개 축이라 걷히지 않는다)
    public static void ToggleFeatureLock()
    {
        OutgameFeatureLock.ForceUnlockAllForDebug = !OutgameFeatureLock.ForceUnlockAllForDebug;

        Debug.Log($"[OutgameDebug] 기능 잠금 {(OutgameFeatureLock.ForceUnlockAllForDebug ? "무시(전체 해금)" : "정상 적용")}");
    }

    // 첫실행 재현 원샷 — 소유까지 비우고 튜토리얼 리셋
    public static void ResetTutorialFromScratch()
    {
        RevokeAllCards();
        ResetTutorial();
    }

    // 팩 없이 앨범 삽입 연출만 반복 검증. 소유 카드를 그대로 다시 꽂는 연출이라 소유·세이브는 건드리지 않는다.
    public static void ForceAlbumInsertSession(int _count = 3)
    {
        List<CardData> t_cards = CollectOwnedAlbumCards(_count);
        if (t_cards.Count == 0)
        {
            Debug.LogWarning("[OutgameDebug] 앨범에 소유 카드가 없어 삽입 세션을 건너뛴다 — 팩을 열거나 전체 해금 후 다시 시도.");
            return;
        }

        AlbumInsertQueue.Enqueue(t_cards);
        AlbumInsertMask.HideAll(t_cards);

        // 도감 탭은 평소 꺼져 있다 — 비활성 포함 탐색이어야 잡힌다.
        var t_album = Object.FindFirstObjectByType<AlbumTabController>(FindObjectsInactive.Include);
        if (t_album == null)
        {
            // 위장이 남으면 카드가 영영 빈 칸이다.
            AlbumInsertQueue.Clear();
            AlbumInsertMask.Clear();

            Debug.LogWarning("[OutgameDebug] 씬에 AlbumTabController가 없어 삽입 세션을 건너뛴다 — 로비 씬에서 실행할 것.");
            return;
        }

        // 도감 탭이 꺼져 있으면 조용히 대기했다가 탭에 들어가는 순간 재생된다.
        t_album.TryBeginInsert();
        Debug.Log($"[OutgameDebug] 앨범 삽입 세션 예약 — {t_cards.Count}장(도감 탭 진입 시 재생)");
    }

    // 앨범 저작 순서(테마→페이지→슬롯) 기준 소유 카드 앞 _count장. 해금은 하지 않는다.
    static List<CardData> CollectOwnedAlbumCards(int _count)
    {
        var t_result = new List<CardData>();
        if (_count <= 0) return t_result;

        var t_themes = CardAlbum.Themes;
        for (int t_i = 0; t_i < t_themes.Count; t_i++)
        {
            var t_cards = t_themes[t_i].Cards;
            for (int t_j = 0; t_j < t_cards.Count; t_j++)
            {
                var t_card = t_cards[t_j];
                if (t_card == null || !OwnershipManager.IsOwned(t_card)) continue;

                t_result.Add(t_card);
                if (t_result.Count >= _count) return t_result;
            }
        }

        return t_result;
    }
}
