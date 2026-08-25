using System;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
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

    [Tooltip("함께 미는 추가 덮개. 화면을 덮은 것이 판 여러 장일 때 쓴다 — 매칭 화면은 검은 딤이 꺼져 있고 " +
             "대각으로 갈린 배경 두 판이 그 자리를 대신한다.\n" +
             "각 판은 자기 저작 색을 기준으로 같은 Level만큼 움직인다(색이 서로 달라도 된다). 비우면 dim 하나만 민다.")]
    [SerializeField] Graphic[] extraDims;

    Color m_base;                           // authoring 색. 중간값을 기준으로 잡으면 반복할수록 밀린다
    Color[] m_extraBases;                   // 추가 덮개의 authoring 색. 판마다 색이 달라 한 벌로 묶을 수 없다
    bool  m_captured;
    float m_level;

    /// <summary>
    /// 덮개 자신. 이 클래스는 알파를 건드리지 않지만(구도가 흔들린다), 화면을 걷어내는 쪽은 알파가 필요하다 —
    /// 진실원을 둘로 만들지 않으려 배선된 대상을 그대로 빌려준다.
    /// </summary>
    public Graphic Target => this.dim;

    /// <summary>-1 가장 어둡게 ~ 0 평상 ~ +1 가장 밝게.</summary>
    public float Level
    {
        get => this.m_level;
        set
        {
            this.m_level = value;

            if (!this.m_captured) return;

            // dim이 비어 있어도 추가 덮개는 밀어야 한다 — 매칭 화면처럼 딤을 끄고 판만 쓰는 배선이 있다.
            if (this.dim != null) this.dim.color = this.Tinted(this.m_base, value);

            if (this.extraDims == null || this.m_extraBases == null) return;

            for (int t_i = 0; t_i < this.extraDims.Length && t_i < this.m_extraBases.Length; t_i++)
            {
                var t_extra = this.extraDims[t_i];
                if (t_extra == null) continue;

                t_extra.color = this.Tinted(this.m_extraBases[t_i], value);
            }
        }
    }

    // getter는 트윈이 시작할 때 한 번 읽힌다 — 그래서 앞 구간이 남긴 밝기에서 이어 출발한다.
    //
    // Tween이 아니라 구체 타입으로 돌려준다 — Tween으로 좁히면 호출부의 .From(1f)가 From(bool isRelative)에 걸린다.
    public TweenerCore<float, float, FloatOptions> TweenLevel(float _to, float _dur)
        => DOTween.To(() => this.Level, _v => this.Level = _v, _to, _dur);

    /// <summary>
    /// 추가 덮개 하나의 <b>기준 색</b>을 옮긴다. Level은 언제나 이 기준 위에서 다시 계산되므로,
    /// 밝기 축(Level)과 색 축(기준 이동)이 같은 Graphic을 쓰면서도 서로를 덮어쓰지 않는다.
    ///
    /// 매칭 화면이 이걸 쓴다 — 상대가 확정되면 배경 두 판이 덱 화면의 섹션 색으로 옮겨 앉는데,
    /// 그동안에도 조임·충돌의 밝기 왕복은 계속 돌아야 한다. 색을 직접 칠하면 둘 중 하나가 반드시 진다.
    /// </summary>
    public void SetExtraBase(Graphic _target, Color _color)
    {
        if (_target == null || this.extraDims == null || this.m_extraBases == null) return;

        for (int t_i = 0; t_i < this.extraDims.Length && t_i < this.m_extraBases.Length; t_i++)
        {
            if (this.extraDims[t_i] != _target) continue;

            this.m_extraBases[t_i] = _color;

            // 새 기준으로 즉시 다시 칠한다 — 다음 Level 변화를 기다리면 그 사이 한 프레임이 옛 색으로 남는다.
            this.Level = this.m_level;

            return;
        }
    }

    /// <summary>추가 덮개의 지금 기준 색. 색 축을 미는 쪽이 출발점을 알아야 한다(없으면 투명을 돌려준다).</summary>
    public Color GetExtraBase(Graphic _target)
    {
        if (_target == null || this.extraDims == null || this.m_extraBases == null) return default;

        for (int t_i = 0; t_i < this.extraDims.Length && t_i < this.m_extraBases.Length; t_i++)
            if (this.extraDims[t_i] == _target) return this.m_extraBases[t_i];

        return default;
    }

    public void Capture()
    {
        // dim이 없어도 추가 덮개만으로 성립한다 — 둘 다 없을 때만 이 축이 통째로 빠진다.
        if (this.m_captured) return;
        if (this.dim == null && (this.extraDims == null || this.extraDims.Length == 0)) return;

        this.m_captured = true;
        if (this.dim != null) this.m_base = this.dim.color;

        if (this.extraDims == null) return;

        this.m_extraBases = new Color[this.extraDims.Length];

        for (int t_i = 0; t_i < this.extraDims.Length; t_i++)
            if (this.extraDims[t_i] != null) this.m_extraBases[t_i] = this.extraDims[t_i].color;
    }

    public void Reset()
    {
        this.m_level = 0f;

        if (!this.m_captured) return;

        if (this.dim != null) this.dim.color = this.m_base;

        if (this.extraDims == null || this.m_extraBases == null) return;

        for (int t_i = 0; t_i < this.extraDims.Length && t_i < this.m_extraBases.Length; t_i++)
            if (this.extraDims[t_i] != null) this.extraDims[t_i].color = this.m_extraBases[t_i];
    }

    // 저작 색을 기준으로 한 단계 민다. 알파는 언제나 저작값 그대로다 — 두께가 바뀌면 구도가 흔들린다.
    Color Tinted(Color _base, float _level)
    {
        Color t_c = _level < 0f ? Color.Lerp(_base, this.darkColor,   -_level)
                                : Color.Lerp(_base, this.brightColor,  _level);
        t_c.a = _base.a;

        return t_c;
    }
}
