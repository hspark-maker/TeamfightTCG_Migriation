using System;

// 전투 씬에서 정산한 랭크 결과를 로비 씬까지 실어 나르는 씬 캐리어(자체 세이브 없음)
public static class RankResultHandoff
{
    // 병합된 값의 의미는 "시작 티어 → 도달 티어, 그 사이 총 증감"이다(1회 전투 스냅샷이 아니다)
    static RankApplyResult? s_pending;

    // 정산 결과 싣기(로비 도달 전 연속 전투는 증감을 누적하고 도달 최고 티어로 병합)
    public static void Set(in RankApplyResult _result)
    {
        // 하한 클램프에 걸려 아무것도 안 바뀐 판은 싣지 않는다 — 로비에서 보여줄 것이 없다.
        if (_result.Delta == 0 && !_result.IsTierUp) return;

        if (s_pending.HasValue)
        {
            // 티어가 그대로인 판(포인트만 변한 판)도 버리지 않는다 — 증감은 늘 누적된다.
            // 출발 티어는 더 낮은 쪽을 남긴다 — 첫 티어 진입의 센티널 -1이 나중에 실려도 삼켜지지 않게.
            var t_prev = s_pending.Value;
            s_pending = new RankApplyResult(t_prev.Delta + _result.Delta,
                                            Math.Min(t_prev.PrevTierIndex, _result.PrevTierIndex),
                                            Math.Max(t_prev.TierIndex, _result.TierIndex));
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
