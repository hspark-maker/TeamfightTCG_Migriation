using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// "터진 자리에서 사방으로 튀어 떨어지는" 파편의 궤적 코어. 축하 순간의 손맛을 한 곳에만 둔다.
//
// UiGainBurst와 갈라지는 지점은 하나다 — 저건 흩어졌다 목적지로 **수렴**하고, 이건 흩어졌다 **떨어진다**.
// 그 외의 규칙(재생하지 않고 시퀀스를 돌려준다, 생성·반납은 호출자가 맡는다, 난수 없이 매번 같은 그림)은 같다.
public static class UiConfettiBurst
{
    /// <summary>파편 한 벌의 궤적 규칙. 솟음(거리·시간) → 낙하(거리·시간)까지가 한 묶음이다.</summary>
    public readonly struct Settings
    {
        public readonly int   Count;
        public readonly float Radius;        // 솟으며 벌어지는 거리(연출 레이어의 로컬 = 캔버스 참조px)
        public readonly float RiseDuration;
        public readonly float FallDistance;  // 솟은 자리에서 더 떨어지는 거리
        public readonly float FallDuration;
        public readonly float PopDuration;   // 생겨나며 커지는 시간
        public readonly float SpreadDelay;   // 조각별 지연의 최대치. 0이면 전부 동시에 튄다 = 폭발이지 팝콘이 아니다.
        public readonly float SpinDegrees;   // 비행 중 z 회전량

        public Settings(int _count, float _radius, float _riseDuration, float _fallDistance, float _fallDuration,
                        float _popDuration, float _spreadDelay, float _spinDegrees)
        {
            this.Count        = _count;
            this.Radius       = _radius;
            this.RiseDuration = _riseDuration;
            this.FallDistance = _fallDistance;
            this.FallDuration = _fallDuration;
            this.PopDuration  = _popDuration;
            this.SpreadDelay  = _spreadDelay;
            this.SpinDegrees  = _spinDegrees;
        }

        /// <summary>연출 전체 길이(초).</summary>
        public float TotalDuration => this.SpreadDelay + this.RiseDuration + this.FallDuration;
    }

    /// <summary>
    /// 분출→낙하 시퀀스를 만들어 돌려준다(재생은 호출자).
    /// _spawn(i)는 i번째 조각을 만들어 준다 — 부모 붙이기·앵커·초기 배율은 이 코어가 맞춘다.
    /// _despawn은 그 조각이 다 떨어진 순간 불린다(숨기기 담당, 실제 파괴·반납은 호출자 몫).
    /// </summary>
    public static Sequence Build(RectTransform _layer, Vector2 _from, in Settings _settings,
                                 Func<int, RectTransform> _spawn,
                                 Action<RectTransform> _despawn = null)
    {
        var t_seq = DOTween.Sequence();

        if (_layer == null || _spawn == null || _settings.Count <= 0) return t_seq;

        for (int t_i = 0; t_i < _settings.Count; t_i++)
        {
            var t_rt = _spawn(t_i);
            if (t_rt == null) continue;

            Place(t_rt, _layer, _from);

            // 조각마다 출발이 어긋나야 "팝콘"이 된다. 균등 간격으로 밀면 분수(噴水)로 읽힌다.
            float t_delay = _settings.SpreadDelay * Jitter(t_i, 17);

            Vector2 t_peak = _from + RiseOffset(t_i, in _settings);

            // 떨어지는 동안 옆으로도 조금 흐른다 — 수직 낙하만 하면 조각이 아니라 눈(雪)이 된다.
            Vector2 t_rest = t_peak + new Vector2((Jitter(t_i, 51) - 0.5f) * _settings.Radius * 0.35f,
                                                  -_settings.FallDistance * (0.7f + 0.6f * Jitter(t_i, 83)));

            t_seq.Insert(t_delay, t_rt.DOScale(1f, _settings.PopDuration).SetEase(Ease.OutBack));
            t_seq.Insert(t_delay, t_rt.DOAnchorPos(t_peak, _settings.RiseDuration).SetEase(Ease.OutQuad));
            // 낙하는 InQuad — 가속이 붙어야 중력으로 읽힌다(솟음의 OutQuad와 대칭).
            t_seq.Insert(t_delay + _settings.RiseDuration,
                         t_rt.DOAnchorPos(t_rest, _settings.FallDuration).SetEase(Ease.InQuad));

            if (!Mathf.Approximately(_settings.SpinDegrees, 0f))
            {
                // 좌우 번갈아 돌려 한쪽으로 쏠리지 않게(UiGainBurst와 같은 규칙).
                float t_spin = _settings.SpinDegrees * (t_i % 2 == 0 ? 1f : -1f) * (0.5f + Jitter(t_i, 29));
                t_seq.Insert(t_delay, t_rt.DOLocalRotate(new Vector3(0f, 0f, t_spin),
                                                         _settings.RiseDuration + _settings.FallDuration,
                                                         RotateMode.FastBeyond360).SetEase(Ease.Linear));
            }

            // 낙하 구간에서 지워진다. 바닥에서 툭 사라지면 잔해가 눈에 걸린다.
            var t_graphic = t_rt.GetComponent<Graphic>();
            if (t_graphic != null)
                t_seq.Insert(t_delay + _settings.RiseDuration,
                             t_graphic.DOFade(0f, _settings.FallDuration).SetEase(Ease.InQuad));

            var t_item = t_rt;   // 클로저가 루프 변수를 붙잡지 않게 복사.
            t_seq.InsertCallback(t_delay + _settings.RiseDuration + _settings.FallDuration,
                                 () => _despawn?.Invoke(t_item));
        }

        return t_seq;
    }

    // 솟는 방향·거리. 균등 분할에 지터를 얹는다 — 완전 균등이면 바퀴살이 되고, 완전 난수면 뭉친다.
    static Vector2 RiseOffset(int _index, in Settings _settings)
    {
        float t_angle = 360f / _settings.Count * _index + (Jitter(_index, 7) - 0.5f) * (360f / _settings.Count);
        float t_reach = _settings.Radius * (0.55f + 0.45f * Jitter(_index, 37));

        return new Vector2(Mathf.Cos(t_angle * Mathf.Deg2Rad), Mathf.Sin(t_angle * Mathf.Deg2Rad)) * t_reach;
    }

    // 결정론적 의사난수 [0,1). Random을 쓰지 않는 이유는 UiGainBurst가 각도를 균등 분할하는 이유와 같다 —
    // 매번 같은 그림이 나와야 값을 보고 튜닝할 수 있다.
    static float Jitter(int _index, int _salt)
    {
        int t_hash = (_index + 1) * 73856093 ^ _salt * 19349663;
        return ((t_hash & 0x7fffffff) % 1000) * 0.001f;
    }

    // 연출 레이어 밑으로 붙이고 중앙 앵커·출발 위치·배율 0으로 초기화(UiGainBurst.Place와 같은 규칙).
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
