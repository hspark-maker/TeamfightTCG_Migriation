using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

// 스텝 행 하나를 실행하는 단일 창구
public static class TutorialStepExecutor
{
    const string BattleScene = "BattleScene";

    // 보상 오버레이 제목의 폴백. 정본은 스텝 저작(rewardTitle)이고, 비어 있을 때만 이 문구가 선다 —
    // 제목 없는 보상 화면을 띄우느니 덜 맞는 문구라도 서는 편이 낫다.
    const string DefaultRewardTitle  = "첫 승리 보너스";
    const string DefaultCardSetTitle = "기본 카드 세트";
    const string DefaultPackNoticeTitle = "무료 카드팩 도착";

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
            case EOutgameTutorialAction.WaitKeywordEnhance:
            case EOutgameTutorialAction.WaitLobbyReturn:
            case EOutgameTutorialAction.WaitCardDetailReturn:
            case EOutgameTutorialAction.WaitDeckEquip:
            case EOutgameTutorialAction.WaitDeckSave:
                return EOutgameTutorialStepResult.Gated;

            case EOutgameTutorialAction.CloseCardDetail: return EnterCloseCardDetail(_context);
            case EOutgameTutorialAction.CloseAlbumPage: return EnterCloseAlbumPage(_context);
            case EOutgameTutorialAction.CloseDeckEdit: return EnterCloseDeckEdit(_context);
            case EOutgameTutorialAction.EnterFirstRank: return EnterFirstRank(_context);
            case EOutgameTutorialAction.BattleEntry:  return EnterBattleEntry(_step, _context);
            case EOutgameTutorialAction.AutoBattle:   return EnterAutoBattle(_step, _context);
            case EOutgameTutorialAction.AutoPurchase: return EnterAutoPurchase(_step, _context);
            case EOutgameTutorialAction.DeckGrant:    return EnterDeckGrant(_step, _context);
            case EOutgameTutorialAction.CardGrant:    return EnterCardGrant(_step, _context);
            case EOutgameTutorialAction.CardSetGrant: return EnterCardSetGrant(_step, _context);
            case EOutgameTutorialAction.PackNotice:   return EnterPackNotice(_step, _context);
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
    /// Halt는 되돌려 그 자리에 세우고(재시도는 다음 초기화), 호출자는 Failed를 받아 기능 잠금을 연다.</summary>
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
            // 볼 연출이 없으니 "연출이 끝난 뒤"가 곧 지금이다 — 트리거 문도 여기서 연다.
            TriggeredTutorialRunner.NotifyRankPromotionFinished();

            _context.CommitAdvance();
            _context.CompleteIfLast();
            return EOutgameTutorialStepResult.Advanced;
        }

        RankResultHandoff.Set(t_entry);

        // 문은 여기서 열지 않는다 — 연출을 다 본 뒤(Completion.RankEffect)가 여는 자리이고,
        // 그 신호를 받는 브리지가 연다.
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

    // 도감 페이지를 걷어 안내가 시작된 테마 화면으로 무대를 돌려놓는다(다음 안내가 그 화면을 가리킨다).
    static EOutgameTutorialStepResult EnterCloseAlbumPage(OutgameTutorialStepContext _context)
    {
        AlbumPageOverlayView.CloseOpen();

        _context.CommitAdvance();
        _context.CompleteIfLast();
        return EOutgameTutorialStepResult.Advanced;
    }

    // 저장이 끝난 덱 편집을 걷어 그 아래 로비 표면을 드러낸다(다음 안내가 로비 위젯을 가리킨다).
    // 이탈 확인 팝업은 끼지 않는다 — 앞선 저장 스텝이 변경사항을 이미 확정해 두었다.
    static EOutgameTutorialStepResult EnterCloseDeckEdit(OutgameTutorialStepContext _context)
    {
        // 이미 닫혀 있으면 걷을 것이 없다(유저가 먼저 나갔거나 다른 화면에서 재생된 경우) — 실패가 아니라 정상 통과다.
        if (!DeckEditController.TryRequestExitOpen())
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 덱 편집이 열려 있지 않아 닫기를 생략합니다.");

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

        // 결제는 서버 왕복이라 이 동기 상태머신이 결과를 기다릴 수 없다 — 살 수 있는지만 먼저 묻고
        // 그 답으로 저작된 실패 정책을 태운다(여기까지가 되돌릴 수 있는 마지막 지점).
        var t_precheck = CardPackOpener.Precheck(_step.PackId);
        if (t_precheck != EPackOpenResult.Success)
            return Fail(_step, _context, $"자동 구매 실패(pack={PackIdOf(_step)}, result={t_precheck})");

        _context.CommitAdvance();
        _context.CompleteIfLast();

        PurchaseAndOpenAsync(_step.PackId, Where(_context)).Forget();

        return EOutgameTutorialStepResult.Advanced;
    }

    // 자동 구매의 서버 왕복. 좌표는 이미 전진한 뒤라 되돌릴 수 없다.
    // 대기 표시와 거절 안내는 PackPurchaseFlow가 맡는다 — 이 자리는 결과로 개봉을 열지 말지만 가른다.
    // ⚠ 서버 왕복이 실패하면 좌표는 개봉 신호를 기다리는 칸에 남는다 — 되돌릴 수 없으므로 그 자리에서 문을 연다.
    static async UniTaskVoid PurchaseAndOpenAsync(string _packId, string _where)
    {
        string t_packId = !string.IsNullOrEmpty(_packId) ? _packId : "null";

        // 대기 표시의 임자로 쓸 인스턴스가 없는 static 경로다 — 타입 자체를 안정된 키로 넘긴다.
        var t_opened = await PackPurchaseFlow.PurchaseAsync(_packId, typeof(TutorialStepExecutor));
        if (t_opened == null)
        {
            // 안내는 PackPurchaseFlow가 이미 띄웠다 — 좌표는 이미 전진해 되돌리지 못한다.
            Debug.LogError($"[TutorialStepExecutor] {_where} 자동 구매 왕복 실패(pack={t_packId}) — 이미 전진해 되돌리지 못한다.");

            // 되감을 구매 스텝이 없는 갈래라, 오지 않을 개봉 신호를 기다리는 칸에 갇혀 스스로 풀 수 없다.
            // 망 오류 한 번에 초기화 3회(SameCoordBootCount)를 거듭하게 두지 않으려고 여기서 문을 연다(멱등).
            OutgameFeatureLock.NotifyStalled();
            return;
        }

        PackHandoff.Set(t_opened, _packId, null, false);

        // 열지 못해도 결제는 이미 나갔다 — 연출만 생략하고 전진한다(실패 정책을 묻는 자리가 아니다).
        if (!PackOpenOverlay.TryOpen())
            Debug.LogWarning($"[TutorialStepExecutor] {_where} 개봉 오버레이 열기 실패(pack={t_packId}) — 구매는 유지, 개봉 연출만 생략.");
    }

    static EOutgameTutorialStepResult EnterDeckGrant(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        _context.CommitAdvance();

        // ⚠ 이 스텝은 앵커가 없어 되돌리면(Halt) 이 초기화에서 다시 세울 신호가 없다 —
        //   덱은 유저가 직접 만들 수 있으니 저작은 Skip으로 두는 편이 낫다.
        if (_step.Scenario == null || !DeckSaveManager.TryBuildDeck(_step.Scenario.PlayerDeckIds, out List<int> t_cards))
            return Fail(_step, _context, $"시나리오 미배선 또는 덱이 {DeckSaveManager.DECK_SIZE}장을 이루지 못함");

        _context.CompleteIfLast();

        // 삽입이 왕복보다 앞선다 — ServerSaveCommands.InvokeAsync 안에서 시작되는 업로드 봉인 밖에서 저장을 끝내
        // 채택이 세우는 업로드 기준선과 경합하지 않고, 바로 다음 스텝(전투 진입)의 덱 게이트가 빈 슬롯을 보지 않는다.
        // 그 사이 덱 카드가 잠시 미소유일 수 있으나 덱 저장은 클라 권한이고 lockDeck 재검증은 멀티 진입에만 걸린다(튜토 전투는 싱글).
        // 지급한 덱을 대표로 세운다 — TryInsertFront가 앞칸에 끼우며 대표 좌표를 옛 덱 쪽으로 밀어 두므로,
        // 세우지 않으면 덱 탭이 엉뚱한 덱을 열고 뒤따르는 카드 장착 스텝이 영구 대기한다.
        if (DeckSaveManager.TryFindSlot(t_cards, out int t_index) ||
            DeckSaveManager.TryInsertFront(t_cards, _step.DeckName, DeckImages.PickRandomKey(), out t_index))
            DeckSaveManager.TrySelectSlot(t_index);
        else
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 덱 삽입 실패 — 목록이 가득 찼거나 세이브 미로드(DeckSaveManager 로그 확인).");

        RequestGrant(GrantPackIdOf(_step, Where(_context)));

        return EOutgameTutorialStepResult.Advanced;
    }

    // 보상 카드를 오버레이로 보여 주고 유저가 [획득]을 눌러야 지급되는 자리.
    // 진입에 성공하면 완료를 넘기지 않는다(EnterFirstRank와 같은 이유: 뒤이을 안내의 딤이 카드 비행을 덮는다) —
    // 완료는 획득 뒤에 이어지는 로비 획득 연출이 끝나는 신호가 확정한다.
    static EOutgameTutorialStepResult EnterCardGrant(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        if (_step.CardId <= 0)
            return FailAfterGrant(_step, _context, "CardGrant에 카드 ID가 미배선");

        // 보여 줄 화면도 연출을 틀 디렉터도 없으면 기다릴 신호가 없다 — 화면 없이 지급만 하고 지나간다.
        // 캐리어를 싣지 않는 것이 중요하다: 소비되지 못한 채 살아남으면 다음 로비 진입의 획득 연출에 섞인다.
        if (!CardRewardOverlay.TryGet(out var t_overlay) || !LobbyGainEffectDirector.Exists)
            return FailAfterGrant(_step, _context, "보상 오버레이·획득 연출 없음(로비 씬 배선 확인)");

        int t_card = _step.CardId;

        // 카드가 서 있던 자리를 함께 넘긴다 — 비행이 그 자리에서 출발해야 보상 화면과 획득 연출이 한 줄로 이어진다.
        var t_origin = t_overlay.CardAnchor;
        bool t_parallel = _step.ParallelGain;
        string t_packId = GrantPackIdOf(_step, Where(_context));
        t_overlay.Show(TitleOf(_step, DefaultRewardTitle), t_card, () => AcquireCard(t_packId, t_card, t_origin, t_parallel));
        return EOutgameTutorialStepResult.Gated;
    }

    // [획득]이 눌린 순간. 지급을 서버에 맡기고 로비 획득 연출에 넘긴다(카드가 도감 탭으로 날아간다).
    // 화면이 뜬 뒤 클릭까지는 시간 제한이 없어, 진입 때 확인한 디렉터가 그 사이 사라질 수 있다.
    static bool AcquireCard(string _packId, int _cardId, RectTransform _origin, bool _parallel)
    {
        // 연출은 왕복을 기다리지 않는다 — [획득]의 반응성을 네트워크에 묶지 않는다(소유는 응답 채택이 뒤따라 맞춘다).
        RequestGrant(_packId);

        CardPackRewardHandoff.Set(CurrencyGain.None, new List<int> { _cardId });
        if (LobbyGainEffectDirector.PlayNow(_origin))
        {
            // 저작이 병렬을 시켰으면 비행이 끝나기를 기다리지 않는다 — 다음 안내가 그 비행과 나란히 선다.
            if (_parallel) LobbyGainEffectDirector.NotifyDetached();
            return true;
        }

        // 재생이 안 되면 캐리어가 소비되지 못한 채 살아남아 다음 로비 진입의 획득 연출에 섞인다 — 여기서 거둔다.
        CardPackRewardHandoff.TryConsume(null, out _);

        // 기다리는 스텝을 놓아준다. 이 신호가 없으면 올 리 없는 연출을 기다리며 영영 멈춘다.
        LobbyGainEffectDirector.NotifySkipped();

        Debug.LogWarning("[TutorialStepExecutor] 획득 연출을 재생하지 못해 카드 비행을 생략합니다(지급 요청은 보냈다).");
        return false;
    }

    // 화면을 세우지 못한 경로의 마무리. 지급을 요청해 두고 결말은 저작에 맡긴다.
    // 서버 이관 후 "화면을 못 세워도 소유는 준다"는 보장은 best-effort 로 격하됐다(왕복이 실패하면 안 들어온다).
    static EOutgameTutorialStepResult FailAfterGrant(TutorialStepDef _step, OutgameTutorialStepContext _context, string _reason)
    {
        // 무엇을 주는지는 서버가 팩 ID로 정한다 — 저작 카드 ID가 아니라 팩 배선 여부가 요청을 보낼 수 있는지의 기준이다.
        RequestGrant(GrantPackIdOf(_step, Where(_context)));

        return Fail(_step, _context, _reason);
    }

    // 카드 묶음을 한 번에 주는 자리. EnterCardGrant와 같은 규약이라 다른 점만 적는다 —
    // 세트는 낱장 보상과 다른 오버레이(격자)를 쓰고, 지급도 한 장이 아니라 목록 단위로 한다.
    static EOutgameTutorialStepResult EnterCardSetGrant(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        var t_cards = _step.CardIds;

        if (t_cards == null || t_cards.Count == 0)
            return FailAfterSetGrant(_step, _context, "CardSetGrant에 카드가 미배선");

        // 디렉터를 오버레이보다 먼저 본다 — 순서를 뒤집으면 로비가 아닌 씬에서도 보상 화면이 세워져 그 씬에 남는다.
        if (!LobbyGainEffectDirector.Exists || !CardSetRewardOverlay.TryGet(out var t_overlay))
            return FailAfterSetGrant(_step, _context, "보상 오버레이·획득 연출 없음(로비 씬 배선 확인)");

        var t_origin = t_overlay.CardAnchor;
        bool t_parallel = _step.ParallelGain;
        string t_packId = GrantPackIdOf(_step, Where(_context));
        t_overlay.Show(TitleOf(_step, DefaultCardSetTitle), t_cards, () => AcquireCards(t_packId, t_cards, t_origin, t_parallel));
        return EOutgameTutorialStepResult.Gated;
    }

    // [받기]가 눌린 순간. 지급을 서버에 맡기고 로비 획득 연출에 넘긴다(카드들이 도감 탭으로 날아간다).
    static void AcquireCards(string _packId, IReadOnlyList<int> _cards, RectTransform _origin, bool _parallel)
    {
        // 연출은 왕복을 기다리지 않는다 — [받기]의 반응성을 네트워크에 묶지 않는다(소유는 응답 채택이 뒤따라 맞춘다).
        RequestGrant(_packId);

        CardPackRewardHandoff.Set(CurrencyGain.None, _cards);
        if (LobbyGainEffectDirector.PlayNow(_origin))
        {
            // 저작이 병렬을 시켰으면 비행이 끝나기를 기다리지 않는다 — 다음 안내가 그 비행과 나란히 선다.
            if (_parallel) LobbyGainEffectDirector.NotifyDetached();
            return;
        }

        // 재생이 안 되면 캐리어가 소비되지 못한 채 살아남아 다음 로비 진입의 획득 연출에 섞인다 — 여기서 거둔다.
        CardPackRewardHandoff.TryConsume(null, out _);
        LobbyGainEffectDirector.NotifySkipped();

        Debug.LogWarning("[TutorialStepExecutor] 획득 연출을 재생하지 못해 카드 비행을 생략합니다(지급 요청은 보냈다).");
    }

    // 팩이 도착했음을 알리는 자리. 지급도 구매도 하지 않는다 —
    // 실제 획득은 이 뒤의 상점 스텝 몫이라, 실패해도 되돌릴 것이 없다.
    //
    // [확인]을 누르면 팩이 팩 탭으로 빨려들고, 그 비행이 끝난 뒤에야 손가락 안내(다음 스텝)가 선다 —
    // 탭을 대신 눌러 주지는 않는다. 화면이 저절로 바뀌면 그 이동이 이 팝업과 이어진 한 줄로 읽히지 않는다.
    //
    // 진입에 성공하면 완료를 넘기지 않는다(EnterCardGrant와 같은 규약) — 완료는 그 비행의 종료 신호가 확정한다.
    static EOutgameTutorialStepResult EnterPackNotice(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        if (string.IsNullOrEmpty(_step.PackId))
            return Fail(_step, _context, "PackNotice에 팩이 미배선");

        // 디렉터를 오버레이보다 먼저 본다 — 순서를 뒤집으면 로비가 아닌 씬에서도 팝업이 세워져 그 씬에 남는다.
        if (!LobbyGainEffectDirector.Exists || !PackRewardOverlay.TryGet(out var t_overlay))
            return Fail(_step, _context, "예고 오버레이·획득 연출 없음(로비 씬 배선 확인)");

        // 팩이 서 있던 자리를 함께 넘긴다 — 비행이 그 자리에서 출발해야 팝업과 탭이 한 줄로 이어진다.
        var t_origin   = t_overlay.PackAnchor;
        var t_art      = PackSpec.Art(_step.PackId);
        bool t_parallel = _step.ParallelGain;

        t_overlay.Show(TitleOf(_step, DefaultPackNoticeTitle), _step.PackId, () => FlyPackToTab(t_art, t_origin, t_parallel));
        return EOutgameTutorialStepResult.Gated;
    }

    // [확인]이 눌린 순간. 로비 획득 연출에 넘긴다(팩이 팩 탭으로 날아간다) — 지급은 없다, 예고일 뿐이다.
    // 화면이 뜬 뒤 클릭까지는 시간 제한이 없어, 진입 때 확인한 디렉터가 그 사이 사라질 수 있다.
    static void FlyPackToTab(Sprite _art, RectTransform _origin, bool _parallel)
    {
        if (LobbyGainEffectDirector.PlayPackFlight(_art, _origin))
        {
            // 저작이 병렬을 시켰으면 비행이 끝나기를 기다리지 않는다 — 손가락이 그 비행과 나란히 선다.
            if (_parallel) LobbyGainEffectDirector.NotifyDetached();
            return;
        }

        // 기다리는 스텝을 놓아준다. 이 신호가 없으면 올 리 없는 연출을 기다리며 영영 멈춘다.
        LobbyGainEffectDirector.NotifySkipped();

        Debug.LogWarning("[TutorialStepExecutor] 팩 비행을 재생하지 못해 생략합니다(안내는 계속 진행).");
    }

    // FailAfterGrant와 같은 규약 — 소유 보장은 서버 이관 후 best-effort 다.
    static EOutgameTutorialStepResult FailAfterSetGrant(TutorialStepDef _step, OutgameTutorialStepContext _context, string _reason)
    {
        RequestGrant(GrantPackIdOf(_step, Where(_context)));

        return Fail(_step, _context, _reason);
    }

    static string TitleOf(TutorialStepDef _step, string _fallback)
    {
        string t_title = _step.RewardTitle;

        return string.IsNullOrEmpty(t_title) ? _fallback : t_title;
    }

    static string Where(OutgameTutorialStepContext _context) => $"스텝 {_context.ChapterIndex}-{_context.StepIndex}";

    static string PackIdOf(TutorialStepDef _step) => !string.IsNullOrEmpty(_step.PackId) ? _step.PackId : "null";

    // 지급 왕복에 실을 팩 ID(미배선이면 null). 서버는 이 값으로 CardPackDrop 표를 찾아 줄 카드를 정한다 —
    // 저작이 비면 보낼 키가 없어 화면만 서고 소유는 늘지 않으므로 그 자리에서 소리내어 남긴다.
    static string GrantPackIdOf(TutorialStepDef _step, string _where)
    {
        string t_packId = _step.PackId;

        if (string.IsNullOrEmpty(t_packId))
            Debug.LogError($"[TutorialStepExecutor] {_where} {_step.Action}에 지급 팩이 미배선 — 지급 요청을 보내지 않습니다(스텝 저작의 pack 확인).");

        return t_packId;
    }

    // 결손은 GrantPackIdOf가 이미 알렸다 — 여기서는 보낼 키가 없는 요청만 조용히 접는다.
    static void RequestGrant(string _packId)
    {
        if (!string.IsNullOrEmpty(_packId)) TutorialGrantCommand.GrantAsync(_packId).Forget();
    }
}
