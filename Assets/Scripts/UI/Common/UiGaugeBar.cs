using DG.Tweening;
using UnityEngine;

/// <summary>기존 채움 사각과 도달 노드를 사용하는 게이지 Tween 연출.</summary>
public sealed class UiGaugeBar
{
    public enum Direction { LeftToRight, RightToLeft, TopToBottom, BottomToTop }

    readonly RectTransform _fill;
    readonly Transform _node;
    readonly Direction _direction;
    readonly Vector3 _nodeScale;

    public float Ratio { get; private set; }

    /// <summary>채움은 부모를 기준으로 스트레치한 사각을 전달한다. 새 UI 오브젝트는 생성하지 않는다.</summary>
    public UiGaugeBar(RectTransform fill, Direction direction, Transform node = null)
    {
        this._fill = fill;
        this._direction = direction;
        this._node = node;
        this._nodeScale = node != null ? node.localScale : Vector3.one;
    }

    /// <summary>실제 진행 비율을 즉시 표시한다. 0이면 채움을 숨긴다.</summary>
    public void SetRatio(float ratio, bool available = true)
    {
        this.ApplyRatio(ratio, available);
    }

    /// <summary>부모 시퀀스에 넣을 도달 팝을 만든다. 완료·중단 시 원래 배율을 복원한다.</summary>
    public Sequence CreateArrival(float duration, float punch)
    {
        if (this._node == null) return null;
        duration = Mathf.Max(0.01f, duration);
        Sequence arrival = DOTween.Sequence().Pause();
        arrival.Append(this._node.DOScale(this._nodeScale * (1f + Mathf.Max(0f, punch)), duration * 0.28f).SetEase(Ease.OutQuad));
        arrival.Append(this._node.DOScale(this._nodeScale, duration * 0.72f).SetEase(Ease.OutBack));
        arrival.OnComplete(this.ResetArrival);
        arrival.OnKill(this.ResetArrival);
        return arrival;
    }

    /// <summary>도달 노드의 저작 배율을 복원한다.</summary>
    public void ResetArrival()
    {
        if (this._node != null) this._node.localScale = this._nodeScale;
    }

    void ApplyRatio(float ratio, bool available)
    {
        this.Ratio = Mathf.Clamp01(ratio);
        if (this._fill == null) return;
        float visible = available ? this.Ratio : 0f;
        switch (this._direction)
        {
            case Direction.LeftToRight:
                this._fill.anchorMin = Vector2.zero;
                this._fill.anchorMax = new Vector2(visible, 1f);
                break;
            case Direction.RightToLeft:
                this._fill.anchorMin = new Vector2(1f - visible, 0f);
                this._fill.anchorMax = Vector2.one;
                break;
            case Direction.TopToBottom:
                this._fill.anchorMin = new Vector2(0f, 1f - visible);
                this._fill.anchorMax = Vector2.one;
                break;
            case Direction.BottomToTop:
                this._fill.anchorMin = Vector2.zero;
                this._fill.anchorMax = new Vector2(1f, visible);
                break;
        }
        this._fill.gameObject.SetActive(visible > 0f);
    }

}
