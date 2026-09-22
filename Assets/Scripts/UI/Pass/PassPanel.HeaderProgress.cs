using System;
using UnityEngine;

public partial class PassPanel
{
    [Header("상단 경험치 연출")]
    [SerializeField] RectTransform expLevelBadge;

    ProgressRange? _headerHeldRange;
    bool _headerBoundaryHeld => this._headerHeldRange.HasValue;
    UiGaugeBar _headerGauge;

    UiGaugeBar HeaderGauge => this._headerGauge ??= new UiGaugeBar(
        this.expFill != null ? this.expFill.rectTransform : null, UiGaugeBar.Direction.LeftToRight, this.expLevelBadge);

    void PresentHeaderProgress()
    {
        bool available = PassManager.HasSeason;
        double shown = this._displayExp;
        ProgressRange range = this._headerHeldRange ?? this.ProgressRangeOf(shown);
        double floor = range.Floor;
        double ceiling = range.Ceiling;
        int level = range.Level;
        bool settled = !this._headerBoundaryHeld && !this.HasProgressToPlay && this._progressSequence == null;
        if (settled) level = PassManager.CurrentLevel;
        float ratio = available ? ceiling > floor ? Mathf.Clamp01((float)((shown - floor) / (ceiling - floor))) : 1f : 0f;
        if (this.levelText != null) this.levelText.text = available ? level.ToString() : string.Empty;
        if (this.expText != null)
            this.expText.text = !available ? string.Empty : ceiling > floor
                ? $"{Math.Floor(Math.Max(0d, shown - floor)):N0} / {ceiling - floor:N0}" : "MAX";
        this.HeaderGauge.SetRatio(ratio, available);
    }

    void HoldHeaderBoundary(UiGaugeSequence.Step _step)
    {
        this._headerHeldRange = this.ProgressRangeOf(_step.From);
        this.PresentHeaderProgress();
    }

    void ReleaseHeaderBoundary()
    {
        this._headerHeldRange = null;
        this.PresentHeaderProgress();
    }

}
