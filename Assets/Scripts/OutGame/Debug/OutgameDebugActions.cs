using System.Collections.Generic;
using UnityEngine;

// 아웃게임 디버그 조작의 단일 창구 (인스펙터 ContextMenu·런타임 오버레이 공용)
public static class OutgameDebugActions
{
    // 디버그 지급 단위
    public const long DEBUG_GOLD_AMOUNT    = 1000;
    public const long DEBUG_DIAMOND_AMOUNT = 1000;
    public const long DEBUG_ENERGY_AMOUNT  = 1000;
    public const long DEBUG_SHARD_AMOUNT   = 1000;

    public static void GrantGold() => GrantCurrency(ECurrencyType.Gold, DEBUG_GOLD_AMOUNT);

    public static void GrantDiamond() => GrantCurrency(ECurrencyType.Diamond, DEBUG_DIAMOND_AMOUNT);

    public static void GrantEnergy() => GrantCurrency(ECurrencyType.Energy, DEBUG_ENERGY_AMOUNT);

    public static void GrantShard() => GrantCurrency(ECurrencyType.Shard, DEBUG_SHARD_AMOUNT);

    // 재화 즉시 지급 + 즉시 영속
    public static void GrantCurrency(ECurrencyType _type, long _amount)
    {
        CurrencyManager.Earn(_type, _amount);
        CurrencyManager.Save();

        Debug.Log($"[OutgameDebug] {_type} +{_amount} — 잔액 {CurrencyManager.GetBalance(_type)}");
    }

    // 전 카드 만렙 (재화·성공률 무시 — 진화 단계·키워드 해금도 레벨에서 파생돼 같이 열린다)
    public static void MaxCardGrowth()
    {
        int t_changed = CardGrowthManager.DebugMaxAll();

        if (t_changed == 0)
        {
            Debug.LogWarning("[OutgameDebug] 최대 강화 대상 없음 — 이미 전부 만렙이거나 성장 시스템이 아직 초기화되지 않았다(부트 경유 필요).");
            return;
        }

        Debug.Log($"[OutgameDebug] 전 카드 최대 강화 — {t_changed}장 Lv{CardGrowthManager.MaxLevel}");
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
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.ClearForce();

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
        // 낙인을 먼저 걷는다 — Abort가 변경을 통지하므로, 순서를 뒤집으면 알림 점이 아직 완주 상태를 보고 안 뜬다.
        OutgameTutorialProgress.ClearTriggersForDebug();
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.ClearForce();

        Debug.Log("[OutgameDebug] 트리거 튜토리얼 낙인 초기화 — 탭에 다시 들어가면 재생됩니다");
    }

    // 튜토리얼 N편 처음으로 되감기 — 되돌리는 것은 좌표와 완료 낙인뿐이다(씬 재진입 시 적용).
    // 소유·재화·덱·랭크·성장은 그대로 남으므로 여기서 본 화면은 실제 신규 유저의 화면과 다르다
    // (덱 지급은 이미 있는 슬롯을 만나 조용히 지나가고, 카드 세트 지급은 가진 카드를 신규처럼 다시 연출한다).
    // 첫실행 상태 그대로 보려면 에디터의 [Tools > Card Battle > 튜토리얼 스텝 되감기]로 예약하고 재생한다.
    public static void RestartTutorialFromChapter(int _chapterIndex)
    {
        int t_last    = OutgameTutorialRunner.ChapterCount - 1;
        int t_chapter = t_last < 0 ? 0 : Mathf.Clamp(_chapterIndex, 0, t_last);

        OutgameTutorialProgress.JumpForDebug(t_chapter, 0);
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.ClearForce();

        Debug.Log($"[OutgameDebug] 튜토리얼 {t_chapter + 1}편 처음으로 — 좌표만 되감음(소유·재화 유지). 씬 재진입 시 적용 (저작된 총 {OutgameTutorialRunner.ChapterCount}편)");
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

    // 승급전 대기선으로 바로 점프. 티어 버튼은 임계치에 세우므로 이 상태엔 못 간다.
    public static void JumpToPromoStandby()
    {
        int t_before  = RankManager.GetInfo().TierIndex;
        long t_points = RankManager.Points;

        if (!RankManager.SetPromoStandbyForDebug())
        {
            Debug.Log("[OutgameDebug] 승급전 대기로 갈 수 없다 — 언랭크이거나 최고 등급이다");
            return;
        }

        RankInfo t_info = RankManager.GetInfo();

        // StepTier와 같은 이유로 캐리어에 싣는다 — 실어야 씬 재진입 때 승급전 진입 연출이 재생된다.
        RankResultHandoff.Set(new RankApplyResult(t_info.Points - t_points, t_before, t_info.TierIndex, false, true));

        Debug.Log($"[OutgameDebug] 승급전 대기 — {t_info.DisplayName} / 포인트 {t_info.Points} — 씬 재진입 시 연출 재생");
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

    // 토너먼트 현재 정점 도전(맵 UI가 붙기 전 검증용). 로비 진입점을 그대로 태운다 — 전투 진입 규율을 우회하지 않는다.
    public static void StartCurrentTournamentNode()
    {
        int t_index = TournamentProgress.CurrentNodeIndex;
        if (t_index < 0)
        {
            Debug.LogWarning("[OutgameDebug] 도전 가능한 정점이 없다 — TournamentConfig 미배선/미저작이거나 전부 클리어했다.");
            return;
        }

        var t_launcher = Object.FindFirstObjectByType<LobbyMatchLauncher>(FindObjectsInactive.Include);
        if (t_launcher == null)
        {
            Debug.LogWarning("[OutgameDebug] 씬에 LobbyMatchLauncher가 없어 정점 도전을 건너뛴다 — 로비 씬에서 실행할 것.");
            return;
        }

        t_launcher.StartTournamentBattle(t_index);
        Debug.Log($"[OutgameDebug] 토너먼트 정점 #{t_index + 1} 도전 — 덱 화면 진입");
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
