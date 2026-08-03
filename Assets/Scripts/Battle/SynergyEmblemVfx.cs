using UnityEngine;

/// <summary>
/// 시너지 상징(엠블럼) 재생의 **진입점**. 발동 지점은 두 곳 —
/// 그 시너지 카드가 배치될 때([Placed]), 그리고 효과가 실제로 터질 때([Triggered], SynergyTriggers).
///
/// 여기엔 "언제 · 누구에게"만 있다. **"어떻게 움직이나"는 SynergyEmblemSpec 자식들이 소유한다**
/// (RiseAndShakeEmblem / DropAndShineEmblem / PopEmblem). 몸짓이 늘어도 이 파일은 그대로다.
/// 배선은 SynergyData.vfx.emblems — 그 타이밍을 맡은 줄이 없으면 조용히 건너뛴다.
///
/// 재생 길이도 몸짓(spec)이 쥔다 — 저장은 raw 초, 배속은 spec.Duration에서 곱한다.
/// </summary>
public static class SynergyEmblemVfx
{
    /// <summary>그 타이밍을 맡은 줄. 배선이 없으면 null.</summary>
    static SynergyEmblemEntry EntryOf(SynergyData _synergy, SynergyEmblemTiming _timing)
        => _synergy != null && _synergy.vfx != null ? _synergy.vfx.EntryFor(_timing) : null;

    /// <summary>이 타이밍 엠블럼 1회 재생 길이(초, 배속 적용). 연출이 끝나길 기다렸다가
    /// 다음 연출을 잇는 호출부(무리 선피해 → 볼리)가 쓴다. 배선이 없으면 0 — 기다릴 것도 없다.</summary>
    public static float DurationOf(SynergyData _synergy, SynergyEmblemTiming _timing)
    {
        SynergyEmblemEntry t_entry = EntryOf(_synergy, _timing);
        return t_entry != null ? t_entry.spec.Duration : 0f;
    }

    /// <summary>[Placed] 배치 상징: 이 카드가 속한 활성 시너지 중 그 타이밍을 맡은 줄만 재생
    /// (여럿이면 겹쳐 뜬다 — 서로 다른 시너지의 상징이라 겹쳐도 읽힌다).
    ///
    /// **발화점이 규칙(SynergyTriggers.Placed)이 아니라 뷰인 이유**: 시너지 Placed는 ApplyDeckSynergy에서
    /// 도는데 그건 InitializeViews보다 앞이라 그 시점엔 CardView가 아직 없다 — 거기서 띄우면 아무도 못 본다.
    /// "놓이는 순간"은 규칙상의 사건이 아니라 화면상의 사건이므로 배치 연출이 끝나는 지점이 유일한 발화점이다.
    ///
    /// 뒷면 카드는 건너뛴다 — 배지와 같은 규약(뒷면 적의 종족/직업 노출 방지).</summary>
    public static void PlayPlaced(CardView _view, CardInstance _card, SynergyState _state)
    {
        if (_view == null || _card == null || _state == null || !_card.isRevealed) return;

        foreach (ActiveSynergy t_active in _state.Active)
        {
            SynergyData t_synergy = t_active?.Synergy;
            if (t_synergy == null) continue;
            if (!SynergyApplier.BelongsTo(_card, t_synergy)) continue;
            Play(_view, t_synergy, SynergyEmblemTiming.Placed);
        }
    }

    /// <summary>[Triggered] 효과가 실제로 일한 순간. 범위(자기 1장 / 소속 아군 전원)는 그 줄이 정한다 —
    /// 무리 선피해처럼 전원이 함께 일하는 효과는 발동 주체 한 장만 빛나면 그림과 어긋난다.
    /// 반환값 = 실제로 띄웠는가(연출이 끝나길 기다릴지 호출부가 판단하는 근거).
    /// 순수 연출이라 결정론과 무관하다(상태·RNG 무접촉).</summary>
    public static bool PlayTriggered(CardInstance _self, SynergyData _synergy, BattleField _field)
    {
        SynergyEmblemEntry t_entry = EntryOf(_synergy, SynergyEmblemTiming.Triggered);
        if (t_entry == null) return false;

        // 필드를 못 받으면 전원 범위를 풀 수 없으므로 발동 주체 1장으로 떨어진다.
        if (t_entry.scope == SynergyEmblemScope.AllMembers && _field != null)
        {
            foreach (CardInstance t_card in _field.GetActiveCards())
            {
                if (t_card == null || !t_card.IsAlive) continue;
                if (!SynergyApplier.BelongsTo(t_card, _synergy)) continue;
                t_entry.spec.Play(CardView.GetView(t_card), _synergy);
            }
            return true;
        }

        CardView t_view = CardView.GetView(_self);
        if (t_view == null) return false;
        t_entry.spec.Play(t_view, _synergy);
        return true;
    }

    /// <summary>이 카드 앞에 그 타이밍의 엠블럼을 1회 재생. 뷰/배선이 없으면 무동작.
    /// 몸짓 선택은 타입 자체가 한다 — 여기에 스타일 분기를 두지 마라
    /// (몸짓을 추가할 때 이 파일을 고쳐야 하는 순간 상속으로 나눈 의미가 사라진다).</summary>
    public static void Play(CardView _view, SynergyData _synergy, SynergyEmblemTiming _timing)
    {
        SynergyEmblemEntry t_entry = EntryOf(_synergy, _timing);
        if (_view == null || t_entry == null) return;
        t_entry.spec.Play(_view, _synergy);
    }
}
