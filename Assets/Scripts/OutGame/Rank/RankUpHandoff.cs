// 전투 씬에서 정산한 랭크 결과를 로비 씬까지 실어 나르는 씬 캐리어(자체 세이브 없음)
public static class RankUpHandoff
{
    // 병합된 값의 의미는 "시작 티어 → 도달 티어, 그 사이 총 증감"이다(1회 전투 스냅샷이 아니다)
    static RankApplyResult? s_pending;

    // 정산 결과 싣기(로비 도달 전 연속 전투는 도달 최고 티어로 병합)
    public static void Set(in RankApplyResult _result)
    {
        if (s_pending.HasValue)
        {
            var t_prev = s_pending.Value;
            if (t_prev.TierIndex >= _result.TierIndex) return;

            s_pending = new RankApplyResult(t_prev.Delta + _result.Delta, t_prev.PrevTierIndex, _result.TierIndex);
            return;
        }

        s_pending = _result;
    }

    // 결과를 꺼내고 홀더를 비운다(1회 소비, 없으면 default + false)
    public static bool TryConsume(out RankApplyResult _result)
    {
        if (!s_pending.HasValue)
        {
            _result = default;
            return false;
        }

        _result = s_pending.Value;
        s_pending = null;
        return true;
    }
}
