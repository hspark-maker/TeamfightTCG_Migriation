using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 화면을 덮은 딤의 밝기 축. 알파는 언제나 authoring 값 그대로 두고 색만 민다 —
// 알파를 내리면 어둠의 두께가 바뀌어 구도가 흔들린다(PackScreenFlash와 같은 규칙).
[Serializable]
public class ScreenDimTint
{
    [SerializeField] Graphic dim;                                               // 미배선이면 이 축이 통째로 무시된다
    [SerializeField] Color   darkColor   = new Color(0.02f, 0.02f, 0.05f, 1f);  // Level -1
    [SerializeField] Color   brightColor = new Color(0.30f, 0.28f, 0.45f, 1f);  // Level +1

    Color m_base;                           // authoring 색. 중간값을 기준으로 잡으면 반복할수록 밀린다
    bool  m_captured;
    float m_level;

    /// <summary>-1 가장 어둡게 ~ 0 평상 ~ +1 가장 밝게.</summary>
    public float Level
    {
        get => this.m_level;
        set
        {
            this.m_level = value;

            if (this.dim == null || !this.m_captured) return;

            Color t_c = value < 0f ? Color.Lerp(this.m_base, this.darkColor, -value)
                                   : Color.Lerp(this.m_base, this.brightColor, value);
            t_c.a          = this.m_base.a;
            this.dim.color = t_c;
        }
    }

    // getter는 트윈이 시작할 때 한 번 읽힌다 — 그래서 앞 구간이 남긴 밝기에서 이어 출발한다.
    public Tween TweenLevel(float _to, float _dur) => DOTween.To(() => this.Level, _v => this.Level = _v, _to, _dur);

    public void Capture()
    {
        if (this.m_captured || this.dim == null) return;

        this.m_captured = true;
        this.m_base     = this.dim.color;
    }

    public void Reset()
    {
        this.m_level = 0f;
        if (this.m_captured && this.dim != null) this.dim.color = this.m_base;
    }
}
