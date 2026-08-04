using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>스텝 행 하나를 실행하는 단일 창구. 스텝 SO 10종의 Enter()를 액션 switch 하나로 접었다.
/// 반환 true = 이 씬에서 앵커에 게이트를 걸어야 함(false면 자동 처리·씬 전환으로 이 씬의 할 일은 끝).</summary>
public static class TutorialStepExecutor
{
    // 전투 씬. 저작 데이터가 아니라 시스템 고정 경로라 행 필드가 아닌 상수로 둔다.
    const string BattleScene = "BattleScene";

    public static bool Enter(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        if (_step == null) return false;

        switch (_step.Action)
        {
            // 진입 시 할 일이 없다 — 게이트를 걸고 완료 신호를 기다리는 게 전부.
            case EOutgameTutorialAction.WaitClick:
            case EOutgameTutorialAction.Message:
            case EOutgameTutorialAction.WaitPurchase:
            case EOutgameTutorialAction.WaitPackOpen:
            case EOutgameTutorialAction.DeckAutoEquip:
            case EOutgameTutorialAction.BattleStart:
                return true;

            case EOutgameTutorialAction.BattleEntry:  return EnterBattleEntry(_step, _context);
            case EOutgameTutorialAction.AutoBattle:   return EnterAutoBattle(_step, _context);
            case EOutgameTutorialAction.AutoPurchase: return EnterAutoPurchase(_step, _context);
            case EOutgameTutorialAction.DeckGrant:    return EnterDeckGrant(_step, _context);
        }

        // 열거형 밖의 값 = 저작 데이터 손상. 재시도해도 결과가 같으므로 같은 자리에서 멈추지 않게 넘긴다.
        Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 알 수 없는 액션({(int)_step.Action}) — 건너뜁니다.");
        _context.CommitAdvance();
        _context.CompleteIfLast();
        return false;
    }

    // 클릭 리스너가 아니라 진입 시 미리 시작한다 — PlayBtn의 씬 PersistentCall(StartAiBattle)이
    // 런타임 리스너보다 먼저 돌기 때문. 그 진입점이 여기서 세운 ShowDeckGate·EnemyDeck을 읽으므로
    // 클릭 시점엔 이미 채워져 있어야 한다. Begin은 멱등이라 재진입도 안전.
    static bool EnterBattleEntry(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        if (_step.Scenario == null)
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} BattleEntry에 시나리오가 미배선 — 일반 전투로 진입합니다.");

        TutorialConfig.Begin(_step.Scenario, _step.ShowDeckGate);
        return true;
    }

    static bool EnterAutoBattle(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        // AutoPurchase와 같은 불변식: 커밋이 실행보다 앞선다. 씬을 떠나면 되돌릴 지점이 없어,
        // 커밋을 미루면 전투 중 강제종료가 이 스텝을 영원히 되풀이한다.
        _context.CommitAdvance();
        _context.CompleteIfLast();

        if (_step.Scenario == null)
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} AutoBattle에 시나리오가 미배선 — 일반 전투로 진입합니다.");

        // 양 덱은 TutorialConfig가 고정 주입한다(GameInitializer) → 저장 덱이 없는 첫 실행도 그대로 진입 가능.
        TutorialConfig.Begin(_step.Scenario, _step.ShowDeckGate);
        SceneManager.LoadScene(BattleScene);
        return false;
    }

    static bool EnterAutoPurchase(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        // 열 화면이 없으면 사지 않는다 — 결제 뒤엔 되돌릴 수 없어(아래 TryOpen 실패 주석 참조)
        // 커밋 전에 끊는 것이 유일한 안전판이다. 다음 부트에 재시도된다.
        if (PackOpenOverlay.Instance == null)
        {
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 개봉 오버레이 미배치 — 구매 보류(로비 씬 배선 확인).");
            return false;
        }

        // 불변식: 커밋이 실행보다 앞선다. 구매 직후 강제종료 시 "소유는 생겼는데 진행도는 0"이 되어
        // 레거시 마이그레이션이 온보딩을 영구 스킵시키는 구멍을 원천 봉쇄한다. 순서를 바꾸지 말 것.
        _context.CommitAdvance();

        var t_opened = CardPackOpener.TryPurchase(_step.Pack, _step.DuplicateRefundGold);
        if (t_opened == null || !t_opened.Success)
        {
            // 실패는 차감 없이 반환되므로(TryPurchase 보장) 커밋만 되돌리면 원상복구된다 — 다음 부트에 재시도.
            _context.Rollback();

            string t_result = t_opened != null ? t_opened.Result.ToString() : "null";
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 자동 구매 실패(pack={PackIdOf(_step)}, result={t_result}) — 개봉 없이 유지.");
            return false;
        }

        // 마지막 스텝이 자동 구매인 저작도 완료로 닫는다(진행도가 시퀀스 끝에 멈춰 재개 불가가 되지 않게).
        _context.CompleteIfLast();

        // 목적지는 비운다 — 개봉은 제자리(오버레이)에서 끝난다. 전투 진입은 BattleEntry 행(로비 PlayBtn)이 담당하므로
        // 캐리어의 튜토리얼 시작도 항상 false다.
        PackHandoff.Set(t_opened, _step.Pack, null, false);

        // 오버레이가 안 열려도 Rollback하지 않는다 — 구매는 이미 원자 영속돼 되돌릴 수 없고,
        // 진행도만 되돌리면 다음 부트에 같은 팩을 또 사서 골드가 이중으로 나간다.
        if (!PackOpenOverlay.TryOpen())
            Debug.LogWarning($"[TutorialStepExecutor] {Where(_context)} 개봉 오버레이 열기 실패(pack={PackIdOf(_step)}) — 구매는 유지, 개봉 연출만 생략.");

        return false;
    }

    // 덱 편집으로 만들게 하지 않고 완성된 덱을 목록에 넣어, 유저가 "고르는" 동선만 배우게 한다.
    // 덱 정본은 시나리오다 — 전투 필드가 실제로 TutorialConfig.PlayerDeck으로 초기화되므로
    // (GameInitializer.InitializeSinglePlayerFields) 팩 풀을 쓰면 화면에 고른 덱과 실제 전투 덱이 갈린다.
    static bool EnterDeckGrant(TutorialStepDef _step, OutgameTutorialStepContext _context)
    {
        // 불변식: 커밋이 실행보다 앞선다(AutoPurchase와 같은 이유).
        // 지급 도중 강제종료되면 덱만 생기고 좌표는 넘어간 상태가 되는데, 그건 아래 멱등 가드가 흡수한다.
        _context.CommitAdvance();

        string t_where = Where(_context);

        // 저작 오류는 재시도해도 결과가 같다 → 롤백하지 않고 진행시킨다(같은 자리에서 무한 정지하지 않게).
        if (_step.Scenario == null || !DeckSaveManager.TryBuildDeck(_step.Scenario.playerDeck, out List<CardData> t_cards))
        {
            Debug.LogWarning($"[TutorialStepExecutor] {t_where} 시나리오 미배선 또는 덱이 {DeckSaveManager.DECK_SIZE}장을 이루지 못함 — 덱 지급 생략.");
            _context.CompleteIfLast();
            return false;
        }

        // 되감기·재진입으로 두 번 들어와도 같은 덱을 또 만들지 않는다(슬롯 6칸이 금방 찬다).
        if (DeckSaveManager.TryFindSlot(t_cards, out _))
        {
            _context.CompleteIfLast();
            return false;
        }

        // 구 decks.json이 아직 세이브로 넘어오지 못했다 — 지금 슬롯을 쓰면 그 덱이 영영 묻힌다(StarterDeck과 같은 가드).
        if (DeckSaveManager.LegacyMigrationPending)
        {
            Debug.LogWarning($"[TutorialStepExecutor] {t_where} 레거시 덱 이관 미완료 — 지급 보류(다음 부트에 재시도).");
            _context.Rollback();
            return false;
        }

        // 덱 편집·도감이 소유 필터를 쓴다 — 덱만 넣으면 컬렉션에 없는 카드가 편성된 꼴이 된다(StarterDeck과 같은 처리).
        OwnershipManager.GrantAll(ToKeys(t_cards));

        // 실패하면 덱이 없어 다음 "덱 고르기" 스텝의 앵커가 등록되지 않는다 → 커밋을 되돌려 재시도한다.
        if (!DeckSaveManager.TryInsertFront(t_cards, _step.DeckName, DeckImages.PickRandomKey(), out _))
        {
            Debug.LogWarning($"[TutorialStepExecutor] {t_where} 덱 삽입 실패 — 목록이 가득 찼거나 세이브 미로드(DeckSaveManager 로그 확인). 진행도를 되돌린다.");
            _context.Rollback();
            return false;
        }

        _context.CompleteIfLast();
        return false;
    }

    static List<string> ToKeys(List<CardData> _cards)
    {
        var t_keys = new List<string>(_cards.Count);
        for (int t_i = 0; t_i < _cards.Count; t_i++)
            t_keys.Add(CardCatalog.KeyOf(_cards[t_i]));

        return t_keys;
    }

    // 자산 이름이 사라졌으므로 로그의 식별자는 진행 좌표가 진다 — 시퀀스에서 그 행을 바로 찾을 수 있다.
    static string Where(OutgameTutorialStepContext _context) => $"스텝 {_context.ChapterIndex}-{_context.StepIndex}";

    static string PackIdOf(TutorialStepDef _step) => _step.Pack != null ? _step.Pack.PackId : "null";
}
