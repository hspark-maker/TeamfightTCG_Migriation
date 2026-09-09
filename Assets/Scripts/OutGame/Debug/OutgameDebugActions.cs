using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 아웃게임 디버그 조작의 단일 창구 (인스펙터 ContextMenu·SROptions 공용)
public static class OutgameDebugActions
{
    // 디버그 지급 단위
    public const long DEBUG_GOLD_AMOUNT    = 1000;
    public const long DEBUG_DIAMOND_AMOUNT = 1000;
    public const long DEBUG_ENERGY_AMOUNT  = 1000;
    public const long DEBUG_SHARD_AMOUNT   = 1000;

    // 1회 회전 비용이 티켓 1장이라 10장이면 충분하다 — 큰 수는 잔액 대조를 흐린다.
    public const long DEBUG_ROULETTE_TICKET_AMOUNT = 10;

    public static void GrantGold() => GrantCurrency(ECurrencyType.Gold, DEBUG_GOLD_AMOUNT);

    public static void GrantDiamond() => GrantCurrency(ECurrencyType.Diamond, DEBUG_DIAMOND_AMOUNT);

    public static void GrantEnergy() => GrantCurrency(ECurrencyType.Energy, DEBUG_ENERGY_AMOUNT);

    public static void GrantShard() => GrantCurrency(ECurrencyType.Shard, DEBUG_SHARD_AMOUNT);

    public static void GrantRouletteTicket() => GrantCurrency(ECurrencyType.RouletteTicket, DEBUG_ROULETTE_TICKET_AMOUNT);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 서버 없이 회전 안무를 만지는 유일한 문. 자동 배선은 없다 — 로컬 추첨이 출시 빌드로 새지 않게 한다.
    public static void UseLocalRouletteSource()
    {
        RouletteManager.UseLocalSourceForDebug();
    }
#endif

    // 카드 희귀도별 개봉 연출만 검증한다. 소유·중복 보상·재화·랭크·세이브는 건드리지 않는다.
    public static void OpenRarityTestPack(ECardGrade _grade)
    {
        if (_grade != ECardGrade.Rare && _grade != ECardGrade.Arcane && _grade != ECardGrade.Mythic)
        {
            Debug.LogWarning($"[OutgameDebug] Unsupported grade for the rarity test pack: {_grade}");
            return;
        }
        if (OutgameTutorialRunner.IsRunning || TriggeredTutorialRunner.IsRunning)
        {
            Debug.LogWarning("[OutgameDebug] The rarity test pack cannot be opened during the tutorial, in order to protect the progress events.");
            return;
        }
        if (PackOpenOverlay.Instance == null || PackOpenOverlay.IsOpen)
        {
            Debug.LogWarning("[OutgameDebug] The rarity test pack cannot be opened because the reveal overlay is missing or already open.");
            return;
        }
        if (PackHandoff.HasPending)
        {
            Debug.LogWarning("[OutgameDebug] The rarity test pack cannot be opened because there is an unconsumed reveal session.");
            return;
        }
        if (!CardCatalog.IsReady)
        {
            Debug.LogWarning("[OutgameDebug] The rarity test pack cannot be opened because the card catalog is not ready yet.");
            return;
        }

        var t_cards = new List<int>();
        for (int t_i = 0; t_i < CardCatalog.AllIds.Count; t_i++)
        {
            int t_card = CardCatalog.AllIds[t_i];
            if (CardCatalog.RequireSpec(t_card).Grade == _grade) t_cards.Add(t_card);
        }
        if (t_cards.Count == 0)
        {
            Debug.LogWarning($"[OutgameDebug] The rarity test pack cannot be opened because there is no {_grade} card.");
            return;
        }

        var t_drawn = new List<DrawnCard>(6);
        for (int t_i = 0; t_i < 6; t_i++)
            t_drawn.Add(new DrawnCard(t_cards[t_i % t_cards.Count], false));

        PackHandoff.Set(OpenedPack.CreateSuccess(t_drawn, ECurrencyType.Gold), null, null, false);
        if (PackOpenOverlay.TryOpen()) return;

        PackHandoff.Consume();
        Debug.LogWarning($"[OutgameDebug] Could not open the {_grade} rarity test pack reveal screen.");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 서버 진단 호출. 세이브를 바꾸지 않아 채택 창구를 거치지 않는다.
    public static void PingServer()
    {
        PingServerAsync().Forget();
    }

    // 서버가 문서를 쓰고 revision을 올리는 경로 검증 — 응답 채택이 세션을 끊지 않는지까지 본다.
    public static void BumpServerRevision()
    {
        BumpServerRevisionAsync().Forget();
    }

    // 배포된 보안 규칙이 실클라를 실제로 막는지 검증. 세 경로 전부 거부돼야 정상이다.
    public static void ProbeRuleDenials()
    {
        ProbeRuleDenialsAsync().Forget();
    }

    static async UniTaskVoid PingServerAsync()
    {
        try
        {
            var t_result = await ServerSaveCommands.InvokeReadOnlyAsync<PingResult>(
                "ping", new { env = ContentProfileConfig.Active.CloudEnvId });

            Debug.Log(
                $"[OutgameDebug] ping ok={t_result.Ok} envKnown={t_result.EnvKnown} " +
                $"uid={t_result.Uid} env={t_result.Env} " +
                $"database={t_result.Database} schemaVersion={t_result.SchemaVersion} " +
                $"documentSchemaVersion={t_result.DocumentSchemaVersion} " +
                $"exists={t_result.Exists} revision={t_result.Revision} readError={t_result.ReadError}");
        }
        catch (System.Exception t_exception)
        {
            Debug.LogError($"[OutgameDebug] ping failed — {t_exception.GetBaseException().Message}");
        }
    }

    static async UniTaskVoid BumpServerRevisionAsync()
    {
        long t_before = PlayerSaveCloud.Revision;

        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<ServerCommandResult>(
                "devBumpRevision", new { env = ContentProfileConfig.Active.CloudEnvId, nickname = "r0-probe" });

            Debug.Log(
                $"[OutgameDebug] devBumpRevision revision {t_before} → {t_result.Revision} " +
                $"({PlayerSaveCloud.Revision} after adoption), state={PlayerSaveCloud.State}");
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            // 거절은 세션 사고가 아니라 이 호출의 결과다 — 도메인이 표면을 진다는 계약의 첫 준수 지점.
            Debug.LogWarning($"[OutgameDebug] devBumpRevision rejected — {t_rejected.Message}");
        }
        catch (System.Exception t_exception)
        {
            Debug.LogError($"[OutgameDebug] devBumpRevision failed — {t_exception.GetBaseException().Message}");
        }
    }

    // 실재하지 않는 uid·env를 겨눈다. 규칙은 문서 존재보다 소유자·환경을 먼저 보므로
    // 문서가 없어도 permission-denied가 나와야 하고, 없어서 통과하면 그건 규칙이 열린 것이다.
    const string FOREIGN_PROBE_UID = "rules-probe-not-my-uid";
    const string UNKNOWN_PROBE_ENV = "dev";
    const int RULE_PROBE_COUNT = 3;

    static async UniTaskVoid ProbeRuleDenialsAsync()
    {
        string t_uid = FirebaseAuthService.Instance.UserId;
        if (string.IsNullOrEmpty(t_uid))
        {
            Debug.LogWarning("[OutgameDebug] Not signed in yet, so the rules probe cannot run.");
            return;
        }

        string t_env = ContentProfileConfig.Active.CloudEnvId;
        int t_denied = 0;

        if (await LogRuleProbeAsync("남의 uid", PlayerSaveFirestorePaths.Current(t_env, FOREIGN_PROBE_UID))) t_denied++;
        if (await LogRuleProbeAsync("미지 env", PlayerSaveFirestorePaths.Current(UNKNOWN_PROBE_ENV, t_uid))) t_denied++;
        if (await LogRuleProbeAsync("매치 문서", FirebaseRootPath.Environment(t_env) + "/matches/rules-probe")) t_denied++;

        if (t_denied == RULE_PROBE_COUNT)
            Debug.Log($"[OutgameDebug] Rules probe blocked {t_denied}/{RULE_PROBE_COUNT} — the deployed rules block a real client. (my uid {t_uid})");
        else
            Debug.LogError($"[OutgameDebug] Rules probe blocked {t_denied}/{RULE_PROBE_COUNT} — there is an open path.");
    }

    static async UniTask<bool> LogRuleProbeAsync(string _label, string _path)
    {
        PlayerSaveCloud.RuleProbe t_probe = await PlayerSaveCloud.ProbeReadDeniedAsync(_path);

        if (t_probe.Denied) Debug.Log($"[OutgameDebug] Rule block OK — {_label} · {t_probe.Detail}");
        else Debug.LogError($"[OutgameDebug] A rule is open — {_label} · {t_probe.Detail} · {_path}");

        return t_probe.Denied;
    }
#endif

    // 재화 지급을 서버에 맡긴다(잔액·영속의 진실원은 서버 문서다).
    // SROptions의 인자 없는 void 버튼 메서드가 각 지급 함수를 호출한다.
    public static void GrantCurrency(ECurrencyType _type, long _amount)
    {
        GrantCurrencyAsync(_type, _amount).Forget();
    }

    static async UniTaskVoid GrantCurrencyAsync(ECurrencyType _type, long _amount)
    {
        try
        {
            await ServerSaveCommands.InvokeAsync<ServerCommandResult>(
                "devGrantCurrency",
                new { env = ContentProfileConfig.Active.CloudEnvId, currency = _type.ToString(), amount = _amount });

            // 서버가 확정한 값을 찍는다 — 표시 잔액에는 다른 요청의 낙관분이 섞여 있어 지급 결과를 대조할 수 없다.
            Debug.Log($"[OutgameDebug] {_type} +{_amount} — server balance {CurrencyManager.GetServerBalance(_type)}");
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            Debug.LogWarning($"[OutgameDebug] devGrantCurrency rejected — {t_rejected.Message}");
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다 — 여기서 표면을 두 번 칠하지 않는다.
            Debug.LogWarning($"[OutgameDebug] Adopting the response closed the session — {t_adoption.Message}");
        }
        catch (System.Exception t_exception)
        {
            Debug.LogError($"[OutgameDebug] devGrantCurrency failed — {t_exception.GetBaseException().Message}");
        }
    }

    // 활성 일일·주간 미션의 진행도를 서버에서 목표까지 채운다(수령 낙인은 안 찍는다 — 수령 흐름 검증용).
    public static void CompleteMissions()
    {
        CompleteMissionsAsync().Forget();
    }

    static async UniTaskVoid CompleteMissionsAsync()
    {
        try
        {
            await ServerSaveCommands.InvokeReadOnlyAsync<ServerCommandResult>(
                "devCompleteMissions",
                new { env = ContentProfileConfig.Active.CloudEnvId });

            // 채운 진행도는 미션 문서에만 있다 — 재조회로 캐시를 갈아야 화면(OnChanged)이 따라온다.
            await MissionCommands.RefreshAsync();
            Debug.Log("[OutgameDebug] devCompleteMissions done — mission cache refreshed.");
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            Debug.LogWarning($"[OutgameDebug] devCompleteMissions rejected — {t_rejected.Message}");
        }
        catch (System.Exception t_exception)
        {
            Debug.LogError($"[OutgameDebug] devCompleteMissions failed — {t_exception.GetBaseException().Message}");
        }
    }

    // 전 카드 만렙 (재화·성공률 무시 — 진화 단계·키워드 해금도 레벨에서 파생돼 같이 열린다)
    public static void MaxCardGrowth()
    {
        int t_changed = CardGrowthManager.DebugMaxAll();

        if (t_changed == 0)
        {
            Debug.LogWarning("[OutgameDebug] No target for max enhance — everything is already at max level, or the growth system is not initialized yet (it has to go through initialization).");
            return;
        }

        Debug.Log($"[OutgameDebug] Max enhance on every card — {t_changed} card(s) at {CardGrowthManager.MaxStar} star(s)");
    }

    // 강화 레벨·진화 단계 재설정 (소유·재화는 유지)
    public static void ResetCardGrowth()
    {
        CardGrowthManager.DebugResetAll();

        Debug.Log("[OutgameDebug] Card growth reset — every card at 0 stars, unevolved");
    }

    // 카탈로그 전량 지급
    public static void UnlockAllCards()
    {
        int t_added = OwnershipManager.GrantEntireCatalog();
        Debug.Log($"[OutgameDebug] Unlock all — {t_added} new / {OwnershipManager.OwnedCount} owned");
    }

    public static void RevokeAllCards()
    {
        int t_removed = OwnershipManager.RevokeAll();
        Debug.Log($"[OutgameDebug] Revoke all — {t_removed} removed / {OwnershipManager.OwnedCount} owned");
    }

    public static void LogOwnership()
    {
        Debug.Log($"[OutgameDebug] {OwnershipManager.OwnedCount} owned: {string.Join(", ", OwnershipManager.OwnedIds)}");
    }

    // 튜토리얼 완료 낙인 + 떠 있는 게이트 해제
    public static void SkipTutorial()
    {
        OutgameTutorialRunner.CompleteSequence();   // 스킵도 졸업 — 첫 랭크 진입을 동일하게 받는다
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.ClearForce();

        Debug.Log("[OutgameDebug] Tutorial marked complete — gate released");
    }

    // 튜토리얼 진행도만 재설정 (소유는 유지)
    public static void ResetTutorial()
    {
        OutgameTutorialProgress.ResetForDebug();
        TriggeredTutorialRunner.Abort();
        Debug.Log($"[OutgameDebug] Tutorial progress reset — {OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex} / completed {OutgameTutorialProgress.IsCompleted}");
    }

    // 트리거 튜토리얼(탭 첫 진입 등) 낙인만 재설정
    public static void ResetTriggeredTutorials()
    {
        // 낙인을 먼저 걷는다 — Abort가 변경을 통지하므로, 순서를 뒤집으면 알림 점이 아직 완주 상태를 보고 안 뜬다.
        OutgameTutorialProgress.ClearTriggersForDebug();
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.ClearForce();

        Debug.Log("[OutgameDebug] Triggered tutorial marks reset — they play again when you re-enter the tab");
    }

    // 튜토리얼 N편 처음으로 되감기 — 되돌리는 것은 좌표와 완료 낙인뿐이다(씬 재진입 시 적용).
    // 소유·재화·덱·랭크·성장은 그대로 남으므로 여기서 본 화면은 실제 신규 유저의 화면과 다르다
    // (덱 지급은 이미 있는 슬롯을 만나 조용히 지나가고, 카드 세트 지급은 가진 카드를 신규처럼 다시 연출한다).
    // 첫실행 상태 그대로 보려면 에디터의 [Tools > Card Battle > 튜토리얼 저작 도구]에서 [여기부터]로 예약하고 재생한다.
    public static void RestartTutorialFromChapter(int _chapterIndex)
    {
        int t_last    = OutgameTutorialRunner.ChapterCount - 1;
        int t_chapter = t_last < 0 ? 0 : Mathf.Clamp(_chapterIndex, 0, t_last);

        OutgameTutorialProgress.JumpForDebug(t_chapter, 0);
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.ClearForce();

        Debug.Log($"[OutgameDebug] Tutorial chapter {t_chapter + 1} back to the start — only the position is rewound (ownership and currency are kept). Applied on scene re-entry ({OutgameTutorialRunner.ChapterCount} chapter(s) authored in total)");
    }

    // 계정 경험치 더하기. 만렙 구간은 전승 1,000판대라 이 문 없이는 확인할 수 없다.
    public static void AddAccountExp(long _amount)
    {
        AccountLevelManager.AddExpForDebug(_amount);
        AccountLevelInfo t_info = AccountLevelManager.GetInfo();
        Debug.Log($"[OutgameDebug] Account exp +{_amount} — Lv.{t_info.Level} ({t_info.ExpInLevel}/{t_info.ExpToNext})");
    }

    // 만렙으로 밀기
    public static void FillAccountLevel()
    {
        AccountLevelManager.FillToMaxForDebug();
        Debug.Log($"[OutgameDebug] Account level maxed — Lv.{AccountLevelManager.Level}");
    }

    // 레벨 1로 되돌리기
    public static void ResetAccountLevel()
    {
        AccountLevelManager.ResetForDebug();
        Debug.Log("[OutgameDebug] Account level reset — Lv.1");
    }

    // 티어 1단계 올리기/내리기
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

        Debug.Log($"[OutgameDebug] Tier {t_before} → {t_after} ({t_info.DisplayName}) / points {t_info.Points} — the presentation plays on scene re-entry");
    }

    // 승급전 대기선으로 바로 점프. 티어 버튼은 임계치에 세우므로 이 상태엔 못 간다.
    public static void JumpToPromoStandby()
    {
        int t_before  = RankManager.GetInfo().TierIndex;
        long t_points = RankManager.Points;

        if (!RankManager.SetPromoStandbyForDebug())
        {
            Debug.Log("[OutgameDebug] Cannot go to promotion standby — unranked, or already at the top grade");
            return;
        }

        RankInfo t_info = RankManager.GetInfo();

        // StepTier와 같은 이유로 캐리어에 싣는다 — 실어야 씬 재진입 때 승급전 진입 연출이 재생된다.
        RankResultHandoff.Set(new RankApplyResult(t_info.Points - t_points, t_before, t_info.TierIndex, false, true));

        Debug.Log($"[OutgameDebug] Promotion standby — {t_info.DisplayName} / points {t_info.Points} — the presentation plays on scene re-entry");
    }

    // 랭크 포인트 재설정(브론즈 1로)
    public static void ResetTier()
    {
        RankManager.ResetForDebug();

        RankInfo t_info = RankManager.GetInfo();
        Debug.Log($"[OutgameDebug] Rank reset — {t_info.DisplayName}");
    }

    // 잠긴 기능 전체 해금 토글 (튜토리얼 딤은 별개 축이라 걷히지 않는다)
    public static void ToggleFeatureLock()
    {
        OutgameFeatureLock.ForceUnlockAllForDebug = !OutgameFeatureLock.ForceUnlockAllForDebug;

        Debug.Log($"[OutgameDebug] Feature lock {(OutgameFeatureLock.ForceUnlockAllForDebug ? "ignored (everything unlocked)" : "applied normally")}");
    }

    // 모험 현재 정점 도전(맵 UI가 붙기 전 검증용). 로비 진입점을 그대로 태운다 — 전투 진입 규율을 우회하지 않는다.
    public static void StartCurrentAdventureNode()
    {
        int t_index = AdventureProgress.CurrentNodeIndex;
        if (t_index < 0)
        {
            Debug.LogWarning("[OutgameDebug] There is no challengeable node — AdventureConfig is unwired/unauthored, or everything is cleared.");
            return;
        }

        var t_launcher = Object.FindFirstObjectByType<LobbyMatchLauncher>(FindObjectsInactive.Include);
        if (t_launcher == null)
        {
            Debug.LogWarning("[OutgameDebug] There is no LobbyMatchLauncher in the scene, so the node challenge is skipped — run this in the lobby scene.");
            return;
        }

        t_launcher.StartAdventureBattle(t_index);
        Debug.Log($"[OutgameDebug] Challenging adventure node #{t_index + 1} — entering the deck screen");
    }

    // 팩 없이 앨범 삽입 연출만 반복 검증. 소유 카드를 그대로 다시 꽂는 연출이라 소유·세이브는 건드리지 않는다.
    public static void ForceAlbumInsertSession(int _count = 3)
    {
        List<int> t_cards = CollectOwnedAlbumCards(_count);
        if (t_cards.Count == 0)
        {
            Debug.LogWarning("[OutgameDebug] There is no owned card for the album, so the insert session is skipped — open a pack or unlock everything and try again.");
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

            Debug.LogWarning("[OutgameDebug] There is no AlbumTabController in the scene, so the insert session is skipped — run this in the lobby scene.");
            return;
        }

        // 도감 탭이 꺼져 있으면 조용히 대기했다가 탭에 들어가는 순간 재생된다.
        t_album.TryBeginInsert();
        Debug.Log($"[OutgameDebug] Album insert session reserved — {t_cards.Count} card(s) (plays when the album tab is entered)");
    }

    // 앨범 저작 순서(테마→페이지→슬롯) 기준 소유 카드 앞 _count장. 해금은 하지 않는다.
    static List<int> CollectOwnedAlbumCards(int _count)
    {
        var t_result = new List<int>();
        if (_count <= 0) return t_result;

        var t_themes = CardAlbum.Themes;
        for (int t_i = 0; t_i < t_themes.Count; t_i++)
        {
            var t_cards = t_themes[t_i].CardIds;
            for (int t_j = 0; t_j < t_cards.Count; t_j++)
            {
                var t_card = t_cards[t_j];
                if (t_card <= 0 || !OwnershipManager.IsOwned(t_card)) continue;

                t_result.Add(t_card);
                if (t_result.Count >= _count) return t_result;
            }
        }

        return t_result;
    }
}

