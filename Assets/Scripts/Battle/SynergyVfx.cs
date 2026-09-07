using UnityEngine;

/// <summary>
/// 시너지 발동 연출 중 **전용 배관이 필요 없는 1회성 스폰**만 모아 둔다.
/// (투사체 다발처럼 순서·수명 관리가 필요한 건 각자 파일 — HealVfx / BrandVolleyVfx.)
///
/// 프리팹·수명·정렬 스펙은 그 시너지의 연출 에셋(SynergyVfxConfig 자식)이 소유하고,
/// 스폰/반납 규약은 BattleVfx가 소유한다. 여기엔 "어디에 띄우나"만 있다.
/// 순수 연출 — 게임상태/RNG 무접촉이고, 미배선이면 조용히 아무것도 하지 않는다.
/// </summary>
public static class SynergyVfx
{
    /// <summary>흐름 발동: <b>발동한 그 카드 자리</b>에서 바람이 인다.
    ///
    /// 필드 중앙이 아닌 이유 — 흐름은 "그 카드가 등장했다 / 그 카드가 때린다"가 발동 사유라,
    /// 중앙에 띄우면 어느 카드 때문에 떴는지 화면에서 읽히지 않는다(스택만 커 보인다).
    ///
    /// 스택은 필드가 세는 값을 그대로 쓴다(연출이 따로 세지 않는다) — 쌓일수록 바람이 커진다.</summary>
    public static void PlayFlowWind(CardInstance _card, BattleFieldState _field, FlowSynergyVfxConfig _vfx)
    {
        if (_card == null || _field == null) return;
        PlayFlowWind(CardView.GetView(_card), _vfx, _field.FlowStack);
    }

    /// <summary>스폰된 연출 인스턴스의 크기 배율. 풀에서 빌려온 오브젝트라 **스폰할 때마다 반드시 세팅**한다 —
    /// 안 그러면 직전 스택의 크기가 그대로 남아 다음 재생에 딸려온다.</summary>
    static void ApplyScale(VfxHandle _handle, float _scale)
    {
        if (!_handle.Valid) return;
        _handle.Go.transform.localScale = Vector3.one * _scale;
    }

    /// <summary>뷰를 직접 아는 호출부(연출 테스터)용. 게임 경로와 **같은 자리·같은 규칙**이다.
    ///
    /// transform.position이 아니라 SlotPosition을 쓰는 이유: 카드가 공격 연출로 나가 있어도
    /// 바람이 카드를 따라 딸려가지 않고 그 슬롯에 남는다.</summary>
    public static void PlayFlowWind(CardView _view, FlowSynergyVfxConfig _vfx, int _stack = 1)
    {
        if (_view == null || _vfx == null || _vfx.wind.prefab == null) return;   // 미배선 = 연출 생략

        ApplyScale(BattleVfx.Play(_vfx.wind, _view.SlotPosition, _view.VfxSortingLayerId),
                   _vfx.WindScaleFor(_stack));
    }
}
