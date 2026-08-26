using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>Firebase 공급자가 붙기 전까지 현재 로컬 세이브의 전체 성장값을 매치 스냅샷으로 만든다.</summary>
public sealed class LocalSaveMatchGrowthSource : IMatchGrowthSource
{
    public UniTask<CardGrowth[]> ResolveMyGrowth(IReadOnlyList<int> _deck, CancellationToken _ct)
    {
        if (_ct.IsCancellationRequested) return UniTask.FromResult<CardGrowth[]>(null);
        if (!CardGrowthManager.IsReady || !CardGrowthManager.IsConfigReady || !KeywordGrowthManager.IsReady)
        {
            Debug.LogError("[MatchGrowth] 로컬 성장 캐시/설정이 준비되지 않아 멀티 성장 스냅샷을 만들 수 없다.");
            return UniTask.FromResult<CardGrowth[]>(null);
        }

        int t_count = _deck?.Count ?? 0;
        var t_result = new CardGrowth[t_count];
        for (int i = 0; i < t_count; i++)
        {
            int t_cardId = _deck[i];
            if (!CardCatalog.Contains(t_cardId))
            {
                Debug.LogError($"[MatchGrowth] 덱의 카드 참조가 비어 있다: index={i}");
                return UniTask.FromResult<CardGrowth[]>(null);
            }
            t_result[i] = CardGrowthManager.GrowthOf(t_cardId);
        }
        return UniTask.FromResult(t_result);
    }

    public UniTask<bool> VerifyOpponentGrowth(MatchGrowthOpponent _opponent, IReadOnlyList<int> _cardIds,
                                               IReadOnlyList<CardGrowth> _growth, CancellationToken _ct)
    {
        // 개발/오프라인 fallback에는 상대 계정의 정본이 없다. 구조 검증은 수신 경로가 이미 수행하지만
        // 권위 검증은 못 한다. 인터넷 멀티 출시 전 Firebase 구현으로 반드시 교체해야 한다.
        Debug.LogWarning("[MatchGrowth] 로컬 fallback은 상대 성장 스냅샷의 서버 정본 검증을 생략한다.");
        return UniTask.FromResult(!_ct.IsCancellationRequested);
    }
}
