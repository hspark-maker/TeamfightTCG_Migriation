using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 덱 화면이 매칭 화면에게 내주는 자리들. 매칭 셸이 덱 화면의 타입을 몰라도 되게 이 구조체 하나로 건넨다 —
/// 두 오버레이가 서로를 직접 알면 어느 쪽도 혼자 열 수 없게 된다.
/// </summary>
public readonly struct MatchHandoffTargets
{
    /// <summary>VS가 교대할 자리(VsBadge). 매칭 VS가 흐려지는 그 자리에서 이쪽이 올라온다.</summary>
    public readonly RectTransform VersusSeat;

    /// <summary>덱 화면의 루트. 전환이 이걸 통째로 당겨 들인다(카메라가 뒤로 빠지는 축의 절반).</summary>
    public readonly RectTransform DeckRoot;

    /// <summary>덱 화면이 스스로 세운 등장 안무. 이미 감춘 상태로 와야 한다 — 여기서 다시 감추지 않는다.</summary>
    public readonly Sequence Intro;

    public MatchHandoffTargets(RectTransform _versusSeat, RectTransform _deckRoot, Sequence _intro)
    {
        this.VersusSeat = _versusSeat;
        this.DeckRoot   = _deckRoot;
        this.Intro      = _intro;
    }
}

// 매칭 화면 → 덱 화면 전환의 안무. 커튼으로 덮지 않는다 —
// 두 화면은 상대 위 / VS 가운데 / 나 아래로 축이 같아서, 가리면 오히려 같은 무대라는 사실이 지워진다.
//
// 동사는 "사라진다"가 아니라 "밀어 연다"이다. VS가 한 번 더 내려찍히며 빛줄기가 위아래로 뻗고,
// 그 결에 실려 두 배너가 프레임 밖으로 밀려나면서 밑에 이미 서 있던 덱 화면이 드러난다.
// 동시에 매칭 화면은 물러나고(축소) 덱 화면은 들어온다(확대) — 두 화면이 같은 공간이라는 주장이 이 축이다.
// VS만은 두 축 어디에서도 크기가 변하지 않는다. 그게 두 화면을 꿰는 못이다.
//
// ⚠ 오브젝트 참조가 하나도 없다. 전부 C# 이니셜라이저 기본값이라 배선 없이 돈다 —
//   빛줄기 그림조차 매칭 화면(MatchmakingFx.RaySprite)에서 빌려 온다.
[Serializable]
public class MatchHandoffFx
{
    [Header("재타격 — 갈라짐의 원인")]
    [Tooltip("VS가 한 번 더 내려찍히는 시간. 이 한 번이 없으면 배너가 '왜' 밀려나는지 화면에 없다.")]
    [Min(0.01f)] public float vsStrike = 0.07f;

    [Tooltip("내려찍히기 직전 배율. t=0에 즉시 적용되고 트윈은 회복만 한다 — 부푸는 과정을 보여주면 타격이 뭉개진다.")]
    [Min(1f)] public float vsStrikeScale = 1.16f;

    [Header("갈라짐 — 배너가 밀려난다")]
    [Tooltip("배너가 프레임 밖으로 밀려나는 시간.")]
    [Min(0.01f)] public float partSweep = 0.24f;

    [Tooltip("밀려나는 거리(px). 화면 밖까지 나가야 '치웠다'가 아니라 '밀렸다'로 읽힌다 — 짧으면 도중에 증발한다.")]
    [Min(0f)] public float partDistance = 620f;

    [Tooltip("밀려나는 길의 몇 지점부터 흐려지기 시작하는가(0~1). 1에 가까울수록 끝에서만 흐려져 " +
             "'사라진 것'이 아니라 '밀려 나간 것'이 된다 — 0.5 아래로 내리면 제자리에서 증발한다.")]
    [Range(0f, 1f)] public float partFadeStart = 0.62f;

    [Header("빛줄기 — 갈라지는 결")]
    [Tooltip("VS 뿌리에서 위/아래로 뻗는 길이(px). 화면 절반을 넘겨야 결이 끝까지 그어진다.")]
    [Min(0f)] public float rayLength = 900f;

    [Tooltip("길이 대비 폭. 0.2를 넘기면 광선이 아니라 꽃잎이 된다(CardEvolveRays와 같은 저작 규약).")]
    [Range(0.02f, 0.4f)] public float rayWidthRatio = 0.12f;

    [Min(0.01f)] public float rayRise = 0.08f;
    [Min(0.01f)] public float rayFall = 0.26f;

    public Color rayColor = new Color(0.72f, 0.88f, 1f, 1f);

    [Range(0f, 1f)] public float rayAlpha = 0.9f;

    [Header("물러남 — 카메라가 빠진다")]
    [Tooltip("매칭 화면이 물러나는 배율. 덱 화면이 들어오는 배율과 짝이라 한쪽만 만지면 축이 어긋난다.")]
    [Min(0.1f)] public float matchEndScale = 0.86f;

    [Tooltip("덱 화면이 들어오기 시작하는 배율. 1보다 커야 '뒤로 빠지며 드러난다'가 된다.")]
    [Min(0.1f)] public float deckStartScale = 1.08f;

    [Min(0.01f)] public float zoomDuration = 0.30f;

    [Header("딤")]
    [Tooltip("어둠이 걷히기 시작하는 시각(초). 0이면 배너가 움직이기도 전에 덱 화면이 드러난다.")]
    [Min(0f)] public float dimOutAt = 0.05f;

    [Tooltip("어둠이 걷히는 시간.")]
    [Min(0.01f)] public float dimOutFade = 0.26f;

    [Header("덱 등장")]
    [Tooltip("덱 화면 등장 안무가 시작하는 시각(초). 배너가 아직 밀려나는 중에 겹쳐야 두 화면이 한 사건으로 읽힌다.")]
    [Min(0f)] public float deckAt = 0.06f;

    [Header("VS 교대")]
    [Tooltip("매칭 VS를 덱 VsBadge에게 넘기는 시각(초). 둘은 같은 자리에 있고 모양만 달라, " +
             "서로 반대 방향으로 움직이며 교차해야 '넘겨받았다'로 읽힌다 — 그냥 끄면 크기가 튄다.")]
    [Min(0f)] public float vsHandoverAt = 0.15f;

    [Min(0.01f)] public float vsHandoverFade = 0.13f;

    /// <summary>
    /// 매칭 화면이 실제로 내려가야 하는 시각. 딤이 다 걷히고 배너가 다 나간 뒤다 —
    /// 이보다 일찍 내리면 남은 어둠이 한 프레임에 사라져 전환 한복판이 번쩍인다.
    /// </summary>
    public float CloseAt => Mathf.Max(Mathf.Max(this.partSweep, this.dimOutAt + this.dimOutFade),
                                      Mathf.Max(this.zoomDuration, this.vsHandoverAt + this.vsHandoverFade));

    /// <summary>전환 안무를 만들어 돌려준다(재생은 호출자).</summary>
    /// <param name="_riders">
    /// 배너에 실리지 않은 것들(제목·취소 버튼). 각자의 y가 VS보다 위면 위로, 아래면 아래로 간다 —
    /// 어느 부품이 어디 있는지 이 클래스는 몰라도 된다.
    /// </param>
    public Sequence Build(MatchProfileView _my, MatchProfileView _opponent, RectTransform _versus,
                          Graphic _dim, RectTransform _matchRoot, RectTransform[] _riders,
                          Sprite _raySprite, in MatchHandoffTargets _targets)
    {
        Sequence t_seq = this.BuildCore(_my, _opponent, _versus, _dim, _matchRoot, _riders,
                                        _raySprite, _targets.VersusSeat);

        // 넘겨줄 화면이 있는 경로에서만 도는 축들 — 카메라가 뒤로 빠지듯 이 화면이 줄고 저 화면이 당겨 들어온다.
        this.StageZoom(t_seq, _matchRoot, _targets.DeckRoot);

        if (_targets.Intro != null) t_seq.Insert(this.deckAt, _targets.Intro);

        return t_seq;
    }

    /// <summary>화면을 통째로 데려가는 전환(씬 교체)용 안무. 부품이 실려 나가는 것은 <see cref="Build"/>와 같고,
    /// 넘겨받을 다음 화면이 없어 덱 쪽 축(당겨 들이기·VS 교대 상대·등장 안무)만 빠진다.
    ///
    /// <para>⚠ 무엇보다 <b>루트를 줄이지 않는다</b>. 배경 두 판이 그 루트에 실려 있어서, 줄이는 순간
    /// 기울어진 판 귀퉁이 뒤로 다음 씬이 샌다 — 덱 화면으로 넘어갈 때는 그 밑이 어차피 덱이라 문제가 없었다.</para></summary>
    public Sequence BuildCarry(MatchProfileView _my, MatchProfileView _opponent, RectTransform _versus,
                               Graphic _dim, RectTransform _matchRoot, RectTransform[] _riders,
                               Sprite _raySprite)
    {
        return this.BuildCore(_my, _opponent, _versus, _dim, _matchRoot, _riders, _raySprite, null);
    }

    // 두 전환이 공유하는 몸통 — 부품을 VS 기준 위아래로 밀어내고 딤을 걷는다.
    Sequence BuildCore(MatchProfileView _my, MatchProfileView _opponent, RectTransform _versus,
                       Graphic _dim, RectTransform _matchRoot, RectTransform[] _riders,
                       Sprite _raySprite, RectTransform _versusSeat)
    {
        var t_seq = DOTween.Sequence();

        // 위/아래를 가르는 기준선은 VS다. 어느 배너가 위인지 프리팹을 몰라도 되는 이유가 이 한 줄이다.
        float t_axis = _versus != null ? _versus.anchoredPosition.y : 0f;

        this.StageStrike(t_seq, _versus);
        this.StageRays(t_seq, _versus, _matchRoot, _raySprite, _dim);

        // ?. 을 쓰지 않는다 — UnityEngine.Object의 가짜 null은 null 조건 연산자가 걸러 주지 못한다.
        if (_opponent != null) this.StagePart(t_seq, _opponent.Rect, _opponent.Group, t_axis);
        if (_my       != null) this.StagePart(t_seq, _my.Rect,       _my.Group,       t_axis);

        this.StageRiders(t_seq, _riders, t_axis);

        this.StageVersusHandover(t_seq, _versus, _versusSeat);

        // ScreenDimTint는 밝기만 미는 축이라(알파는 저작값 고정) 걷어내는 일은 여기서 직접 한다.
        if (_dim != null)
        {
            _dim.DOKill();
            t_seq.Insert(this.dimOutAt, _dim.DOFade(0f, this.dimOutFade).SetEase(Ease.InQuad));
        }

        return t_seq;
    }

    /// <summary>전환이 세운 중간값을 저작 상태로 되돌린다. 잘려도 화면이 밀려나거나 투명한 채로 굳지 않게.</summary>
    public void Reset(RectTransform _matchRoot, RectTransform _versus)
    {
        if (_matchRoot != null)
        {
            _matchRoot.DOKill();
            _matchRoot.localScale = Vector3.one;
        }

        if (_versus == null) return;

        _versus.DOKill();
        _versus.localScale = Vector3.one;

        var t_group = _versus.GetComponent<CanvasGroup>();
        if (t_group == null) return;

        t_group.DOKill();
        t_group.alpha = 1f;
    }

    // VS 재타격. 배율을 t=0에 즉시 밀어 넣고 트윈은 회복만 한다 — 갈라짐의 원인이 되려면 이미 큰 것이 내려꽂혀야 한다.
    void StageStrike(Sequence _seq, RectTransform _versus)
    {
        if (_versus == null) return;

        _versus.DOKill();
        _versus.localScale = Vector3.one * this.vsStrikeScale;

        _seq.Insert(0f, _versus.DOScale(1f, this.vsStrike).SetEase(Ease.InQuad));
    }

    // 배너 하나를 프레임 밖으로 민다. 방향은 그 배너가 VS의 어느 쪽에 있었는지가 정한다.
    // 부모를 옮기지 않는다 — 갈아타면 덱 화면 레이아웃이 이 배너를 칸으로 세어 실제 화면이 밀린다.
    void StagePart(Sequence _seq, RectTransform _rect, CanvasGroup _group, float _axisY)
    {
        if (_rect == null) return;

        _rect.DOKill();

        float   t_sign = _rect.anchoredPosition.y >= _axisY ? 1f : -1f;
        Vector2 t_to   = _rect.anchoredPosition + new Vector2(0f, t_sign * this.partDistance);

        // 가속이라 "밀려 나갔다"가 된다. 감속이면 스스로 물러난 것으로 보인다.
        _seq.Insert(0f, _rect.DOAnchorPos(t_to, this.partSweep).SetEase(Ease.InCubic));

        if (_group == null) return;

        _group.DOKill();
        _group.alpha = 1f;

        // 끝에서만 흐려진다 — 일찍 흐려지면 제자리에서 증발한 것으로 보여 밀린 사실이 지워진다.
        float t_fadeAt = this.partSweep * this.partFadeStart;
        _seq.Insert(t_fadeAt, _group.DOFade(0f, this.partSweep - t_fadeAt).SetEase(Ease.InQuad));
    }

    // 제목·취소 버튼처럼 배너에 실리지 않은 것들. 그냥 꺼지면 전환 한복판에서 두 물건이 증발한다.
    void StageRiders(Sequence _seq, RectTransform[] _riders, float _axisY)
    {
        if (_riders == null) return;

        for (int t_i = 0; t_i < _riders.Length; t_i++)
        {
            var t_rider = _riders[t_i];
            if (t_rider == null) continue;

            this.StagePart(_seq, t_rider, ResolveGroup(t_rider), _axisY);
        }
    }

    // 매칭은 물러나고 덱은 들어온다. 같은 시간에 서로 반대로 움직여야 두 화면이 한 공간으로 읽힌다.
    void StageZoom(Sequence _seq, RectTransform _matchRoot, RectTransform _deckRoot)
    {
        if (_matchRoot != null)
        {
            _matchRoot.DOKill();
            _matchRoot.localScale = Vector3.one;

            _seq.Insert(0f, _matchRoot.DOScale(this.matchEndScale, this.zoomDuration).SetEase(Ease.InQuad));
        }

        if (_deckRoot == null) return;

        _deckRoot.DOKill();
        _deckRoot.localScale = Vector3.one * this.deckStartScale;

        _seq.Insert(0f, _deckRoot.DOScale(1f, this.zoomDuration).SetEase(Ease.OutQuad));
    }

    // 같은 자리의 두 VS를 교대시킨다. 매칭 쪽은 줄며 흐려지고 덱 쪽은 커지며 나타난다 —
    // 반대 방향이라 모양 차이가 '교대'로 읽히고, 그냥 끄면 그 차이가 그대로 스냅이 된다.
    //
    // ⚠ 덱 쪽 배지(VsBadge)는 Content의 VerticalLayoutGroup 아이템이다 — 좌표나 sizeDelta를 밀면
    //   다음 리빌드에 통째로 되돌아간다. 배율과 알파만 건드리는 이유가 이것이다.
    void StageVersusHandover(Sequence _seq, RectTransform _versus, RectTransform _seat)
    {
        if (_versus != null)
        {
            var t_group = ResolveGroup(_versus);

            t_group.DOKill();
            t_group.alpha = 1f;

            _seq.Insert(this.vsHandoverAt, t_group.DOFade(0f, this.vsHandoverFade).SetEase(Ease.InQuad));
            _seq.Insert(this.vsHandoverAt, _versus.DOScale(0.92f, this.vsHandoverFade).SetEase(Ease.InQuad));
        }

        if (_seat == null) return;

        var t_seatGroup = ResolveGroup(_seat);

        // 덱 화면의 배지는 등장 안무(MatchDeckIntroFx)가 손대지 않는다 — 교대 시각을 아는 것은 이쪽뿐이라
        // 감추는 일도 여기서 한다. 전환을 타지 않는 길에서는 애초에 이 코드가 돌지 않는다.
        t_seatGroup.DOKill();
        t_seatGroup.alpha = 0f;

        _seat.DOKill();
        _seat.localScale = Vector3.one * 1.08f;

        _seq.Insert(this.vsHandoverAt, t_seatGroup.DOFade(1f, this.vsHandoverFade).SetEase(Ease.OutQuad));
        _seq.Insert(this.vsHandoverAt, _seat.DOScale(1f, this.vsHandoverFade).SetEase(Ease.OutQuad));
    }

    // VS 뿌리에서 위/아래로 뻗는 빛줄기 두 줄. 배너가 밀려날 길을 먼저 그어 준다 —
    // 이게 없으면 배너가 그냥 날아간 것이지 무언가에 밀린 것이 아니다.
    // 스캔 띠·조임 빛과 같은 자가설치 규약이다(프리팹에 배선할 자리를 만들지 않는다).
    void StageRays(Sequence _seq, RectTransform _versus, RectTransform _root, Sprite _sprite, Graphic _dim)
    {
        if (_versus == null || _root == null || _sprite == null || this.rayLength <= 0f) return;

        Vector2 t_at = _versus.anchoredPosition;

        this.StageRay(_seq, _root, _sprite, t_at, 0f,   _dim);
        this.StageRay(_seq, _root, _sprite, t_at, 180f, _dim);
    }

    void StageRay(Sequence _seq, RectTransform _root, Sprite _sprite, Vector2 _at, float _angle, Graphic _dim)
    {
        var t_go = new GameObject("HandoffRay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rt = (RectTransform)t_go.transform;

        t_rt.SetParent(_root, false);

        // 피벗이 뿌리(0.5, 0)여야 VS 중심에서 바깥으로 뻗는다 — 중심 피벗이면 밝은 한복판이 VS에 가려 꼬리만 남는다.
        // 앵커는 VS와 같은 중앙이라 저작 좌표를 그대로 옮겨 쓸 수 있다.
        t_rt.anchorMin        = t_rt.anchorMax = new Vector2(0.5f, 0.5f);
        t_rt.pivot            = new Vector2(0.5f, 0f);
        t_rt.anchoredPosition = _at;
        t_rt.sizeDelta        = new Vector2(this.rayLength * this.rayWidthRatio, this.rayLength);
        t_rt.localRotation    = Quaternion.Euler(0f, 0f, _angle);
        t_rt.localScale       = new Vector3(1f, 0.05f, 1f);

        // 배너보다 뒤, 딤보다는 앞. 맨 앞 형제로 보내면 딤(검정 89%) 뒤로 들어가 갈라짐의 원인이 안 보인다 —
        // 이 화면의 딤은 0번 자식이라 "뒤로 보낸다"가 곧 "지운다"가 된다.
        MatchmakingFx.PlaceJustAboveDim(t_rt, _dim);

        var t_image = t_go.GetComponent<Image>();
        t_image.sprite         = _sprite;
        t_image.raycastTarget  = false;
        t_image.preserveAspect = false;
        t_image.color          = new Color(this.rayColor.r, this.rayColor.g, this.rayColor.b, 0f);

        UiAdditive.Apply(t_go);

        // 뻗는 것이 밝기보다 오래간다 — 알파가 먼저 죽으면 '뻗었다'가 아니라 '깜빡였다'가 된다.
        _seq.Insert(0f, t_rt.DOScaleY(1f, this.rayRise).SetEase(Ease.OutQuad));
        _seq.Insert(0f, t_image.DOFade(this.rayAlpha, this.rayRise * 0.6f).SetEase(Ease.OutQuad));

        // 뻗은 뒤엔 얇아지며 꺼진다. 폭이 남은 채 흐려지면 판이 흐려진 것으로 보인다.
        _seq.Insert(this.rayRise, t_rt.DOScaleX(0.3f, this.rayFall).SetEase(Ease.InQuad));
        _seq.Insert(this.rayRise, t_image.DOFade(0f, this.rayFall).SetEase(Ease.InQuad));

        // 잔해를 남기지 않는다 — 다음 매칭이 알파 0짜리 줄기를 물려받으면 두 벌이 겹친다.
        _seq.InsertCallback(this.rayRise + this.rayFall,
                            () => { if (t_go != null) UnityEngine.Object.Destroy(t_go); });
    }

    // 저작에 없어도 되게 런타임에 붙인다 — 제목·취소·VS마다 하나씩 꽂게 하면 배선만 늘어난다.
    static CanvasGroup ResolveGroup(Component _target)
    {
        var t_group = _target.GetComponent<CanvasGroup>();

        return t_group != null ? t_group : _target.gameObject.AddComponent<CanvasGroup>();
    }
}
