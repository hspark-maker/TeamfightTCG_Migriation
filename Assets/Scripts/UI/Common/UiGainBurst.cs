using System;
using DG.Tweening;
using UnityEngine;

// "원점에서 흩뿌린 뒤 목적지로 빨려드는" 획득 연출의 궤적 코어.
// 코인이든 카드든 손맛은 같아야 하므로 궤적 규칙을 여기 한 곳에만 둔다(값만 Settings로 갈아 끼운다).
//
// 재생하지 않고 시퀀스를 만들어 돌려준다 — 호출자가 자기 연출 시퀀스에 붙여야 스킵 한 번으로
// 날아가던 것들까지 함께 최종 상태로 끌어당길 수 있다.
// 도착 통지는 InsertCallback으로 시간축에 박는다(중첩 트윈의 콜백 순서에 기대지 않는다).
public static class UiGainBurst
{
    /// <summary>궤적 규칙 한 벌. 흩어짐(각도·거리·시간) → 수렴(시간·도착 배율·회전)까지가 한 묶음이다.</summary>
    public readonly struct Settings
    {
        public readonly int   Count;
        public readonly float ScatterRadius;     // 흩어지는 거리(연출 레이어의 로컬 = 캔버스 참조px)
        public readonly float ScatterDuration;
        public readonly float GatherDuration;
        public readonly float Interval;          // 하나씩 출발이 밀리는 간격. 0이면 전부 동시에 튄다.
        public readonly float PopDuration;       // 생겨나며 커지는 시간
        public readonly float AngleStart;        // 흩어지는 부채꼴 시작 각(도)
        public readonly float AngleSpan;         // 360 이상이면 전방위 균등 분할
        public readonly float GatherScale;       // 목적지에 닿을 때의 배율(RestScale 대비. 1이면 축소 없음)
        public readonly float SpinDegrees;       // 비행 중 z 회전량(0이면 회전 없음)
        // 흩어져 날아가는 동안의 기준 배율. 프리팹을 원본 크기 그대로 쓰면서 화면에는 작게 띄울 때 쓴다.
        public readonly float RestScale;
        // 수렴 궤적이 직선에서 부풀어 오르는 폭(px). 0이면 직선 + InBack(예전 궤적) 그대로다.
        public readonly float ArcHeight;

        public Settings(int _count, float _scatterRadius, float _scatterDuration, float _gatherDuration,
                        float _interval, float _popDuration, float _angleStart, float _angleSpan,
                        float _gatherScale = 1f, float _spinDegrees = 0f, float _restScale = 1f,
                        float _arcHeight = 0f)
        {
            this.Count           = _count;
            this.ScatterRadius   = _scatterRadius;
            this.ScatterDuration = _scatterDuration;
            this.GatherDuration  = _gatherDuration;
            this.Interval        = _interval;
            this.PopDuration     = _popDuration;
            this.AngleStart      = _angleStart;
            this.AngleSpan       = _angleSpan;
            this.GatherScale     = _gatherScale;
            this.SpinDegrees     = _spinDegrees;
            this.RestScale       = _restScale;
            this.ArcHeight       = _arcHeight;
        }

        /// <summary>연출 전체 길이(초).</summary>
        public float TotalDuration
            => Mathf.Max(0, this.Count - 1) * this.Interval + this.ScatterDuration + this.GatherDuration;
    }

    /// <summary>
    /// 분출→수렴 시퀀스를 만들어 돌려준다(재생은 호출자).
    /// _spawn(i)는 i번째 날아갈 것을 만들어 준다 — 부모 붙이기·앵커·초기 배율은 이 코어가 맞춘다.
    /// _despawn은 그 하나가 목적지에 닿은 순간 불린다(숨기기 담당, 실제 파괴·반납은 호출자 몫).
    /// _onArrived(도착 수, 전체 수)는 닿을 때마다 불린다 — 수치 증가·강조를 여기에 맞물린다.
    /// </summary>
    public static Sequence Build(RectTransform _layer, Vector2 _from, Vector2 _to, in Settings _settings,
                                 Func<int, RectTransform> _spawn,
                                 Action<RectTransform> _despawn = null,
                                 Action<int, int> _onArrived = null)
    {
        var t_seq = DOTween.Sequence();

        // 날릴 것이 없으면 진행을 막지 않도록 "전부 도착"만 통지하고 빈 시퀀스를 돌려준다.
        if (_layer == null || _spawn == null || _settings.Count <= 0)
        {
            t_seq.AppendCallback(() => _onArrived?.Invoke(1, 1));
            return t_seq;
        }

        // in 파라미터는 람다가 캡처할 수 없다(CS1628) — 도착 통지에 쓸 값은 지역으로 복사해 둔다.
        int t_total = _settings.Count;

        for (int t_i = 0; t_i < t_total; t_i++)
        {
            var t_rt = _spawn(t_i);
            if (t_rt == null) continue;

            Place(t_rt, _layer, _from);

            Vector2 t_mid   = _from + ScatterOffset(t_i, in _settings);
            float   t_delay = t_i * _settings.Interval;

            t_seq.Insert(t_delay, t_rt.DOScale(_settings.RestScale, _settings.PopDuration).SetEase(Ease.OutBack));
            t_seq.Insert(t_delay, t_rt.DOAnchorPos(t_mid, _settings.ScatterDuration).SetEase(Ease.OutQuad));

            // 수렴은 두 갈래다. 직선은 InBack으로 잠깐 뒤로 물렸다 빨려들고,
            // 휘어진 궤적은 그 물림이 필요 없다 — 옆으로 부푼 곡선 자체가 "돌아 들어가는" 시간을 이미 만든다.
            t_seq.Insert(t_delay + _settings.ScatterDuration,
                         _settings.ArcHeight > 0f
                       ? ArcTo(t_rt, t_mid, _to, _settings.GatherDuration, _settings.ArcHeight, t_i)
                       : t_rt.DOAnchorPos(_to, _settings.GatherDuration).SetEase(Ease.InBack));

            if (!Mathf.Approximately(_settings.GatherScale, 1f))
                t_seq.Insert(t_delay + _settings.ScatterDuration,
                             t_rt.DOScale(_settings.RestScale * _settings.GatherScale, _settings.GatherDuration)
                                 .SetEase(Ease.InQuad));

            if (!Mathf.Approximately(_settings.SpinDegrees, 0f))
            {
                // 좌우 번갈아 돌려 한쪽으로 쏠리지 않게(난수 없이 매번 같은 그림).
                float t_spin = _settings.SpinDegrees * (t_i % 2 == 0 ? 1f : -1f);
                t_seq.Insert(t_delay, t_rt.DOLocalRotate(new Vector3(0f, 0f, t_spin),
                                                         _settings.ScatterDuration + _settings.GatherDuration)
                                           .SetEase(Ease.InOutSine));
            }

            var t_item  = t_rt;         // 클로저가 루프 변수를 붙잡지 않게 복사.
            int t_index = t_i + 1;
            t_seq.InsertCallback(t_delay + _settings.ScatterDuration + _settings.GatherDuration, () =>
            {
                _despawn?.Invoke(t_item);
                _onArrived?.Invoke(t_index, t_total);
            });
        }

        return t_seq;
    }

    /// <summary>다른 좌표계의 RectTransform 위치를 연출 레이어의 로컬(anchoredPosition 기준)로 옮긴다.</summary>
    public static Vector2 ToLayerLocal(RectTransform _layer, RectTransform _target)
    {
        if (_layer == null || _target == null) return Vector2.zero;
        return _layer.InverseTransformPoint(_target.position);
    }

    /// <summary>
    /// 2차 베지에로 휘어 든다. DOAnchorPos는 직선밖에 못 그리므로 진행도만 트윈하고 좌표는 여기서 찍는다.
    /// 코인이든 빛이든 "빨려드는" 궤적은 이 한 곳만 쓴다 — 규칙이 갈라지면 손맛도 갈라진다.
    /// _index는 휘는 방향을 가르는 축(짝수는 한쪽, 홀수는 반대쪽).
    /// </summary>
    // 대상을 트윈에 물려 둔다 — 호출자가 조각별로 거는 DOKill(transform)이 이 트윈도 함께 잡아야 잔해가 안 남는다.
    public static Tween ArcTo(RectTransform _rt, Vector2 _from, Vector2 _to, float _duration,
                              float _height, int _index)
    {
        Vector2 t_delta = _to - _from;
        float   t_dist  = t_delta.magnitude;

        // 좌우 번갈아 휜다(회전과 같은 규칙) — 한쪽으로만 부풀면 코인 전체가 한 덩어리로 돈다.
        // 이동거리에 비례해 눌러 두지 않으면 가까운 코인이 고리를 그리며 되돌아온다.
        float t_bow = Mathf.Min(_height, t_dist * 0.45f) * (_index % 2 == 0 ? 1f : -1f);

        Vector2 t_perp = t_dist > 0.001f ? new Vector2(-t_delta.y, t_delta.x) / t_dist : Vector2.up;
        Vector2 t_ctrl = _from + t_delta * 0.5f + t_perp * t_bow;

        return DOTween.To(() => 0f, _t => _rt.anchoredPosition = Bezier(_from, t_ctrl, _to, _t), 1f, _duration)
                      .SetEase(Ease.InCubic)     // 목적지에서 가속해야 "빨려든다"로 읽힌다
                      .SetTarget(_rt.transform);
    }

    static Vector2 Bezier(Vector2 _a, Vector2 _ctrl, Vector2 _b, float _t)
    {
        float t_inv = 1f - _t;
        return t_inv * t_inv * _a + 2f * t_inv * _t * _ctrl + _t * _t * _b;
    }

    // 흩어짐 방향·거리. 각도를 균등 분할해 난수 없이도 고르게 퍼지고 매번 같은 그림이 나온다.
    static Vector2 ScatterOffset(int _index, in Settings _settings)
    {
        float t_angle;
        if (_settings.AngleSpan >= 360f)
        {
            // 전방위는 Count로 나눠야 0도와 360도가 겹치지 않는다.
            t_angle = _settings.AngleStart + 360f / _settings.Count * _index;
        }
        else
        {
            float t_ratio = _settings.Count <= 1 ? 0.5f : (float)_index / (_settings.Count - 1);
            t_angle = _settings.AngleStart + _settings.AngleSpan * t_ratio;
        }

        float t_reach = _settings.ScatterRadius * (0.7f + 0.15f * (_index % 3));
        return new Vector2(Mathf.Cos(t_angle * Mathf.Deg2Rad), Mathf.Sin(t_angle * Mathf.Deg2Rad)) * t_reach;
    }

    // 연출 레이어 밑으로 붙이고 중앙 앵커·출발 위치·배율 0으로 초기화.
    static void Place(RectTransform _rt, RectTransform _layer, Vector2 _at)
    {
        _rt.SetParent(_layer, false);
        _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
        _rt.pivot     = new Vector2(0.5f, 0.5f);
        _rt.anchoredPosition = _at;
        _rt.localScale       = Vector3.zero;
        _rt.localRotation    = Quaternion.identity;
    }
}
