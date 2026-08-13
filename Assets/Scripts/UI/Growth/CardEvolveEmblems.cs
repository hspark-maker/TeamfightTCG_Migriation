using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 진화로 새로 열리는 카드 프레임 문양(CardVisualView.keywordFrames)이 새겨지는 연출.
// 빛에서 응결되듯 흰빛으로 커진 채 나타나 제 자리로 조여들고, 박히는 순간 제 색으로 물러난다.
//
// 대상은 인스펙터가 아니라 런타임 주입이다 — 이번 진화로 무엇이 열리는지는 카드마다 다르다.
// 시간축은 모른다(CardEvolveRays와 같은 결): 언제 새길지는 CardEvolveRitualView가 정한다.
[Serializable]
public class CardEvolveEmblems
{
    [Tooltip("문양 하나가 새겨지는 시간.")]
    [SerializeField] float engraveDur = 0.28f;

    [Tooltip("문양끼리 밀리는 간격. 0이면 전부 한 번에 떠 '켜졌다'가 되고 새겨지는 것으로 안 읽힌다.")]
    [SerializeField] float stagger = 0.07f;

    [Tooltip("나타나기 시작할 때의 크기 배율. 여기서 제 자리로 조여든다.")]
    [SerializeField] float seedScale = 1.35f;

    [Tooltip("박히기 전의 과노출 색. 카드가 머금은 빛과 같은 계열이어야 '빛이 굳었다'로 읽힌다.")]
    [SerializeField] Color flashColor = new Color(1f, 0.97f, 0.88f, 1f);

    // authoring 색·배율. 연출이 잘려도 여기로 되돌린다 — 최종 상태는 언제나 "제 색으로 켜진 문양"이다.
    readonly List<Graphic> m_targets = new List<Graphic>();
    readonly List<Color>   m_colors  = new List<Color>();
    readonly List<Vector3> m_scales  = new List<Vector3>();

    /// <summary>새길 문양이 있는가. 없으면 뷰가 이 축을 통째로 건너뛴다.</summary>
    public bool HasEmblems => this.m_targets.Count > 0;

    /// <summary>마지막 문양이 다 새겨지기까지. 결과 구간이 이 자리를 담아야 복귀가 그 위를 덮치지 않는다.</summary>
    public float Span => HasEmblems
                             ? Mathf.Max(0f, this.stagger) * (this.m_targets.Count - 1) + Mathf.Max(0.05f, this.engraveDur)
                             : 0f;

    /// <summary>이번 판에 새길 문양들. 앞 판의 대상은 여기서 되돌려 놓고 갈아끼운다.</summary>
    public void SetTargets(IReadOnlyList<Graphic> _targets)
    {
        Restore();

        this.m_targets.Clear();
        this.m_colors.Clear();
        this.m_scales.Clear();

        if (_targets == null) return;

        foreach (Graphic t_g in _targets)
        {
            if (t_g == null) continue;

            this.m_targets.Add(t_g);
            this.m_colors.Add(t_g.color);
            this.m_scales.Add(t_g.rectTransform.localScale);
        }
    }

    /// <summary>새겨지기 직전의 모습을 못 박는다. 켜지는 프레임(빛 아래)에 걸어야
    /// 호출부가 SetActive로 켠 문양이 그대로 보이지 않는다.</summary>
    public void InsertSeed(Sequence _seq, float _at)
    {
        if (!HasEmblems) return;

        _seq.InsertCallback(_at, ApplySeed);
    }

    /// <summary>문양을 하나씩 새긴다.</summary>
    public void InsertEngrave(Sequence _seq, float _at)
    {
        if (!HasEmblems) return;

        float t_dur = Mathf.Max(0.05f, this.engraveDur);

        for (int t_i = 0; t_i < this.m_targets.Count; t_i++)
        {
            Graphic t_g = this.m_targets[t_i];
            if (t_g == null) continue;

            Color t_base = this.m_colors[t_i];
            float t_from = _at + Mathf.Max(0f, this.stagger) * t_i;

            // 색 트윈은 순차다 — 한 축을 두 트윈이 겹쳐 밀면 뒷마디가 앞마디의 중간값에서 출발한다.
            Color t_lit = new Color(this.flashColor.r, this.flashColor.g, this.flashColor.b, t_base.a);
            _seq.Insert(t_from, t_g.DOColor(t_lit, t_dur * 0.4f).SetEase(Ease.OutQuad));
            _seq.Insert(t_from + t_dur * 0.4f, t_g.DOColor(t_base, t_dur * 0.6f).SetEase(Ease.InOutSine));

            _seq.Insert(t_from, t_g.rectTransform.DOScale(this.m_scales[t_i], t_dur).SetEase(Ease.OutCubic));
        }
    }

    /// <summary>authoring 색·배율로 되돌린다(멱등). 새기다 잘려도 문양은 켜진 채 남아야 한다.</summary>
    public void Restore()
    {
        for (int t_i = 0; t_i < this.m_targets.Count; t_i++)
        {
            Graphic t_g = this.m_targets[t_i];
            if (t_g == null) continue;

            t_g.DOKill();
            t_g.rectTransform.DOKill();

            t_g.color                    = this.m_colors[t_i];
            t_g.rectTransform.localScale = this.m_scales[t_i];
        }
    }

    void ApplySeed()
    {
        for (int t_i = 0; t_i < this.m_targets.Count; t_i++)
        {
            Graphic t_g = this.m_targets[t_i];
            if (t_g == null) continue;

            Color t_c = this.flashColor;
            t_c.a = 0f;

            t_g.color                    = t_c;
            t_g.rectTransform.localScale = this.m_scales[t_i] * this.seedScale;
        }
    }
}
