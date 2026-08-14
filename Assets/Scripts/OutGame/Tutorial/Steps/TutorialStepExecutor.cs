using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 스텝 행 하나를 실행하는 단일 창구
public static class TutorialStepExecutor
{
    const string BattleScene = "BattleScene";

    // 보상 오버레이 제목. 지금은 자리마다 하나씩이라 상수로 두지만, 늘어나면 스텝 저작값으로 올린다.
    const string RewardTitle = "첫 승리 보너스";
    const string CardSetTitle = "기본 카드 세트";

    // 스텝 진입 — 무엇을 하고 끝났는지는 반환값이 말한다(호출자가 좌표 델타로 되짚지 않게)
    public static EOutgameTutorialStepResult Enter(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        // 읽을 저작이 없으니 물어볼 실패 정책도 없다 — 좌표를 그대로 두고 정지로 답한다.
        if (_step == null) return EOutgameTutorialStepResult.Failed;

        switch (_step.Action)
        {
            case EOutgameTutorialAction.WaitClick:
            case EOutgameTutorialAction.Message:
            case EOutgameTutorialAction.WaitPurchase:
            case EOutgameTutorialAction.WaitPackOpen:
            case EOutgameTutorialAction.DeckAutoEquip:
            case EOutgameTutorialAction.BattleStart:
            case EOutgameTutorialAction.WaitAlbumInsert:
            case EOutgameTutorialAction.WaitEnhance:
            case EOutgameTutorialAction.WaitLobbyReturn:
            case EOutgameTutorialAction.WaitCardDetailReturn:
                return EOutgameTutorialStepResult.Gated;

            case EOutgameTutorialAction.CloseCardDetail: return EnterCloseCardDetail(_context);
            case EOutgameTutorialAction.EnterFirstRank: return EnterFirstRank(_context);
            case EOutgameTutorialAction.BattleEntry:  return EnterBattleEntry(_step, _context);
            case EOutgameTutorialAction.AutoBattle:   return EnterAutoBattle(_step, _context);
            case EOutgameTutorialAction.AutoPurchase: return EnterAutoPurchase(_step, _context);
            case EOutgameTutorialAction.DeckGrant:    return EnterDeckGrant(_step, _context);
            case EOutgameTutorialAction.CardGrant:    return EnterCardGrant(_step, _context);
            case EOutgameTutorialAction.CardSetGrant: return EnterCardSetGrant(_step, _context);
        }

        // 저작 실수라 정책을 물을 자리가 아니다 — 시퀀스가 이 칸에 걸리지 않게 넘긴다.
        Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 알 수 없는 액션({(int)_step.Action}) — 건너뜁니다.");
        _context.CommitAdvance();
        _context.CompleteIfLast();
        return EOutgameTutorialStepResult.Advanced;
    }

    /// <summary>실패 분기의 단일 창구 — 결말은 코드가 아니라 저작(onFailure)이 정한다.
    ///
    /// 커밋이 실행보다 앞서는 규약이라(OutgameTutorialStepContext) 실패 처리는 "선커밋을 되돌릴 것인가" 하나로 환원된다.
    /// Halt는 되돌려 그 자리에 세우고(재시도는 다음 부팅), 호출자는 Failed를 받아 기능 잠금을 연다.</summary>
    static EOutgameTutorialStepResult Fail(TutorialStepDef _step, OutgameTutorialStepContext _context, string _reason)
    {
        bool t_halt = _step != null && _step.OnFailure == EOutgameTutorialFailure.Halt;

        Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} {_reason} — "
                       + (t_halt ? "여기서 멈춥니다(기능 잠금 해제)." : "건너뛰고 진행합니다."));

        if (t_halt)
        {
            _context.Rollback();
            return EOutgameTutorialStepResult.Failed;
        }

        _context.CommitAdvance();
        _context.CompleteIfLast();
        return EOutgameTutorialStepResult.Advanced;
    }

    // 첫 랭크 티어 진입. 온보딩 전투가 다 끝난 자리(= 마지막 전투에서 로비로 돌아온 직후)에 두어야
    // 랭크 연출 디렉터가 자기 Start에서 캐리어를 소비한다 — 브리지의 스텝 진입이 그 소비(다음 프레임)보다 앞선다.
    // 뒤로 밀면 캐리어가 남아 다음 로비 진입 때 그때의 전투 결과에 병합돼 뒤늦게 터진다.
    //
    // 진입에 성공하면 완료를 넘기지 않는다 — 뒤이을 안내가 승급 연출 위에 겹쳐 뜨지 않도록
    // 그 연출이 끝나는 신호를 기다린다(Completion.RankEffect).
    static EOutgameTutorialStepResult EnterFirstRank(OutgameTutorialStepContext _context)
    {
        // 이미 랭크에 오른 세이브(디버그 승급·재진입)엔 보여줄 연출이 없다 — 기다리지 않고 지나간다.
        // 실패가 아니라 정상 통과라 실패 정책을 묻지 않는다.
        if (!RankManager.TryEnterFirstTier(out var t_entry))
        {
            _context.CommitAdvance();
            _context.CompleteIfLast();
            return EOutgameTutorialStepResult.Advanced;
        }

        RankResultHandoff.Set(t_entry);

        // 이 뒤로 가르칠 것이 없는 관람 구간이다 — 졸업 낙인(연출이 끝나야 찍힌다)을 기다리지 않고
        // 트리거 튜토리얼의 알림 점이 승급 연출과 나란히 뜨게 문을 먼저 연다.
        TriggeredTutorialRunner.NotifyOnboardingFinale();
        return EOutgameTutorialStepResult.Gated;
    }

    // 카드 상세는 전면 오버레이라 열려 있는 동안 로비 위젯을 전부 덮는다 —
    // 다음 안내가 로비를 가리킨다면 여기서 걷어 줘야 손가락이 닿을 자리가 생긴다.
    static EOutgameTutorialStepResult EnterCloseCardDetail(OutgameTutorialStepContext _context)
    {
        CardDetailOverlayView.Close();

        _context.CommitAdvance();
        _context.CompleteIfLast();
        return EOutgameTutorialStepResult.Advanced;
    }

    // 시나리오는 클릭 리스너가 아니라 진입 시 세운다 — PlayBtn의 씬 PersistentCall이 런타임 리스너보다 먼저 돈다
    static EOutgameTutorialStepResult EnterBattleEntry(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        // 실패로 치지 않는다 — 시나리오가 비어도 전투는 그대로 열린다(대본이 없을 뿐인 "저하된 성공").
        if (_step.Scenario == null)
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} BattleEntry에 시나리오가 미배선 — 일반 전투로 진입합니다.");

        TutorialConfig.Begin(_step.Scenario, _step.ShowDeckGate);
        return EOutgameTutorialStepResult.Gated;
    }

    static EOutgameTutorialStepResult EnterAutoBattle(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        _context.CommitAdvance();
        _context.CompleteIfLast();

        if (_step.Scenario == null)
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} AutoBattle에 시나리오가 미배선 — 일반 전투로 진입합니다.");

        TutorialConfig.Begin(_step.Scenario, _step.ShowDeckGate);
        SceneManager.LoadScene(BattleScene);
        return EOutgameTutorialStepResult.Advanced;
    }

    static EOutgameTutorialStepResult EnterAutoPurchase(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        if (PackOpenOverlay.Instance == null)
            return Fail(_step, _context, "개봉 오버레이 미배치(로비 씬 배선 확인)");

        _context.CommitAdvance();

        var t_opened = CardPackOpener.TryPurchase(_step.Pack);
        if (t_opened == null || !t_opened.Success)
        {
            string t_result = t_opened != null ? t_opened.Result.ToString() : "null";
            return Fail(_step, _context, $"자동 구매 실패(pack={PackIdOf(_step)}, result={t_result})");
        }

        _context.CompleteIfLast();

        PackHandoff.Set(t_opened, _step.Pack, null, false);

        // 구매는 이미 성공했다 — 연출만 생략하고 전진한다(실패 정책을 묻는 자리가 아니다).
        if (!PackOpenOverlay.TryOpen())
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 개봉 오버레이 열기 실패(pack={PackIdOf(_step)}) — 구매는 유지, 개봉 연출만 생략.");

        return EOutgameTutorialStepResult.Advanced;
    }

    static EOutgameTutorialStepResult EnterDeckGrant(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        _context.CommitAdvance();

        // ⚠ 이 스텝은 앵커가 없어 되돌리면(Halt) 이 부트에서 다시 세울 신호가 없다 —
        //   덱은 유저가 직접 만들 수 있으니 저작은 Skip으로 두는 편이 낫다.
        if (_step.Scenario == null || !DeckSaveManager.TryBuildDeck(_step.Scenario.playerDeck, out List<CardData> t_cards))
            return Fail(_step, _context, $"시나리오 미배선 또는 덱이 {DeckSaveManager.DECK_SIZE}장을 이루지 못함");

        if (DeckSaveManager.LegacyMigrationPending)
            return Fail(_step, _context, "레거시 덱 이관 미완료");

        // 저장 덱과 소유권은 별도 데이터다. 둘이 어긋난 세이브라도
        // 가이드 진입 전 실제 카드 소유를 먼저 보장한다.
        OwnershipManager.GrantAll(ToIds(t_cards));

        if (DeckSaveManager.TryFindSlot(t_cards, out _))
        {
            _context.CompleteIfLast();
            return EOutgameTutorialStepResult.Advanced;
        }

        // 목록이 가득 찼다면 이미 쓸 덱이 여섯 개 있다는 뜻이라 튜토 덱 없이도 전투가 된다.
        if (!DeckSaveManager.TryInsertFront(t_cards, _step.DeckName, DeckImages.PickRandomKey(), out _))
            return Fail(_step, _context, "덱 삽입 실패 — 목록이 가득 찼거나 세이브 미로드(DeckSaveManager 로그 확인)");

        _context.CompleteIfLast();
        return EOutgameTutorialStepResult.Advanced;
    }

    // 보상 카드를 오버레이로 보여 주고 유저가 [획득]을 눌러야 지급되는 자리.
    // 진입에 성공하면 완료를 넘기지 않는다(EnterFirstRank와 같은 이유: 뒤이을 안내의 딤이 카드 비행을 덮는다) —
    // 완료는 획득 뒤에 이어지는 로비 획득 연출이 끝나는 신호가 확정한다.
    static EOutgameTutorialStepResult EnterCardGrant(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        if (_step.Card == null)
            return FailAfterGrant(_step, _context, null, "CardGrant에 카드가 미배선");

        // 보여 줄 화면도 연출을 틀 디렉터도 없으면 기다릴 신호가 없다 — 화면 없이 지급만 하고 지나간다.
        // 캐리어를 싣지 않는 것이 중요하다: 소비되지 못한 채 살아남으면 다음 로비 진입의 획득 연출에 섞인다.
        if (!CardRewardOverlay.TryGet(out var t_overlay) || !LobbyGainEffectDirector.Exists)
            return FailAfterGrant(_step, _context, _step.Card, "보상 오버레이·획득 연출 없음(로비 씬 배선 확인)");

        var t_card = _step.Card;

        // 카드가 서 있던 자리를 함께 넘긴다 — 비행이 그 자리에서 출발해야 보상 화면과 획득 연출이 한 줄로 이어진다.
        var t_origin = t_overlay.CardAnchor;
        t_overlay.Show(RewardTitle, t_card, () => AcquireCard(t_card, t_origin));
        return EOutgameTutorialStepResult.Gated;
    }

    // [획득]이 눌린 순간. 지급을 끝내고 로비 획득 연출에 넘긴다(카드가 도감 탭으로 날아간다).
    // 화면이 뜬 뒤 클릭까지는 시간 제한이 없어, 진입 때 확인한 디렉터가 그 사이 사라질 수 있다.
    static bool AcquireCard(CardData _card, RectTransform _origin)
    {
        OwnershipManager.Grant(CardCatalog.IdOf(_card));

        CardPackRewardHandoff.Set(CurrencyGain.None, new List<CardData> { _card });
        if (LobbyGainEffectDirector.PlayNow(_origin)) return true;

        // 재생이 안 되면 캐리어가 소비되지 못한 채 살아남아 다음 로비 진입의 획득 연출에 섞인다 — 여기서 거둔다.
        CardPackRewardHandoff.TryConsume(null, out _);

        // 기다리는 스텝을 놓아준다. 이 신호가 없으면 올 리 없는 연출을 기다리며 영영 멈춘다.
        LobbyGainEffectDirector.NotifySkipped();

        Debug.LogWarning("[TutorialStepExecutor] 획득 연출을 재생하지 못해 카드 비행을 생략합니다(지급은 완료).");
        return false;
    }

    // 화면을 세우지 못한 경로의 마무리. 연출은 접더라도 소유권은 맞춰 두고 결말은 저작에 맡긴다.
    static EOutgameTutorialStepResult FailAfterGrant(TutorialStepDef _step, OutgameTutorialStepContext _context,
                                                    CardData _card, string _reason)
    {
        if (_card != null) OwnershipManager.Grant(CardCatalog.IdOf(_card));

        return Fail(_step, _context, _reason);
    }

    // 카드 묶음을 한 번에 주는 자리. EnterCardGrant와 같은 규약이라 다른 점만 적는다 —
    // 세트는 낱장 보상과 다른 오버레이(격자)를 쓰고, 지급도 한 장이 아니라 목록 단위로 한다.
    static EOutgameTutorialStepResult EnterCardSetGrant(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        var t_cards = _step.Cards;

        if (t_cards == null || t_cards.Count == 0)
            return FailAfterSetGrant(_step, _context, null, "CardSetGrant에 카드가 미배선");

        // 디렉터를 오버레이보다 먼저 본다 — 순서를 뒤집으면 로비가 아닌 씬에서도 보상 화면이 세워져 그 씬에 남는다.
        if (!LobbyGainEffectDirector.Exists || !CardSetRewardOverlay.TryGet(out var t_overlay))
            return FailAfterSetGrant(_step, _context, t_cards, "보상 오버레이·획득 연출 없음(로비 씬 배선 확인)");

        var t_origin = t_overlay.CardAnchor;
        t_overlay.Show(CardSetTitle, t_cards, () => AcquireCards(t_cards, t_origin));
        return EOutgameTutorialStepResult.Gated;
    }

    // [받기]가 눌린 순간. 지급을 끝내고 로비 획득 연출에 넘긴다(카드들이 도감 탭으로 날아간다).
    static void AcquireCards(IReadOnlyList<CardData> _cards, RectTransform _origin)
    {
        OwnershipManager.GrantAll(ToIds(_cards));

        CardPackRewardHandoff.Set(CurrencyGain.None, _cards);
        if (LobbyGainEffectDirector.PlayNow(_origin)) return;

        // 재생이 안 되면 캐리어가 소비되지 못한 채 살아남아 다음 로비 진입의 획득 연출에 섞인다 — 여기서 거둔다.
        CardPackRewardHandoff.TryConsume(null, out _);
        LobbyGainEffectDirector.NotifySkipped();

        Debug.LogWarning("[TutorialStepExecutor] 획득 연출을 재생하지 못해 카드 비행을 생략합니다(지급은 완료).");
    }

    static EOutgameTutorialStepResult FailAfterSetGrant(TutorialStepDef _step, OutgameTutorialStepContext _context,
                                                       IReadOnlyList<CardData> _cards, string _reason)
    {
        if (_cards != null) OwnershipManager.GrantAll(ToIds(_cards));

        return Fail(_step, _context, _reason);
    }

    // null 원소는 건너뛴다 — 저작이 빈 칸을 남긴 세트도 그대로 지급되어야 한다.
    static List<int> ToIds(IReadOnlyList<CardData> _cards)
    {
        var t_ids = new List<int>(_cards.Count);
        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            if (_cards[t_i] == null) continue;

            t_ids.Add(CardCatalog.IdOf(_cards[t_i]));
        }

        return t_ids;
    }

    static string Where(OutgameTutorialStepContext _context) => $"스텝 {_context.ChapterIndex}-{_context.StepIndex}";

    static string PackIdOf(TutorialStepDef _step) => _step.Pack != null ? _step.Pack.PackId : "null";
}
