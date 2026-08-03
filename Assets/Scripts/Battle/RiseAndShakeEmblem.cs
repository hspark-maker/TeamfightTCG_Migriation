using System;
using DG.Tweening;
using UnityEngine;

/// <summary>솟아올라 몇 번 들썩이다 줄어들며 사라지는 엠블럼. 기본 몸짓.</summary>
[Serializable]
public class RiseAndShakeEmblem : SynergyEmblemSpec
{
    [Header("구간 비율 (합 1, 나머지가 소멸 구간)")]
    [Range(0f, 1f)] public float riseRatio  = 0.22f;   // 솟아오르며 커지는 구간
    [Range(0f, 1f)] public float shakeRatio = 0.48f;   // 들썩이는 구간

    [Header("형태")]
    [Min(0)] public int shakeCount = 2;   // 들썩임 횟수(커졌다 작아졌다 = 1회). 0이면 들썩임 없음
    public float riseScale = 1.12f;   // 솟았을 때 배율(기준 크기 대비)
    public float shakeLow  = 0.90f;   // 들썩임 아래쪽 배율
    public float exitScale = 0.72f;   // 사라질 때 줄어드는 배율

    public override void Play(CardView _view, SynergyData _synergy)
    {
        float t_total = Duration;   // 배속 적용은 베이스가 한다(raw 초 × SpeedFactor)

        GameObject t_go = Create(_view, _synergy, out SpriteRenderer t_sr, out float t_base);
        t_go.transform.localScale = Vector3.zero;

        int   t_shakes = Mathf.Max(0, this.shakeCount);
        float t_rise   = t_total * this.riseRatio;
        float t_shake  = t_total * this.shakeRatio;
        float t_exit   = Mathf.Max(0.05f, t_total - t_rise - t_shake);
        // 위·아래 한 번씩이 1회. 횟수를 0으로 두면 들썩임 없이 솟았다 사라지기만 한다.
        float t_beat   = t_shakes > 0 ? t_shake / (t_shakes * 2f) : 0f;

        Color t_on = Tint(_synergy, this.alpha);

        var t_seq = DOTween.Sequence().SetLink(t_go);

        // 1) 솟아오름 — OutBack 오버슈트로 "툭" 튀어나온다.
        t_seq.Append(t_go.transform.DOScale(t_base * this.riseScale, t_rise).SetEase(Ease.OutBack));
        t_seq.Join(t_sr.DOColor(t_on, t_rise));

        // 2) 들썩임 — 커졌다 작아졌다 반복. InOutSine이라 양 끝에서 꺾이지 않는다.
        for (int i = 0; i < t_shakes; i++)
        {
            t_seq.Append(t_go.transform.DOScale(t_base * this.shakeLow,  t_beat).SetEase(Ease.InOutSine));
            t_seq.Append(t_go.transform.DOScale(t_base * this.riseScale, t_beat).SetEase(Ease.InOutSine));
        }

        // 3) 소멸 — 줄어들며 투명해진다.
        t_seq.Append(t_go.transform.DOScale(t_base * this.exitScale, t_exit).SetEase(Ease.InQuad));
        t_seq.Join(t_sr.DOFade(0f, t_exit));

        t_seq.OnComplete(() => { if (t_go != null) UnityEngine.Object.Destroy(t_go); });
        // 중간에 씬이 내려가면 SetLink가 트윈을 죽이는데, 그때 오브젝트도 같이 사라지므로 별도 정리 불필요.
    }
}
