using DG.Tweening;
using UnityEngine;

// 획득이 도착한 순간 대상을 한 번 튀겨 주는 공통 강조(골드 수치, 탭 아이콘 …).
// 여러 곳에서 같은 손맛이 나야 하므로 규칙을 한 곳에 둔다.
public static class UiPunch
{
    public const float DEFAULT_SCALE    = 0.35f;
    public const float DEFAULT_DURATION = 0.3f;

    /// <summary>
    /// 대상을 기준 배율에서 부풀렸다 되돌린다.
    /// 도착이 연달아 와도 배율이 누적되지 않는다 — 진행 중 펀치를 먼저 완료(=기준 배율 복귀)시킨 뒤 다시 시작한다.
    /// </summary>
    public static Tween Play(Transform _target, float _punch = DEFAULT_SCALE, float _duration = DEFAULT_DURATION)
    {
        if (_target == null) return null;

        _target.DOComplete();
        return _target.DOPunchScale(Vector3.one * _punch, _duration, vibrato: 2, elasticity: 0.8f)
                      .SetLink(_target.gameObject);
    }
}
