// 전투 씬에서 정산한 랭크 결과를 로비 씬까지 실어 나르는 씬 캐리어.
// 승급 보상 패널은 로비에서만 열 수 있는데 정산은 전투 씬에서 끝나므로, 그 사이를 잇는 휘발 컨텍스트가 필요하다.
// 자체 세이브 없음 — 포인트는 RankManager.ApplyBattleResult가 이미 즉시 영속했다.
public static class RankUpHandoff
{
    // 대기 중인 정산 결과. 홀더 하나뿐이라 "pending 여부"와 "값"이 갈라질 수 없다.
    // 병합된 값의 의미는 "로비까지 오는 동안 시작 티어 → 도달 티어, 그 사이 총 증감"이다(1회 전투 스냅샷이 아니다).
    static RankApplyResult? s_pending;

    /// <summary>정산 결과를 싣는다. 로비 도달 전 연속 전투가 나도 도달 최고 티어가 남도록 병합한다.</summary>
    public static void Set(in RankApplyResult _result)
    {
        if (s_pending.HasValue)
        {
            var t_prev = s_pending.Value;
            if (t_prev.TierIndex >= _result.TierIndex) return;                       // 강등·동일 티어는 승급 연출거리가 없으니 기존 값을 지킨다.

            // 시작점은 처음 실었던 티어로 유지하고 증감은 누적해야 구간(시작~도달)과 델타의 기준이 어긋나지 않는다.
            s_pending = new RankApplyResult(t_prev.Delta + _result.Delta, t_prev.PrevTierIndex, _result.TierIndex);
            return;
        }

        s_pending = _result;
    }

    /// <summary>결과를 꺼내고 홀더를 비운다(1회 소비). 없으면 default + false.</summary>
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
