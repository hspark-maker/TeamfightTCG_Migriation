using System.Collections.Generic;
using UnityEngine;

// CardPack 테스트 씬 단독 실행용 더미 개봉 세션 주입 + 오버레이 열기(테스트 씬 전용).
// 로비를 거치지 않고 개봉 연출을 검증하는 루프를 지킨다 — 상점의 구매 경로를 껍데기로 재현한 것.
// 개봉 1회로 끝난다(획득 후 다시 보려면 Play를 다시 누른다) — 재개봉 트리거는 두지 않는다.
// 정상 진입(상점/부트가 PackHandoff를 채운 상태)이면 주입은 건너뛴다 — 실제 세션이 항상 우선.
// 구매·차감·소유 부여는 하지 않는다(연출 배선 검증용 껍데기 결과). 경제 계약은 CardPackOpener만 건드린다.
public class PackStandaloneBoot : MonoBehaviour
{
    [Header("더미 개봉 (PackHandoff 미배선일 때만)")]
    [Tooltip("더미 카드의 출처 팩. 풀 앞에서 DrawCount장을 순서대로 집는다(랜덤 아님 — 재현 가능).")]
    [SerializeField] CardPackData dummyPack;
    [Tooltip("팩 미배선 시 쓸 카드 목록. 팩이 있으면 무시된다.")]
    [SerializeField] List<CardData> dummyCards = new List<CardData>();

    [Header("신규/중복 섞기")]
    [Tooltip("켜면 홀수 번째 카드를 중복으로 만든다. 신규 연출만 반복되면 중복 표현을 검증할 수 없다.")]
    [SerializeField] bool alternateDuplicates;
    [Tooltip("중복으로 만든 카드에 붙일 환급 Gold(표시용 — 실제 지갑은 건드리지 않는다).")]
    [Min(0)] [SerializeField] long dummyRefund = 10;

    [Header("획득 후 목적지")]
    [Tooltip("비우면 획득해도 씬 전이 없이 이 씬에 머문다(단독 실행 기본값).")]
    [SerializeField] string nextScene = "";
    [Tooltip("체크 시 획득 후 튜토리얼 시작 — nextScene이 전투 씬일 때만 의미 있다.")]
    [SerializeField] bool startTutorial;

    // 캐리어는 어떤 Start보다 먼저 차 있어야 한다 — 여는 쪽이 Start라 주입은 Awake다.
    void Awake()
    {
        if (PackHandoff.HasPending) return;   // 실제 진입 — 더미로 덮지 않는다.

        var t_cards = ResolveCards();
        if (t_cards.Count == 0)
        {
            Debug.LogWarning("[PackStandaloneBoot] 더미 카드 없음(dummyPack/dummyCards 미배선) — 주입 생략.");
            return;
        }

        // 소유 세이브를 읽지 않으므로 신규 여부는 사후 판정이 불가하다 — 여기서 저작한 값을 그대로 태운다.
        var t_drawn = new List<DrawnCard>(t_cards.Count);
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            bool t_isNew = !alternateDuplicates || (t_i % 2 == 0);
            t_drawn.Add(new DrawnCard(t_cards[t_i], t_isNew, t_isNew ? 0 : dummyRefund));
        }

        var t_packId = dummyPack != null ? dummyPack.PackId : "DummyPack";
        PackHandoff.Set(OpenedPack.CreateSuccess(t_packId, t_drawn), dummyPack, nextScene, startTutorial);

        Debug.Log($"[PackStandaloneBoot] 단독 실행 — 더미 개봉 세션 주입(packId={t_packId}, {t_drawn.Count}장).");
    }

    // 오버레이가 Awake에서 Instance를 선점하므로 열기는 Start까지 미룬다.
    void Start()
    {
        if (!PackHandoff.HasPending) return;   // 주입이 생략됐으면 열 팩이 없다.

        PackOpenOverlay.TryOpen();
    }

    // 팩 풀 앞에서 DrawCount장(풀이 짧으면 순환). 팩 미배선이면 직접 배선한 목록.
    List<CardData> ResolveCards()
    {
        var t_result = new List<CardData>();

        if (dummyPack == null || dummyPack.PoolCount == 0)
        {
            for (int t_i = 0; t_i < dummyCards.Count; t_i++)
                if (dummyCards[t_i] != null) t_result.Add(dummyCards[t_i]);
            return t_result;
        }

        var t_pool = dummyPack.Pool;
        for (int t_i = 0; t_i < dummyPack.DrawCount; t_i++)
        {
            var t_card = t_pool[t_i % t_pool.Count];
            if (t_card != null) t_result.Add(t_card);
        }
        return t_result;
    }
}
