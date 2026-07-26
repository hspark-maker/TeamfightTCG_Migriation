using System.Collections.Generic;
using UnityEngine;

// CardPack 씬 단독 실행용 더미 개봉 세션 주입(테스트 씬 전용).
// 정상 진입(상점/부트가 PackHandoff를 채우고 이 씬을 로드)이면 아무것도 하지 않는다 — 실제 세션이 항상 우선.
// 구매·차감·소유 부여는 하지 않는다(연출 배선 검증용 껍데기 결과). 경제 계약은 CardPackOpener만 건드린다.
public class PackStandaloneBoot : MonoBehaviour
{
    [Header("더미 개봉 (PackHandoff 미배선일 때만)")]
    [Tooltip("더미 카드의 출처 팩. 풀 앞에서 DrawCount장을 순서대로 집는다(랜덤 아님 — 재현 가능).")]
    [SerializeField] CardPackData dummyPack;
    [Tooltip("팩 미배선 시 쓸 카드 목록. 팩이 있으면 무시된다.")]
    [SerializeField] List<CardData> dummyCards = new List<CardData>();

    [Header("획득 후 목적지")]
    [Tooltip("비우면 획득해도 씬 전이 없이 이 씬에 머문다(단독 실행 기본값).")]
    [SerializeField] string nextScene = "";
    [Tooltip("체크 시 획득 후 튜토리얼 시작 — nextScene이 전투 씬일 때만 의미 있다.")]
    [SerializeField] bool startTutorial;

    // PackAcquireController가 Start에서 캐리어를 읽으므로 그보다 앞선 Awake에서 채운다.
    void Awake()
    {
        if (PackHandoff.HasPending) return;   // 실제 진입 — 더미로 덮지 않는다.

        var t_cards = ResolveCards();
        if (t_cards.Count == 0)
        {
            Debug.LogWarning("[PackStandaloneBoot] 더미 카드 없음(dummyPack/dummyCards 미배선) — 주입 생략.");
            return;
        }

        // 더미는 전부 신규·환급 0으로 고정(소유 세이브를 읽지 않으므로 사후 판정 불가).
        var t_drawn = new List<DrawnCard>(t_cards.Count);
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
            t_drawn.Add(new DrawnCard(t_cards[t_i], true, 0));

        var t_packId = dummyPack != null ? dummyPack.PackId : "DummyPack";
        PackHandoff.Set(OpenedPack.CreateSuccess(t_packId, t_drawn), nextScene, startTutorial);

        Debug.Log($"[PackStandaloneBoot] 단독 실행 — 더미 개봉 세션 주입(packId={t_packId}, {t_drawn.Count}장).");
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
