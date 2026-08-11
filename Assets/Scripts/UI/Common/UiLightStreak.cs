using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// "아이콘이 빛이 되어 한 줄기로 흘러드는" 획득 연출의 코어. 조립만 맡고 궤적은 UiGainBurst.ArcTo를 쓴다
// (코인과 손맛이 갈라지지 않게).
//
// 코인 분출(UiGainBurst)과 갈라 둔 이유는 장수다 — 보상 수령은 이동체가 재화당 하나여야 한다.
// 여러 장이 사방으로 흩어지면 눈이 따라갈 대상을 못 정한다(그래서 이 연출이 생겼다).
//
// 재생하지 않고 시퀀스를 만들어 돌려준다 — 호출자 시퀀스에 붙어야 스킵 한 번에 함께 끌려간다.
public static class UiLightStreak
{
    // 사그라드는 구간이 비행에서 차지하는 비율. 닿는 프레임에 이미 없어야 "수치 속으로 스몄다"로 읽힌다 —
    // 닿고 나서 지우면 빛이 HUD 위에 한 번 얹혔다 사라진다.
    const float VANISH_RATIO = 0.35f;

    // 꼬리 끝의 크기·밝기(머리 대비). 0으로 내리면 마지막 조각이 점이 되어 꼬리가 끊겨 보인다.
    const float TAIL_MIN_SCALE = 0.35f;
    const float TAIL_MIN_ALPHA = 0.25f;

    /// <summary>빛 한 줄기의 생김새. 피어남 → 비행 → 사그라듦이 한 묶음이다.</summary>
    public readonly struct Settings
    {
        public readonly float BloomDuration;   // 출발 자리에서 피어나는 시간
        public readonly float TravelDuration;  // 목적지까지 흐르는 시간
        public readonly float HeadSize;        // 머리 지름(px). 아이콘을 덮을 만해야 "변했다"로 읽힌다.
        public readonly int   TailCount;       // 뒤따르는 조각 수(0이면 머리만)
        public readonly float TailInterval;    // 조각 하나씩 늦는 간격
        public readonly float BowHeight;       // 직선에서 부푸는 폭
        public readonly Color Tint;

        public Settings(float _bloomDuration, float _travelDuration, float _headSize,
                        int _tailCount, float _tailInterval, float _bowHeight, Color _tint)
        {
            this.BloomDuration  = _bloomDuration;
            this.TravelDuration = _travelDuration;
            this.HeadSize       = _headSize;
            this.TailCount      = _tailCount;
            this.TailInterval   = _tailInterval;
            this.BowHeight      = _bowHeight;
            this.Tint           = _tint;
        }
    }

    /// <summary>
    /// 빛 줄기 시퀀스를 만들어 돌려준다(재생은 호출자).
    /// _spawn(i)는 i번째 조각(0 = 머리)을 만들어 준다 — 부모·앵커·크기·초기 알파는 이 코어가 맞춘다.
    /// _lane은 휘는 방향을 가르는 축이다. 재화마다 다른 값을 줘야 두 줄기가 겹쳐 한 덩어리로 보이지 않는다.
    /// _onArrived는 <b>머리</b>가 닿을 때 한 번 불린다 — 수치 상승을 여기에 맞물린다.
    /// </summary>
    public static Sequence Build(RectTransform _layer, Vector2 _from, Vector2 _to, in Settings _settings,
                                 int _lane, Func<int, Graphic> _spawn,
                                 Action<Graphic> _despawn = null, Action _onArrived = null)
    {
        var t_seq = DOTween.Sequence();

        // 세울 것이 없어도 도착만은 통지한다 — 수치 상승이 연출 배선에 인질로 잡히지 않게.
        if (_layer == null || _spawn == null)
        {
            t_seq.AppendCallback(() => _onArrived?.Invoke());
            return t_seq;
        }

        int t_pieces = Mathf.Max(1, _settings.TailCount + 1);
        float t_vanish = Mathf.Max(0.01f, _settings.TravelDuration * VANISH_RATIO);

        for (int t_i = 0; t_i < t_pieces; t_i++)
        {
            var t_light = _spawn(t_i);
            if (t_light == null) continue;

            var t_rt = (RectTransform)t_light.transform;

            // 뒤로 갈수록 작고 흐리게 — 이 체감이 없으면 조각 여러 개가 줄지어 가는 것으로 보인다.
            float t_ratio = t_pieces <= 1 ? 0f : (float)t_i / (t_pieces - 1);
            float t_scale = Mathf.Lerp(1f, TAIL_MIN_SCALE, t_ratio);
            float t_alpha = _settings.Tint.a * Mathf.Lerp(1f, TAIL_MIN_ALPHA, t_ratio);

            Place(t_light, _layer, _from, _settings.HeadSize, _settings.Tint);

            float t_born = t_i * _settings.TailInterval;
            float t_fly  = t_born + _settings.BloomDuration;
            float t_hit  = t_fly + _settings.TravelDuration;

            // 피어난다. 조각이 출발 자리에 겹쳐 쌓이는 이 한 순간이 "아이콘이 빛덩이가 됐다"이다.
            t_seq.Insert(t_born, t_rt.DOScale(t_scale, _settings.BloomDuration).SetEase(Ease.OutBack));
            t_seq.Insert(t_born, t_light.DOFade(t_alpha, _settings.BloomDuration).SetEase(Ease.OutQuad));

            // 흘러간다. 끝에서 가속하는 궤적이라 뭉쳐 있던 조각이 늘어나며 꼬리가 된다.
            t_seq.Insert(t_fly, UiGainBurst.ArcTo(t_rt, _from, _to, _settings.TravelDuration,
                                                  _settings.BowHeight, _lane));

            t_seq.Insert(t_hit - t_vanish, t_rt.DOScale(0f, t_vanish).SetEase(Ease.InQuad));
            t_seq.Insert(t_hit - t_vanish, t_light.DOFade(0f, t_vanish).SetEase(Ease.InQuad));

            var t_item = t_light;        // 클로저가 루프 변수를 붙잡지 않게 복사.
            bool t_head = t_i == 0;
            t_seq.InsertCallback(t_hit, () =>
            {
                _despawn?.Invoke(t_item);
                if (t_head) _onArrived?.Invoke();
            });
        }

        return t_seq;
    }

    // 연출 레이어 밑으로 붙이고 중앙 앵커·출발 위치·배율 0으로 초기화. 알파도 0으로 눕힌다 —
    // 피어나기 전 한 프레임이라도 제 크기로 보이면 "빛이 켜졌다"가 아니라 "튀어나왔다"가 된다.
    static void Place(Graphic _light, RectTransform _layer, Vector2 _at, float _size, Color _tint)
    {
        var t_rt = (RectTransform)_light.transform;

        t_rt.SetParent(_layer, false);
        t_rt.anchorMin = t_rt.anchorMax = new Vector2(0.5f, 0.5f);
        t_rt.pivot     = new Vector2(0.5f, 0.5f);
        t_rt.sizeDelta = Vector2.one * _size;

        t_rt.anchoredPosition = _at;
        t_rt.localScale       = Vector3.zero;
        t_rt.localRotation    = Quaternion.identity;

        var t_c = _tint;
        t_c.a = 0f;
        _light.color = t_c;
    }
}
