using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 진화 충전에서 카드 뒤에 하나 둘 켜지는 빛줄기.
// 담금질의 불티(CardEnhanceEmbers)와 반대 벡터다 — 불티는 터진 뒤 흩어지고, 줄기는 공개 프레임에 카드로 빨려든다.
// 시간축은 모른다: 언제 몇 번째를 켤지는 CardEvolveRitualView가 정한다.
[Serializable]
public class CardEvolveRays
{
    [Tooltip("⚠ 카드(CardSlot)의 자식으로 두지 않는다 — burst 스케일을 함께 받으면 줄기가 아니라 무늬가 된다.\n" +
             "  CardPad의 자식이면서 CardSlot보다 **앞선 형제**여야 카드 뒤로 간다(uGUI는 나중 형제를 위에 그린다).\n" +
             "\n" +
             "저작 규약 세 가지 (어기면 광선으로 안 읽힌다):\n" +
             "  · 피벗은 (0.5, 0) — 뿌리가 카드 중심이고 거기서 바깥으로 뻗는다.\n" +
             "    중심 피벗이면 스프라이트의 밝은 한복판이 카드에 가려 흐린 꼬리만 남는다.\n" +
             "  · 폭은 길이의 0.12 안팎. 0.2를 넘기면 광선이 아니라 꽃잎이 된다.\n" +
             "  · 각도를 등간격으로 두지 말 것 — 나란하면 바람개비로 읽힌다.")]
    [SerializeField] Graphic[] rays;                                            // 줄기 판들. 각도·길이·색은 판마다 저작(코드는 알파·배율·회전만 민다)

    [Header("점화 — 하나가 뻗어 나온다")]
    [SerializeField] float igniteSeedWidth  = 0.55f;                            // 켜지기 직전의 폭 배율(authoring 대비)
    [SerializeField] float igniteSeedLength = 0.2f;                             // 켜지기 직전의 길이 배율. 여기서 뻗어 나온다
    [Range(0f, 1f)]
    [SerializeField] float litAlpha = 0.55f;                                    // 점화 뒤 유지 밝기. 1이면 다음 줄기가 켜질 자리가 남지 않는다

    [Header("일제 — 전부 최대")]
    [Range(0f, 1f)]
    [SerializeField] float flareAlpha = 1f;
    [SerializeField] float flareScale = 1.18f;                                  // authoring 대비 부푸는 배율

    [Header("회수 — 카드로 빨려든다")]
    [SerializeField] float retractDelay  = 0.1f;                                // 공개 프레임에서 얼마나 뒤에 거두나. 0이면 백열과 같이 사라져 안 보인다
    [SerializeField] float retractSweep  = 0.45f;
    [SerializeField] float retractLength = 0.05f;                               // 거둘 때 남는 길이 배율. 폭보다 훨씬 줄어야 '빨려든다'가 된다
    [SerializeField] float retractWidth  = 0.8f;

    RayPose[] m_poses;                      // 판의 authoring 자세
    bool      m_captured;

    /// <summary>줄기가 배선돼 있는가. 미배선이면 뷰가 이 축을 통째로 건너뛴다.</summary>
    public bool HasRays => this.rays != null && this.rays.Length > 0;

    /// <summary>마지막 줄기가 다 거둬지기까지. 결과 구간이 이 자리를 담아야 복귀가 그 위를 덮치지 않는다.</summary>
    public float RetractSpan => HasRays ? Mathf.Max(0f, this.retractDelay) + Mathf.Max(0.05f, this.retractSweep) : 0f;

    /// <summary>authoring 자세를 1회만 잡는다(중간값을 기준으로 잡으면 반복할수록 밀린다).</summary>
    public void CapturePoses()
    {
        if (this.m_captured || this.rays == null) return;

        this.m_captured = true;
        this.m_poses    = new RayPose[this.rays.Length];

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            if (this.rays[t_i] != null) this.m_poses[t_i] = new RayPose(this.rays[t_i].rectTransform);
        }
    }

    /// <summary>authoring 자세로 되돌린다. 잘린 채 굳은 각도·길이가 새면 다음 판이 중간에서 출발한다.</summary>
    public void Reset()
    {
        if (this.rays == null || this.m_poses == null) return;

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            if (this.rays[t_i] == null) continue;

            SetAlpha(this.rays[t_i], 0f);
            this.m_poses[t_i].ApplyTo(this.rays[t_i].rectTransform);
        }
    }

    /// <summary>_index번 줄기 하나를 켠다.</summary>
    public void InsertIgnite(Sequence _seq, int _index, float _at, float _dur)
    {
        if (!TryGet(_index, out Graphic t_ray, out RayPose t_pose)) return;

        RectTransform t_rt  = t_ray.rectTransform;
        float         t_dur = Mathf.Max(0.05f, _dur);

        InsertSeed(_seq, _at, t_ray, t_rt, t_pose);

        // 밝기가 먼저, 길이는 뒤따라 뻗는다 — 순서가 반대면 판이 미끄러져 들어온 것으로 보인다.
        _seq.Insert(_at, t_ray.DOFade(this.litAlpha, t_dur * 0.45f).SetEase(Ease.OutQuad));
        _seq.Insert(_at, t_rt.DOScale(t_pose.Scale, t_dur).SetEase(Ease.OutCubic));
    }

    /// <summary>전부 최대로. _fromIndex부터는 아직 안 켜진 줄기라 자세를 못 박고 켜지만,
    /// 그 앞은 이미 켜져 있으므로 밝기·배율만 끌어올린다(자세를 다시 얹으면 켜져 있던 것이 되감긴다).</summary>
    public void InsertFlare(Sequence _seq, float _at, float _dur, int _fromIndex)
    {
        if (this.rays == null || this.m_poses == null) return;

        float t_dur = Mathf.Max(0.05f, _dur);

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            if (!TryGet(t_i, out Graphic t_ray, out RayPose t_pose)) continue;

            RectTransform t_rt = t_ray.rectTransform;

            if (t_i >= _fromIndex) InsertSeed(_seq, _at, t_ray, t_rt, t_pose);

            _seq.Insert(_at, t_ray.DOFade(this.flareAlpha, t_dur * 0.6f).SetEase(Ease.OutQuad));
            _seq.Insert(_at, t_rt.DOScale(t_pose.Scale * this.flareScale, t_dur).SetEase(Ease.OutQuad));
        }
    }

    /// <summary>저작된 부채꼴을 유지한 채 통째로 돌린다.</summary>
    /// <remarks>⚠ 백열에 덮인 구간에서 끝나면 아무도 못 본다 — 공개 구간까지 물고 이어져야 각도 변화가 읽힌다.
    /// 같은 시각에 <see cref="InsertFlare"/>를 거는 경우 그쪽을 <b>먼저</b> Insert할 것(자세 못 박기가 회전 시작보다 앞서야 한다).</remarks>
    public void InsertSpin(Sequence _seq, float _at, float _dur, float _deg)
    {
        if (this.rays == null || this.m_poses == null) return;

        float t_dur = Mathf.Max(0.05f, _dur);

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            if (!TryGet(t_i, out Graphic t_ray, out RayPose t_pose)) continue;

            // 목표를 authoring 각도에서 절대값으로 잡는다 — 상대 회전은 잘린 판에서 반복할수록 밀린다.
            Vector3 t_to = t_pose.Rotation.eulerAngles + new Vector3(0f, 0f, _deg);
            _seq.Insert(_at, t_ray.rectTransform.DOLocalRotate(t_to, t_dur).SetEase(Ease.InOutSine));
        }
    }

    /// <summary>길이가 먼저 무너지고 밝기가 뒤따라 꺼진다 — 순서가 반대면 '꺼졌다'이지 '빨려들었다'가 아니다.</summary>
    public void InsertRetract(Sequence _seq, float _at)
    {
        if (this.rays == null || this.m_poses == null) return;

        float t_from = _at + Mathf.Max(0f, this.retractDelay);
        float t_dur  = Mathf.Max(0.05f, this.retractSweep);

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            if (!TryGet(t_i, out Graphic t_ray, out RayPose t_pose)) continue;

            Vector3 t_end = Vector3.Scale(t_pose.Scale, new Vector3(this.retractWidth, this.retractLength, 1f));

            _seq.Insert(t_from, t_ray.rectTransform.DOScale(t_end, t_dur).SetEase(Ease.InQuad));
            _seq.Insert(t_from + t_dur * 0.35f, t_ray.DOFade(0f, t_dur * 0.65f).SetEase(Ease.InQuad));
        }
    }

    // 켜지기 직전의 자세를 시작 프레임에 못 박는다 — "한 번 더"로 이어온 길에는 Reset이 지나가지 않는다.
    void InsertSeed(Sequence _seq, float _at, Graphic _ray, RectTransform _rt, RayPose _pose)
    {
        Vector3 t_seed = Vector3.Scale(_pose.Scale, new Vector3(this.igniteSeedWidth, this.igniteSeedLength, 1f));

        _seq.InsertCallback(_at, () =>
        {
            SetAlpha(_ray, 0f);
            _pose.ApplyTo(_rt);
            _rt.localScale = t_seed;
        });
    }

    bool TryGet(int _index, out Graphic _ray, out RayPose _pose)
    {
        _ray  = null;
        _pose = default;

        if (this.rays == null || this.m_poses == null) return false;
        if (_index < 0 || _index >= this.rays.Length) return false;

        _ray  = this.rays[_index];
        _pose = this.m_poses[_index];

        return _ray != null;
    }

    static void SetAlpha(Graphic _g, float _a)
    {
        if (_g == null) return;

        Color t_c = _g.color;
        t_c.a = _a;
        _g.color = t_c;
    }

    // 판 하나의 authoring 자세. 세 배열로 흩어 두면 인덱스가 어긋날 때 조용히 틀어진다(MotePose와 같은 규약).
    readonly struct RayPose
    {
        public readonly Vector2    Anchored;
        public readonly Vector3    Scale;
        public readonly Quaternion Rotation;

        public RayPose(RectTransform _rt)
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
