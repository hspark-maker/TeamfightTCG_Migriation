using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 카드 뒤에서 조여드는 빛. 축 두 개(짙기·크기)만 열어 두고 언제 얼마나 밀지는 CardEnhanceRitualView가 정한다.
// 미배선이어도 축은 이어진다 — 호출부가 배선 여부를 묻지 않게.
[Serializable]
public class CardEnhanceHalo
{
    [SerializeField] Graphic glow;                                              // 에셋 후보: Sprites/CardPack/Glow_Radial
    [SerializeField] float   idleScale = 1.6f;                                  // 꺼져 있을 때의 크기. 여기서 카드 쪽으로 조여든다

    float m_alpha;
    float m_scale;

    /// <summary>꺼져 있을 때의 크기. 복귀 구간이 여기로 되돌린다.</summary>
    public float IdleScale => this.idleScale;

    public float Alpha
    {
        get => this.m_alpha;
        set
        {
            this.m_alpha = value;

            if (this.glow == null) return;

            Color t_c = this.glow.color;
            t_c.a           = value;
            this.glow.color = t_c;
        }
    }

    public float Scale
    {
        get => this.m_scale;
        set
        {
            this.m_scale = value;
            if (this.glow != null) this.glow.rectTransform.localScale = Vector3.one * value;
        }
    }

    // getter는 트윈이 시작할 때 한 번 읽힌다 — 앞 구간이 남긴 값에서 이어 출발한다.
    public Tween TweenAlpha(float _to, float _dur) => DOTween.To(() => this.Alpha, _v => this.Alpha = _v, _to, _dur);
    public Tween TweenScale(float _to, float _dur) => DOTween.To(() => this.Scale, _v => this.Scale = _v, _to, _dur);

    public void Reset()
    {
        this.Alpha = 0f;
        this.Scale = this.idleScale;
    }
}
