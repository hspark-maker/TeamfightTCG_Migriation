using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 스텝 행 하나를 실행하는 단일 창구
public static class TutorialStepExecutor
{
    const string BattleScene = "BattleScene";

    // 스텝 진입 — 반환 true = 이 씬에서 앵커에 게이트를 걸어야 함
    public static bool Enter(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        if (_step == null) return false;

        switch (_step.Action)
        {
            case EOutgameTutorialAction.WaitClick:
            case EOutgameTutorialAction.Message:
            case EOutgameTutorialAction.WaitPurchase:
            case EOutgameTutorialAction.WaitPackOpen:
            case EOutgameTutorialAction.DeckAutoEquip:
            case EOutgameTutorialAction.BattleStart:
            case EOutgameTutorialAction.WaitAlbumInsert:
                return true;

            case EOutgameTutorialAction.BattleEntry:  return EnterBattleEntry(_step, _context);
            case EOutgameTutorialAction.AutoBattle:   return EnterAutoBattle(_step, _context);
            case EOutgameTutorialAction.AutoPurchase: return EnterAutoPurchase(_step, _context);
            case EOutgameTutorialAction.DeckGrant:    return EnterDeckGrant(_step, _context);
        }

        Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 알 수 없는 액션({(int)_step.Action}) — 건너뜁니다.");
        _context.CommitAdvance();
        _context.CompleteIfLast();
        return false;
    }

    // 시나리오는 클릭 리스너가 아니라 진입 시 세운다 — PlayBtn의 씬 PersistentCall이 런타임 리스너보다 먼저 돈다
    static bool EnterBattleEntry(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        if (_step.Scenario == null)
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} BattleEntry에 시나리오가 미배선 — 일반 전투로 진입합니다.");

        TutorialConfig.Begin(_step.Scenario, _step.ShowDeckGate);
        return true;
    }

    static bool EnterAutoBattle(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        _context.CommitAdvance();
        _context.CompleteIfLast();

        if (_step.Scenario == null)
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} AutoBattle에 시나리오가 미배선 — 일반 전투로 진입합니다.");

        TutorialConfig.Begin(_step.Scenario, _step.ShowDeckGate);
        SceneManager.LoadScene(BattleScene);
        return false;
    }

    static bool EnterAutoPurchase(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        if (PackOpenOverlay.Instance == null)
        {
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 개봉 오버레이 미배치 — 구매 보류(로비 씬 배선 확인).");
            return false;
        }

        _context.CommitAdvance();

        var t_opened = CardPackOpener.TryPurchase(_step.Pack);
        if (t_opened == null || !t_opened.Success)
        {
            _context.Rollback();

            string t_result = t_opened != null ? t_opened.Result.ToString() : "null";
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 자동 구매 실패(pack={PackIdOf(_step)}, result={t_result}) — 개봉 없이 유지.");
            return false;
        }

        _context.CompleteIfLast();

        PackHandoff.Set(t_opened, _step.Pack, null, false);

        if (!PackOpenOverlay.TryOpen())
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 개봉 오버레이 열기 실패(pack={PackIdOf(_step)}) — 구매는 유지, 개봉 연출만 생략.");

        return false;
    }

    static bool EnterDeckGrant(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        _context.CommitAdvance();

        string t_where = Where(_context);

        if (_step.Scenario == null || !DeckSaveManager.TryBuildDeck(_step.Scenario.playerDeck, out List<CardData> t_cards))
        {
            Debug.LogWarning($"[TutorialStepExecutor] {t_where} 시나리오 미배선 또는 덱이 {DeckSaveManager.DECK_SIZE}장을 이루지 못함 — 덱 지급 생략.");
            _context.CompleteIfLast();
            return false;
        }

        if (DeckSaveManager.TryFindSlot(t_cards, out _))
        {
            _context.CompleteIfLast();
            return false;
        }

        if (DeckSaveManager.LegacyMigrationPending)
        {
            Debug.LogWarning($"[TutorialStepExecutor] {t_where} 레거시 덱 이관 미완료 — 지급 보류(다음 부트에 재시도).");
            _context.Rollback();
            return false;
        }

        OwnershipManager.GrantAll(ToIds(t_cards));

        if (!DeckSaveManager.TryInsertFront(t_cards, _step.DeckName, DeckImages.PickRandomKey(), out _))
        {
            Debug.LogWarning($"[TutorialStepExecutor] {t_where} 덱 삽입 실패 — 목록이 가득 찼거나 세이브 미로드(DeckSaveManager 로그 확인). 진행도를 되돌린다.");
            _context.Rollback();
            return false;
        }

        _context.CompleteIfLast();
        return false;
    }

    static List<int> ToIds(List<CardData> _cards)
    {
        var t_ids = new List<int>(_cards.Count);
        for (int t_i = 0; t_i < _cards.Count; t_i++)
            t_ids.Add(CardCatalog.IdOf(_cards[t_i]));

        return t_ids;
    }

    static string Where(OutgameTutorialStepContext _context) => $"스텝 {_context.ChapterIndex}-{_context.StepIndex}";

    static string PackIdOf(TutorialStepDef _step) => _step.Pack != null ? _step.Pack.PackId : "null";
}
