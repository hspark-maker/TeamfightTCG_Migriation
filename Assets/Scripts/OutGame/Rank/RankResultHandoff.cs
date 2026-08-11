// 전투 씬에서 정산한 랭크 결과를 로비 씬까지 실어 나르는 씬 캐리어(자체 세이브 없음)
public static class RankResultHandoff
{
    // 병합된 값의 의미는 "시작 티어 → 도달 티어, 그 사이 총 증감"이다(1회 전투 스냅샷이 아니다)
    static RankApplyResult? s_pending;

    // 정산 결과 싣기(로비 도달 전 연속 전투는 증감을 누적하고 처음 출발점 → 마지막 도달점으로 병합)
    public static void Set(in RankApplyResult _result)
    {
        // 클램프에 걸려 아무것도 안 바뀐 판은 싣지 않는다 — 로비에서 보여줄 것이 없다.
        if (_result.Delta == 0 && !_result.IsTierUp) return;

        if (s_pending.HasValue)
        {
            // 티어가 그대로인 판(포인트만 변한 판)도 버리지 않는다 — 증감은 늘 누적된다.
            // 출발은 처음 실린 것, 도착은 마지막 것 — 최소/최대로 접으면 승패가 섞였을 때 거짓말이 된다
            // (실버1 →승 실버2 →패 실버1이 "실버1 → 실버2 승급"으로 보고된다).
            var t_prev = s_pending.Value;

            // 예외는 첫 티어 진입의 센티널 -1 하나뿐 — 언제 실리든 이긴다. 삼켜지면 진입 연출이 사라진다.
            int t_from = _result.PrevTierIndex < 0 ? _result.PrevTierIndex : t_prev.PrevTierIndex;

            s_pending = new RankApplyResult(t_prev.Delta + _result.Delta, t_from, _result.TierIndex);
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
