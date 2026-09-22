using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public partial class PassPanel
{
    [Header("레벨 게이지 연출")]
    [Tooltip("한 구간 전체 충전 시간. 작은 증가분도 최소 충전 시간만큼 표시한다.")]
    [SerializeField, Min(0.01f)] float progressFillDuration = 0.45f;
    [SerializeField, Min(0.01f)] float progressMinFillDuration = 0.15f;
    [Tooltip("여러 레벨을 올려도 마지막 노드 강조까지 이 시간 안에 끝낸다.")]
    [SerializeField, Min(0.1f)] float progressMaxDuration = 2.5f;
    [SerializeField, Min(0.01f)] float progressArrivalDuration = 0.25f;
    [Tooltip("다음 구간으로 넘어가기 전 가득 찬 게이지를 유지하는 시간.")]
    [UnityEngine.Serialization.FormerlySerializedAs("progressFlashDuration")]
    [SerializeField, Min(0.01f)] float progressBoundaryHoldDuration = 0.12f;
    [SerializeField, Range(0f, 0.5f)] float progressNodePunch = 0.12f;
    [SerializeField] ScrollRect progressScroll;

    double _displayExp;
    long _targetExp;
    string _progressSeason;
    string _progressUser;
    bool _hasProgress;
    bool _followProgress;
    Sequence _progressSequence;

    bool HasProgressToPlay => this._hasProgress && this._displayExp < this._targetExp;

    readonly struct ProgressRange
    {
        internal readonly double Floor;
        internal readonly double Ceiling;
        internal readonly int Level;

        internal ProgressRange(double floor, double ceiling, int level)
        {
            this.Floor = floor;
            this.Ceiling = ceiling;
            this.Level = level;
        }
    }

    /// <summary>직접 스크롤하면 진행 위치 자동 추적을 멈춘다.</summary>
    public void StopProgressFollow() => this._followProgress = false;

    void SynchronizeProgress()
    {
        string season = PassManager.Season?.SeasonId;
        string user = FirebaseAuthService.Instance.UserId;
        long target = PassManager.HasRepeatReward ? PassManager.Exp : Math.Min(PassManager.Exp, PassManager.MaxRequiredExp);
        bool reset = !this._hasProgress || season != this._progressSeason || user != this._progressUser
            || target < this._targetExp;
        if (reset || target != this._targetExp)
        {
            this.StopProgress();
            if (reset) this._displayExp = this._hasProgress && season == this._progressSeason
                && user == this._progressUser && target < this._targetExp ? target : this.ProgressRangeOf(target).Floor;
            this._targetExp = target;
        }
        this._progressSeason = season;
        this._progressUser = user;
        this._hasProgress = PassManager.HasSeason && PassManager.Levels.Count > 0;
    }

    void UpdateProgressPresentation()
    {
        if (!this.isShow || this.m_waitForOpeningRefresh || this.IsViewTransitioning) return;
        if (this._progressSequence == null && this.HasProgressToPlay && !this.m_scrollOnOpen)
            this.PlayProgress();
        if (this._progressSequence != null && this._followProgress)
            this.FollowProgress();
    }

    ProgressRange ProgressRangeOf(double _exp, bool _beforeBoundary = false)
    {
        double floor = 0d;
        int level = 0;
        foreach (PassLevelDefinition entry in PassManager.Levels)
        {
            if (entry == null) continue;
            if (entry.RequiredExp > _exp || (_beforeBoundary && entry.RequiredExp == _exp && _exp > 0d))
                return new ProgressRange(floor, entry.RequiredExp, level);
            floor = entry.RequiredExp;
            level = entry.Level;
        }
        if (PassManager.HasRepeatReward)
        {
            double step = PassManager.Repeat.RequiredExp;
            double cycles = Math.Floor(Math.Max(0d, _exp - floor) / step);
            if (_beforeBoundary && cycles > 0d && _exp == floor + cycles * step) cycles--;
            floor += cycles * step;
            return new ProgressRange(floor, floor + step, level);
        }
        return new ProgressRange(floor, floor, level);
    }

    List<UiGaugeSequence.Step> BuildProgressSteps(double _start, double _target)
    {
        var steps = new List<UiGaugeSequence.Step>();
        bool crossedRepeat = false;
        while (_start < _target)
        {
            ProgressRange range = this.ProgressRangeOf(_start);
            if (range.Ceiling <= _start) break;
            if (crossedRepeat && range.Ceiling < _target)
            {
                // 반복 보상은 첫 경계와 마지막 경계를 남기고 중간 구간을 건너뛴다.
                double lastBoundary = this.ProgressRangeOf(_target).Floor;
                if (lastBoundary > range.Ceiling)
                {
                    range = this.ProgressRangeOf(lastBoundary, true);
                    _start = range.Floor;
                }
            }
            double end = Math.Min(_target, range.Ceiling);
            float fraction = (float)((end - _start) / (range.Ceiling - range.Floor));
            steps.Add(new UiGaugeSequence.Step(_start, end, fraction, end == range.Ceiling));
            crossedRepeat |= PassManager.HasRepeatReward && _start >= PassManager.MaxRequiredExp;
            _start = end;
        }
        return steps;
    }

    void PlayProgress()
    {
        List<UiGaugeSequence.Step> steps = this.BuildProgressSteps(this._displayExp, this._targetExp);
        var timing = new UiGaugeSequence.Timing(this.progressFillDuration, this.progressMinFillDuration,
            this.progressMaxDuration, this.progressBoundaryHoldDuration, this.progressArrivalDuration);
        this._progressSequence = UiGaugeSequence.Create(this.gameObject, steps, timing,
            this.PresentProgress, this.HoldHeaderBoundary, this.CreateProgressArrival, this.ReleaseHeaderBoundary, () =>
            {
                this._progressSequence = null;
                this.PresentProgress(this._targetExp);
            });
        this._progressSequence?.Play();
    }

    Tween CreateProgressArrival(UiGaugeSequence.Step _step, float _duration)
    {
        Sequence arrival = this.HeaderGauge.CreateArrival(_duration, this.progressNodePunch);
        foreach (PassLevelRowView row in this.m_rows)
        {
            if (row == null || !row.gameObject.activeSelf || row.RequiredExp != _step.To) continue;
            Sequence rowArrival = row.CreateArrival(_duration, this.progressNodePunch);
            if (rowArrival == null) continue;
            if (arrival == null) arrival = rowArrival;
            else arrival.Insert(0f, rowArrival);
        }
        return arrival;
    }

    void PresentProgress(double _exp)
    {
        this._displayExp = _exp;
        this.PresentHeaderProgress();
        foreach (PassLevelRowView row in this.m_rows)
            if (row != null && row.gameObject.activeSelf) row.PresentProgress(_exp);
    }

    void StopProgress()
    {
        this._progressSequence?.Kill();
        this._progressSequence = null;
        this._headerHeldRange = null;
    }

    int ProgressRowIndex()
    {
        int index = 0;
        for (int i = 0; i < this.m_rows.Count; i++)
        {
            PassLevelRowView row = this.m_rows[i];
            if (row == null || !row.gameObject.activeSelf) continue;
            if (row.RequiredExp > this._displayExp) break;
            index = i;
        }
        return index;
    }

    void FollowProgress()
    {
        ScrollRect scroll = this.progressScroll;
        if (scroll == null || scroll.content == null) return;
        int index = this.ProgressRowIndex();
        if (index >= this.m_rows.Count) return;
        RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        float height = scroll.content.rect.height - viewport.rect.height;
        if (height <= 0f) return;
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.content, this.m_rows[index].transform);
        float target = Mathf.Clamp01((bounds.center.y - scroll.content.rect.yMin - viewport.rect.height * 0.5f) / height);
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = Mathf.Lerp(scroll.verticalNormalizedPosition, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
    }
}
