using UnityEngine;

/// <summary>
/// 시너지 발동 연출 중 **전용 배관이 필요 없는 1회성 스폰**만 모아 둔다.
/// (투사체 다발처럼 순서·수명 관리가 필요한 건 각자 파일 — HealVfx / SwarmVfx.)
///
/// 프리팹·수명·정렬은 전부 BattleVfxLibrary + BattleVfx 소유. 여기엔 "어디에 띄우나"만 있다.
/// 순수 연출 — 게임상태/RNG 무접촉이고, 미배선이면 조용히 아무것도 하지 않는다.
/// </summary>
public static class SynergyVfx
{
    /// <summary>흐름 발동: 그 진영 필드 위로 바람이 지나간다.
    ///
    /// 스폰 위치는 <b>슬롯 좌표의 평균</b>이다. 효과 SO는 BattleField만 알고 BattleFieldView를 찾아올
    /// 배관이 없어서(그 배관을 새로 뚫으면 시너지가 뷰 계층을 알게 된다) 카드 뷰의 자리로 중앙을 대신 잡는다.
    /// transform.position이 아니라 SlotPosition을 쓰는 이유: 카드가 공격 연출로 나가 있어도 바람이 딸려가지 않게.</summary>
    public static void PlayFlowWind(BattleField _field)
    {
        if (_field == null) return;
        if (!BattleVfx.TryGetEntry(BattleVfxId.FlowWind, out _)) return;   // 미배선 = 연출 생략

        Vector3 t_sum   = Vector3.zero;
        int     t_count = 0;
        int     t_layer = 0;

        foreach (CardInstance t_card in _field.GetActiveCards())
        {
            CardView t_view = CardView.GetView(t_card);
            if (t_view == null) continue;
            t_sum  += t_view.SlotPosition;
            t_layer = t_view.VfxSortingLayerId;
            t_count++;
        }

        if (t_count == 0) return;   // 띄울 자리가 없다(빈 필드)
        BattleVfx.Play(BattleVfxId.FlowWind, t_sum / t_count, t_layer);
    }

    /// <summary>뷰를 직접 아는 호출부(연출 테스터)용. 중앙 판정은 BattleFieldView.FieldCenter —
    /// 슬롯 격자 기준이라 카드가 죽어 비어 있어도 자리가 흔들리지 않는다(게임 경로보다 정확하다).
    /// 게임 경로가 이걸 못 쓰는 이유는 시너지 효과 SO가 뷰 계층을 모르기 때문이다.</summary>
    public static void PlayFlowWind(BattleFieldView _view)
    {
        if (_view == null) return;
        if (!BattleVfx.TryGetEntry(BattleVfxId.FlowWind, out _)) return;

        CardView t_any = _view.GetSlotView(BattleField.SLOT_COUNT / 2);
        BattleVfx.Play(BattleVfxId.FlowWind, _view.FieldCenter, t_any != null ? t_any.VfxSortingLayerId : 0);
    }
}
