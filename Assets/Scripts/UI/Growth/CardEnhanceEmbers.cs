using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 강화 성공에서 벼려낸 자리에 튀는 불티. 판 여러 장을 authoring 자세에서 띄웠다 꺼뜨린다 —
// 김은 부풀며 흩어지고(식는다) 불티는 작아지며 꺼진다(탄다).
[Serializable]
public class CardEnhanceEmbers
{
    [Tooltip("⚠ 카드의 폭발 스케일을 받는 노드 아래에 두지 않는다 — 함께 부풀면 불티가 아니라 무늬가 된다.")]
    [SerializeField] Graphic[] motes;                                           // 불티 판들. 자리·크기·색을 판마다 다르게 저작할 것(코드는 알파만 건드린다)
    [SerializeField] Material  material;                                        // CardRitualMote. DISTORT 필요 — 미배선이면 매끈하게 미끄러진다
    [SerializeField] float     rise    = 0.7f;                                  // 하나가 솟아 꺼지기까지
    [SerializeField] float     stagger = 0.06f;                                 // 판 사이 시차. 촘촘해야 터진 자리에서 튀어나온 것으로 읽힌다
    [SerializeField] float     travel  = 190f;                                  // 솟는 높이(px)
    [SerializeField] float     spread  = 70f;                                   // 좌우로 벌어지는 폭(px). 0이면 나란히 올라간다
    [SerializeField] float     shrink  = 0.4f;                                  // 꺼질 때 남는 배율. 타 없어지므로 1보다 작아야 한다
    [SerializeField] float     spin    = 70f;                                   // 솟는 동안 도는 각도(도)
    [Range(0f, 1f)]
    [SerializeField] float     alpha   = 0.9f;                                  // 짙기. 저작된 색은 그대로 두고 알파만 여기까지 올린다

    Material   m_mat;
    MotePose[] m_poses;                     // 판의 authoring 자세
    bool       m_captured;

    /// <summary>마지막 불티가 꺼지기까지. 남은 채 복귀가 시작되면 흩날리던 것이 카드와 함께 걷혀 툭 끊긴다.</summary>
    public float Span => this.motes == null || this.motes.Length == 0
                             ? 0f
                             : Mathf.Max(0.1f, this.rise) + Mathf.Max(0f, this.stagger) * (this.motes.Length - 1);

    public void Attach()
    {
        if (this.m_mat != null || this.material == null || this.motes == null) return;

        this.m_mat = new Material(this.material) { name = this.material.name + " (ritual)" };

        foreach (Graphic t_m in this.motes)
        {
            if (t_m != null) t_m.material = this.m_mat;
        }
    }

    public void Release()
    {
        if (this.m_mat != null) UnityEngine.Object.Destroy(this.m_mat);
        this.m_mat = null;
    }

    // 연출 중간값을 기준으로 잡으면 반복할수록 자리가 밀린다 → 1회만 캡처한다.
    public void CapturePoses()
    {
        if (this.m_captured || this.motes == null) return;

        this.m_captured = true;
        this.m_poses    = new MotePose[this.motes.Length];

        // 미배선 칸은 기본값(스케일 0)으로 남지만, 그 칸은 판이 없어 어디서도 읽히지 않는다.
        for (int t_i = 0; t_i < this.motes.Length; t_i++)
        {
            if (this.motes[t_i] != null) this.m_poses[t_i] = new MotePose(this.motes[t_i].rectTransform);
        }
    }

    /// <summary>authoring 자세로 되돌린다. 잘린 채 굳은 높이·크기가 새면 다음 불티가 중간에서 출발한다.</summary>
    public void Reset()
    {
        if (this.motes == null || this.m_poses == null) return;

        for (int t_i = 0; t_i < this.motes.Length; t_i++)
        {
            if (this.motes[t_i] == null) continue;

            SetAlpha(this.motes[t_i], 0f);
            this.m_poses[t_i].ApplyTo(this.motes[t_i].rectTransform);
        }
    }

    public void Insert(Sequence _seq, float _at)
    {
        if (this.motes == null || this.m_poses == null) return;

        float t_rise = Mathf.Max(0.1f, this.rise);

        for (int t_i = 0; t_i < this.motes.Length; t_i++)
        {
            Graphic t_mote = this.motes[t_i];
            if (t_mote == null) continue;

            RectTransform t_rt    = t_mote.rectTransform;
            MotePose      t_pose  = this.m_poses[t_i];
            float         t_start = _at + Mathf.Max(0f, this.stagger) * t_i;

            // 인덱스로 흩는다 — 난수를 쓰면 같은 강화가 매번 다르게 보이고 저작된 자리와도 어긋난다.
            float t_dir = t_i % 2 == 0 ? -1f : 1f;
            float t_far = 0.7f + (t_i % 3) * 0.25f;

            _seq.InsertCallback(t_start, () =>
            {
                SetAlpha(t_mote, 0f);
                t_pose.ApplyTo(t_rt);
            });

            _seq.Insert(t_start,                  t_mote.DOFade(this.alpha, t_rise * 0.15f).SetEase(Ease.OutQuad));
            _seq.Insert(t_start + t_rise * 0.35f, t_mote.DOFade(0f, t_rise * 0.65f).SetEase(Ease.InQuad));

            // 축을 갈라 민다 — 위로는 튀어 올랐다 느려지고(OutQuad) 옆으로는 뒤늦게 흘러(InOutSine),
            // 두 이징이 어긋나며 경로가 직선이 아니라 호가 된다.
            _seq.Insert(t_start, t_rt.DOAnchorPosY(t_pose.Anchored.y + this.travel * t_far, t_rise).SetEase(Ease.OutQuad));
            _seq.Insert(t_start, t_rt.DOAnchorPosX(t_pose.Anchored.x + this.spread * t_dir * t_far, t_rise).SetEase(Ease.InOutSine));

            _seq.Insert(t_start, t_rt.DOScale(t_pose.Scale * this.shrink, t_rise).SetEase(Ease.InQuad));
            _seq.Insert(t_start, t_rt.DOLocalRotate(t_pose.Rotation.eulerAngles + new Vector3(0f, 0f, this.spin * t_dir), t_rise)
                                     .SetEase(Ease.OutSine));
        }
    }

    static void SetAlpha(Graphic _g, float _a)
    {
        if (_g == null) return;

        Color t_c = _g.color;
        t_c.a = _a;
        _g.color = t_c;
    }

    // 판 하나의 authoring 자세. 세 배열로 흩어 두면 인덱스가 어긋날 때 조용히 틀어진다.
    readonly struct MotePose
    {
        public readonly Vector2    Anchored;
        public readonly Vector3    Scale;
        public readonly Quaternion Rotation;

        public MotePose(RectTransform _rt)
        {
            this.Anchored = _rt.anchoredPosition;
            this.Scale    = _rt.localScale;
            this.Rotation = _rt.localRotation;
        }

        public void ApplyTo(RectTransform _rt)
        {
            _rt.anchoredPosition = this.Anchored;
            _rt.localScale       = this.Scale;
            _rt.localRotation    = this.Rotation;
        }
    }
}
