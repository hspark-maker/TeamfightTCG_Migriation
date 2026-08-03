using System;
using DG.Tweening;
using UnityEngine;

/// <summary>제자리에서 한 번 "툭" 튀어나왔다 곧바로 꺼지는 짧은 엠블럼.
///
/// 자주 발동하는 타이밍(피격마다 도는 비늘 감소 등)용이다 — 솟았다 들썩이는 몸짓은 그 빈도로 돌면
/// 화면이 계속 시끄럽다. 여기선 커지는 순간이 곧 신호고, 나머지는 빠지는 시간이다.</summary>
[Serializable]
public class PopEmblem : SynergyEmblemSpec
{
    [Header("구간 비율 (합 1, 나머지가 유지 구간)")]
    [Range(0f, 1f)] public float popRatio  = 0.30f;   // 튀어나오는 구간(OutBack 오버슈트)
    [Range(0f, 1f)] public float exitRatio = 0.45f;   // 줄어들며 사라지는 구간

    [Header("형태")]
    public float popScale  = 1.25f;   // 튀어나왔을 때 배율(기준 크기 대비)
    public float exitScale = 0.85f;   // 사라질 때 줄어드는 배율
    // 시작 배율. 1이면 **원래 크기에서 커진다** — 상징이 이미 그 자리에 있던 것처럼 읽힌다.
    // 1보다 작게 두면 작은 점에서 부풀어 오르는 그림이 된다(등장 성격이 강해진다).
    [Range(0f, 2f)] public float startScale = 1f;

    public override void Play(CardView _view, SynergyData _synergy)
    {
        float t_total = Duration;   // 배속 적용은 베이스가 한다(raw 초 × SpeedFactor)

        GameObject t_go = Create(_view, _synergy, out SpriteRenderer t_sr, out float t_base);
        t_go.transform.localScale = Vector3.one * (t_base * Mathf.Max(0f, this.startScale));

        float t_pop  = t_total * this.popRatio;
        float t_exit = t_total * this.exitRatio;
        float t_hold = Mathf.Max(0f, t_total - t_pop - t_exit);   // 튀어나온 채로 눈에 남는 짬

        Color t_on = Tint(_synergy, this.alpha);

        var t_seq = DOTween.Sequence().SetLink(t_go);

        // 1) pop — 오버슈트로 튀어나오며 나타난다. 나타남이 튀어나옴보다 빨라야 "이미 있던 게 커진" 느낌이 안 난다.
        t_seq.Append(t_go.transform.DOScale(t_base * this.popScale, t_pop).SetEase(Ease.OutBack));
        t_seq.Join(t_sr.DOColor(t_on, t_pop * 0.5f));
        t_seq.AppendInterval(t_hold);

        // 2) 소멸 — 줄어들며 투명해진다.
        t_seq.Append(t_go.transform.DOScale(t_base * this.exitScale, t_exit).SetEase(Ease.InQuad));
        t_seq.Join(t_sr.DOFade(0f, t_exit));

        t_seq.OnComplete(() => { if (t_go != null) UnityEngine.Object.Destroy(t_go); });
    }
}
