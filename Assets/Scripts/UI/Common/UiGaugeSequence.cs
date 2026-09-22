using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>데이터 종류와 무관하게 충전·경계 유지·다음 구간을 순서대로 재생한다.</summary>
public static class UiGaugeSequence
{
    public readonly struct Step
    {
        public readonly double From;
        public readonly double To;
        public readonly float Fraction;
        public readonly bool ReachesBoundary;

        public Step(double from, double to, float fraction, bool reachesBoundary)
        {
            this.From = from;
            this.To = to;
            this.Fraction = Mathf.Clamp01(fraction);
            this.ReachesBoundary = reachesBoundary;
        }
    }

    public readonly struct Timing
    {
        public readonly float FillDuration;
        public readonly float MinFillDuration;
        public readonly float MaxDuration;
        public readonly float BoundaryHold;
        public readonly float ArrivalDuration;
        public readonly Ease FillEase;

        public Timing(float fillDuration, float minFillDuration, float maxDuration, float boundaryHold, float arrivalDuration, Ease fillEase = Ease.OutCubic)
        {
            this.FillDuration = Mathf.Max(0.01f, fillDuration);
            this.MinFillDuration = Mathf.Max(0.01f, minFillDuration);
            this.MaxDuration = Mathf.Max(0.01f, maxDuration);
            this.BoundaryHold = Mathf.Max(0f, boundaryHold);
            this.ArrivalDuration = Mathf.Max(0f, arrivalDuration);
            this.FillEase = fillEase;
        }
    }

    /// <summary>충전과 도달 팝을 소유하는 정지 시퀀스. createArrival에는 보정된 팝 시간을 전달한다.</summary>
    public static Sequence Create(GameObject owner, IReadOnlyList<Step> steps, Timing timing,
        Action<double> present, Action<Step> reached = null, Func<Step, float, Tween> createArrival = null,
        Action released = null, TweenCallback completed = null)
    {
        if (steps == null || steps.Count == 0) return null;
        float total = timing.ArrivalDuration;
        foreach (Step step in steps)
            total += Mathf.Max(timing.MinFillDuration, timing.FillDuration * step.Fraction) + timing.BoundaryHold;
        float speed = Mathf.Min(1f, timing.MaxDuration / total);
        Sequence sequence = DOTween.Sequence().SetUpdate(true).Pause();
        if (owner != null) sequence.SetLink(owner);
        float cursor = 0f;
        foreach (Step step in steps)
        {
            sequence.InsertCallback(cursor, () => present?.Invoke(step.From));
            float duration = Mathf.Max(timing.MinFillDuration, timing.FillDuration * step.Fraction) * speed;
            sequence.Insert(cursor, DOVirtual.Float(0f, 1f, duration,
                fraction => present?.Invoke(step.From + (step.To - step.From) * fraction)).SetEase(timing.FillEase));
            cursor += duration;
            sequence.InsertCallback(cursor, () =>
            {
                present?.Invoke(step.To);
                if (step.ReachesBoundary) reached?.Invoke(step);
            });
            if (step.ReachesBoundary)
            {
                Tween arrival = createArrival?.Invoke(step, timing.ArrivalDuration * speed);
                if (arrival != null) sequence.Insert(cursor, arrival);
            }
            cursor += timing.BoundaryHold * speed;
            sequence.InsertCallback(cursor, () => released?.Invoke());
        }
        sequence.AppendInterval(Mathf.Max(0f, cursor + timing.ArrivalDuration * speed - sequence.Duration()));
        if (completed != null) sequence.OnComplete(completed);
        return sequence;
    }
}
