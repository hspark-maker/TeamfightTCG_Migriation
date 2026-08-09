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
        OutgameTutorialProgress.Complete();
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log("[OutgameDebug] 튜토리얼 완료 처리 — 게이트 해제");
    }

    // 튜토리얼 진행도만 초기화 (소유는 유지)
    public static void ResetTutorial()
    {
        OutgameTutorialProgress.ResetForDebug();
        TriggeredTutorialRunner.Abort();
        Debug.Log($"[OutgameDebug] 튜토리얼 진행도 리셋 — {OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex} / completed {OutgameTutorialProgress.IsCompleted}");
    }

    // 트리거 튜토리얼(탭 첫 진입 등) 낙인만 초기화
    public static void ResetTriggeredTutorials()
    {
        TriggeredTutorialRunner.Abort();
        OutgameTutorialProgress.ClearTriggersForDebug();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log("[OutgameDebug] 트리거 튜토리얼 낙인 초기화 — 탭에 다시 들어가면 재생됩니다");
    }

    // 튜토리얼 N편 처음으로 되감기 (소유·재화 유지, 씬 재진입 시 적용)
    public static void RestartTutorialFromChapter(int _chapterIndex)
    {
        int t_last    = OutgameTutorialRunner.ChapterCount - 1;
        int t_chapter = t_last < 0 ? 0 : Mathf.Clamp(_chapterIndex, 0, t_last);

        OutgameTutorialProgress.JumpForDebug(t_chapter, 0);
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log($"[OutgameDebug] 튜토리얼 {t_chapter + 1}편 처음으로 — 씬 재진입 시 적용 (저작된 총 {OutgameTutorialRunner.ChapterCount}편)");
    }

    // 티어 1단계 올리기/내리기. AI 카드 레벨이 티어에서 나오므로 난이도 곡선을 이걸로 확인한다.
    public static void RaiseTier() => StepTier(+1);

    public static void LowerTier() => StepTier(-1);

    static void StepTier(int _step)
    {
        int t_before = RankManager.GetInfo().TierIndex;
        int t_after  = RankManager.StepTierForDebug(_step);

        RankInfo t_info = RankManager.GetInfo();
        Debug.Log($"[OutgameDebug] 티어 {t_before} → {t_after} ({t_info.DisplayName}) / 포인트 {t_info.Points} / AI 카드 레벨 {RankManager.AiCardLevel}");
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

        // 삽입 패널은 평소 꺼져 있다 — 비활성 포함 탐색이어야 잡힌다.
        var t_session = Object.FindFirstObjectByType<AlbumInsertSession>(FindObjectsInactive.Include);
        if (t_session == null)
        {
            // 위장이 남으면 카드가 영영 빈 칸이다.
            AlbumInsertQueue.Clear();
            AlbumInsertMask.Clear();

            Debug.LogWarning("[OutgameDebug] 씬에 AlbumInsertSession이 없어 삽입 세션을 건너뛴다 — 로비 씬에서 실행할 것.");
            return;
        }

        t_session.Begin();
        Debug.Log($"[OutgameDebug] 앨범 삽입 세션 강제 시작 — {t_cards.Count}장");
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
