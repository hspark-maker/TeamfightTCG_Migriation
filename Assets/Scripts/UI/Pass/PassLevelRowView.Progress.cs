using UnityEngine;

public partial class PassLevelRowView
{
    double? _displayExp;
    UiGaugeBar _gaugeBar;

    internal long RequiredExp => this.m_definition?.RequiredExp ?? 0;

    UiGaugeBar GaugeBar => this._gaugeBar ??= new UiGaugeBar(this.levelFill,
        UiGaugeBar.Direction.TopToBottom, this.reachedLevelRoot != null ? this.reachedLevelRoot.transform : null);

    internal void PresentProgress(double _exp)
    {
        this._displayExp = _exp;
        if (this.m_definition == null) return;
        bool reached = _exp >= this.RequiredExp;
        if (this.reachedLevelRoot != null) this.reachedLevelRoot.SetActive(reached);
        if (this.lockedLevelRoot != null) this.lockedLevelRoot.SetActive(!reached);
        if (this.connectorRoot != null) this.connectorRoot.SetActive(this.m_nextRequiredExp.HasValue);
        long span = (this.m_nextRequiredExp ?? this.RequiredExp) - this.RequiredExp;
        float fill = span > 0 ? Mathf.Clamp01((float)((_exp - this.RequiredExp) / span)) : 0f;
        this.GaugeBar.SetRatio(fill, reached && span > 0 && fill > 0f);
    }

    internal DG.Tweening.Sequence CreateArrival(float _duration, float _punch) => this.GaugeBar.CreateArrival(_duration, _punch);

    void OnDisable() => this._gaugeBar?.ResetArrival();
}
