using UnityEngine;

/// <summary>
/// "이 피해가 얼마나 센가"를 0~1로 접는 **단일 진실원**. 화면 흔들림·카드 반동이 같은 곡선을 쓴다 —
/// 각자 데미지를 해석하면 한쪽만 세지고 다른 쪽은 안 따라와 타격감이 어긋난다.
///
/// 순수 산술(RNG 미소비) + 표시 전용이라 결정론과 무관하다 — 값이 바뀌어도 게임 판정은 그대로다.
/// </summary>
public static class HitImpact
{
    /// <summary>대상 정보를 못 잡았을 때만 쓰는 최대 체력 폴백. 절대 피해로 재는 게 아니라
    /// **비율**로 재기 때문에 기준 체력이 없으면 세기를 정할 수 없다.</summary>
    const int DefaultMaxHp = 6;

    // 흔들림 배율 하한/상한. 하한을 0으로 내리지 않는 이유 — 1 피해도 맞은 건 맞은 거라
    // 화면이 아예 안 흔들리면 타격이 씹힌 것처럼 보인다.
    const float MinShakeScale = 0.55f;
    const float MaxShakeScale = 1.7f;

    /// <summary>피해 세기 0~1 = **입은 피해 / 대상의 최대 체력**. 반동 거리·회전 폭 등
    /// "비례해야 하는 값"의 공용 입력이다.
    ///
    /// 절대 피해가 아니라 비율인 이유: 체력 3짜리가 3을 맞는 것과 체력 10짜리가 3을 맞는 것은
    /// 같은 숫자여도 체감이 다르다. 기준은 현재 체력이 아니라 **최대 체력** — 현재 체력으로 재면
    /// 빈사 상태에서 스치기만 해도 매번 최대 세기로 흔들린다.
    /// 초과 피해(오버킬)는 1로 잘린다.</summary>
    public static float Strength01(int _damage, CardInstance _target)
    {
        if (_damage <= 0) return 0f;
        int t_max = _target != null && _target.maxHp > 0 ? _target.maxHp : DefaultMaxHp;
        return Mathf.Clamp01(_damage / (float)t_max);
    }

    /// <summary>이미 구한 세기(0~1)를 화면 흔들림 배율로. 여러 대상이 한 순간에 맞을 때
    /// 호출부가 **비율끼리** 비교한 뒤 최댓값을 넘기는 용도 — 피해 숫자끼리 비교하면
    /// 체력 큰 카드가 받은 큰 숫자가 항상 이겨서 비율 기준이 무의미해진다.</summary>
    public static float ShakeScale01(float _strength01) =>
        _strength01 <= 0f ? 0f : Mathf.Lerp(MinShakeScale, MaxShakeScale, Mathf.Clamp01(_strength01));

    /// <summary>화면 흔들림 배율. 0 피해면 0을 돌려줘 흔들림 자체를 생략시킨다
    /// (BattleCamera가 진폭 0이면 조용히 무동작).</summary>
    public static float ShakeScale(int _damage, CardInstance _target) =>
        ShakeScale01(Strength01(_damage, _target));
}
