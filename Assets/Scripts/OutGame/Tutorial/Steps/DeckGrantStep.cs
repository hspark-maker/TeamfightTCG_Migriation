using System.Collections.Generic;
using UnityEngine;

/// <summary>이번 전투에서 쓸 덱을 유저 세이브에 미리 지급하는 자동 스텝.
/// 덱 편집으로 만들게 하지 않고 완성된 덱을 목록에 넣어, 유저가 "고르는" 동선만 배우게 한다.
///
/// 덱 정본은 시나리오다 — 전투 필드가 실제로 TutorialConfig.PlayerDeck으로 초기화되므로
/// (GameInitializer.InitializeSinglePlayerFields) 팩 풀을 쓰면 화면에 고른 덱과 실제 전투 덱이 갈린다.</summary>
[CreateAssetMenu(fileName = "Step_GrantDeck", menuName = "Card Battle/Outgame Tutorial/Step/Deck Grant")]
public class DeckGrantStep : OutgameTutorialStep
{
    [Tooltip("지급할 덱의 정본. 이 시나리오의 playerDeck이 그대로 덱 슬롯이 된다")]
    [SerializeField] TutorialScenarioData scenario;

    [Tooltip("덱 목록에 표시할 이름")]
    [SerializeField] string deckName;

    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Auto;
    public override bool LeavesScene => false;

    public override bool Enter(OutgameTutorialStepContext _context)
    {
        // 불변식: 커밋이 실행보다 앞선다(AutoPurchaseStep과 같은 이유).
        // 지급 도중 강제종료되면 덱만 생기고 좌표는 넘어간 상태가 되는데, 그건 아래 멱등 가드가 흡수한다.
        _context.CommitAdvance();

        string t_where = $"스텝 {_context.ChapterIndex}-{_context.StepIndex}('{name}')";

        // 저작 오류는 재시도해도 결과가 같다 → 롤백하지 않고 진행시킨다(같은 자리에서 무한 정지하지 않게).
        if (scenario == null || !DeckSaveManager.TryBuildDeck(scenario.playerDeck, out List<CardData> t_cards))
        {
            Debug.LogWarning($"[DeckGrantStep] {t_where} 시나리오 미배선 또는 덱이 {DeckSaveManager.DECK_SIZE}장을 이루지 못함 — 덱 지급 생략.");
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
            Debug.LogWarning($"[DeckGrantStep] {t_where} 레거시 덱 이관 미완료 — 지급 보류(다음 부트에 재시도).");
            _context.Rollback();
            return false;
        }

        // 덱 편집·도감이 소유 필터를 쓴다 — 덱만 넣으면 컬렉션에 없는 카드가 편성된 꼴이 된다(StarterDeck과 같은 처리).
        OwnershipManager.GrantAll(ToKeys(t_cards));

        // 실패하면 덱이 없어 다음 "덱 고르기" 스텝의 앵커가 등록되지 않는다 → 커밋을 되돌려 재시도한다.
        if (!DeckSaveManager.TryInsertFront(t_cards, deckName, DeckImages.PickRandomKey(), out _))
        {
            Debug.LogWarning($"[DeckGrantStep] {t_where} 덱 삽입 실패 — 목록이 가득 찼거나 세이브 미로드(DeckSaveManager 로그 확인). 진행도를 되돌린다.");
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
}
