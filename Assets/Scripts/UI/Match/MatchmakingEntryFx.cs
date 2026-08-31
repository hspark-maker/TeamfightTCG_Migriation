using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 로비 → 매칭 화면 진입의 안무. MatchHandoffFx(매칭 → 덱)의 앞자리 짝이다.
//
// 동사는 "켜진다"가 아니라 "덮어 온다"이다. 배경 두 판이 대각으로 맞물려 로비를 덮고(MatchmakingBgFx),
// 그 위로 두 배너가 각자의 바깥에서 — 나중에 갈라짐이 배너를 밀어낼 바로 그 방향에서 — 되돌아 꽂힌다.
// 로비·매칭·덱 세 화면이 상대 위 / VS 가운데 / 나 아래라는 한 축을 공유한다는 주장이 이 방향이다.
// 방향 부호를 뒤집으면 두 배너가 서로를 스쳐 지나가고, 그 순간 축은 사라진다.
//
// ⚠ 덮는 일은 배경 판이 한다. 딤(Dimed)은 이 프리팹에서 꺼져 있다 — BG 두 판이 이미 화면을 채우기 때문이다.
//   그래서 알파를 미는 아래 dimFade 축은 실제로는 아무 일도 하지 않는다(딤을 되살리는 날을 위해 남겨 둔 것).
//   밝기 축은 다르다 — ScreenDimTint가 두 판을 extraDims로 함께 밀어 살아 있다.
//
// 들어오는 것은 감속(OutCubic)이고 갈라짐의 나가는 것은 가속(InCubic)이다 — 앉는 것과 밀리는 것의 차이다.
//
// 스캔 띠를 여기서 켜지 않는 이유: 아직 날아드는 틀 안에서 띠가 돌면 두 움직임이 겹쳐 어느 쪽도 읽히지 않는다.
// 언제 켤지(ScanAt)만 알려주고 켜는 일은 셸이 한다.
//
// ⚠ 오브젝트 참조가 하나도 없다. 전부 C# 이니셜라이저 기본값이라 배선 없이 돈다(MatchHandoffFx와 같은 규약).
[Serializable]
public class MatchmakingEntryFx
{
    [Header("어둠 — 로비를 덮는다")]
    [Tooltip("어둠이 저작 농도까지 차오르는 시간(초). 0이면 로비가 한 프레임에 사라져 " +
             "'화면이 바뀌었다'가 되고, 길면 로비가 미련하게 남는다.")]
    [Min(0.01f)] public float dimFade = 0.18f;

    [Header("다가옴 — 카메라가 들어간다")]
    [Tooltip("이 화면이 들어오기 시작하는 배율. 1보다 커야 '로비 위로 다가온다'가 된다 — " +
             "덱 화면이 들어오는 배율(MatchHandoffFx.deckStartScale)과 같은 규약이라 한쪽만 만지면 두 진입의 결이 갈린다.")]
    [Min(0.1f)] public float rootStartScale = 1.06f;

    [Min(0.01f)] public float rootDuration = 0.26f;

    [Header("배너 — 바깥에서 꽂힌다")]
    [Tooltip("배너가 움직이기 시작하는 시각(초). 배경 두 판이 절반 넘게 맞물린 뒤여야 한다(MatchmakingBgFx.closeDuration=0.22) — " +
             "판이 열려 있는 동안 배너가 보이면 아직 로비인 화면 위에 배너가 떠 있는 꼴이 된다.")]
    [Min(0f)] public float bannerAt = 0.12f;

    [Min(0.01f)] public float bannerDuration = 0.26f;

    [Tooltip("배너가 바깥에서 들어오는 거리(px). 갈라짐이 내보내는 거리(partDistance)만큼 멀 필요는 없다 — " +
             "나갈 때는 프레임을 벗어나야 하지만 들어올 때는 이미 화면 안에서 출발해도 방향이 읽힌다.\n" +
             "0이면 페이드만 남아 '다른 것이 켜졌다'가 된다.")]
    [Min(0f)] public float bannerDistance = 190f;

    [Tooltip("들어오는 길의 몇 지점에서 다 나타나는가(0~1). 갈라짐의 partFadeStart와 반대 규약이다 — " +
             "나갈 때는 끝에서 흐려지고 들어올 때는 앞에서 나타나야, 양쪽 다 '밀려 움직였다'로 읽힌다.")]
    [Range(0.05f, 1f)] public float bannerFadeRatio = 0.55f;

    [Header("따라 들어오는 것들 — 제목·취소")]
    [Tooltip("제목·취소 버튼이 들어오는 시각(초). 배너보다 늦어야 시선이 배너에 먼저 간다.")]
    [Min(0f)] public float riderAt = 0.18f;

    [Min(0.01f)] public float riderDuration = 0.22f;

    [Tooltip("제목·취소가 들어오는 거리(px). 배너보다 짧아야 주역과 곁가지가 구분된다.")]
    [Min(0f)] public float riderDistance = 80f;

    [Header("스캔")]
    [Tooltip("빈 틀을 훑는 띠가 켜지는 시각(초). 배너가 자리를 잡은 뒤여야 한다 — " +
             "날아드는 틀 안에서 띠가 돌면 두 움직임이 겹쳐 어느 쪽도 읽히지 않는다.")]
    [Min(0f)] public float scanAt = 0.34f;

    // 이번 진입이 쓸 방향. Build가 인자로 받아 세우고 각 축이 읽는다 — 축마다 인자로 흘리면 서명만 길어진다.
    Vector2 m_normal = Vector2.up;

    /// <summary>빈 틀 스캔을 켜야 하는 시각. 켜는 일은 셸이 한다 — 무엇을 훑을지는 이 클래스가 모른다.</summary>
    public float ScanAt => this.scanAt;

    /// <summary>진입 안무가 끝나는 시각. 뒤따르는 것을 붙이려는 쪽의 기준이다.</summary>
    public float Duration => Mathf.Max(Mathf.Max(this.dimFade, this.rootDuration),
                                       Mathf.Max(this.bannerAt + this.bannerDuration,
                                                 this.riderAt  + this.riderDuration));

    /// <summary>한 연출 몫의 등장 박자(화면 배율·배너·따라 들어오는 것들). 매칭과 대치가 같은 부품을 쓰고 이 여섯 값만 갈아끼운다.</summary>
    // readonly로 잠그지 못한다 — Unity는 readonly 필드를 직렬화하지 않아 인스펙터로 저작할 수 없다.
    [Serializable]
    public struct EntranceTuning
    {
        [Min(0.01f)] public float rootDuration;
        [Min(0f)]    public float bannerAt;
        [Min(0.01f)] public float bannerDuration;
        [Min(0f)]    public float bannerDistance;
        [Min(0f)]    public float riderAt;
        [Min(0.01f)] public float riderDuration;
    }

    /// <summary>지금 저작된 등장 박자를 한 묶음으로 꺼낸다. 갈아끼우기 전에 한 번만 잡아야 저작값이 진실원으로 남는다.</summary>
    public EntranceTuning CaptureEntrance() => new EntranceTuning
    {
        rootDuration   = this.rootDuration,
        bannerAt       = this.bannerAt,
        bannerDuration = this.bannerDuration,
        bannerDistance = this.bannerDistance,
        riderAt        = this.riderAt,
        riderDuration  = this.riderDuration,
    };

    /// <summary>등장 박자를 통째로 갈아끼운다. 안무를 짓기 전에 불러야 그 판의 값으로 지어진다.</summary>
    public void ApplyEntrance(in EntranceTuning _tuning)
    {
        this.bannerAt       = _tuning.bannerAt;
        this.bannerDistance = _tuning.bannerDistance;
        this.riderAt        = _tuning.riderAt;

        // 길이 0짜리 트윈은 Duration을 앞당겨 착지 시각까지 흔든다 — 저작 하한(Min)을 코드에서도 지킨다.
        this.rootDuration   = Mathf.Max(0.01f, _tuning.rootDuration);
        this.bannerDuration = Mathf.Max(0.01f, _tuning.bannerDuration);
        this.riderDuration  = Mathf.Max(0.01f, _tuning.riderDuration);
    }

    /// <summary>진입 안무를 만들어 돌려준다(재생은 호출자).</summary>
    /// <param name="_riders">배너에 실리지 않은 것들(제목·취소 버튼). 방향은 각자의 y가 VS의 어느 쪽인지가 정한다 —
    /// 갈라짐(MatchHandoffFx.StageRiders)과 같은 규약이라 나간 자리에서 되돌아 들어온다.</param>
    /// <param name="_normal">
    /// 들어오는 방향. 배경 이음매의 법선(MatchmakingBgFx.EnterNormal)을 받으면 배너가 두 판이 맞물리는
    /// 그 대각을 타고 미끄러져, 배너·판·이음매가 한 축으로 정렬된다. Vector2.zero면 수직으로 들어온다.
    /// </param>
    public Sequence Build(MatchProfileView _my, MatchProfileView _opponent, RectTransform _versus,
                          Graphic _dim, RectTransform _root, RectTransform[] _riders, Vector2 _normal)
    {
        var t_seq = DOTween.Sequence();

        // 이 화면의 축이 곧 방향이다. 못 받았으면 수직 — 배경이 미배선이어도 진입은 돌아야 한다.
        this.m_normal = _normal.sqrMagnitude > 0.0001f ? _normal.normalized : Vector2.up;

        // 위/아래를 가르는 기준선은 VS다 — 이 화면에서 VS는 아직 꺼져 있지만 좌표는 읽을 수 있다.
        // 갈라짐이 쓰는 것과 같은 한 줄이라, 나갈 방향과 들어온 방향이 저절로 맞는다.
        float t_axis = _versus != null ? _versus.anchoredPosition.y : 0f;

        this.StageDim(t_seq, _dim);
        this.StageZoom(t_seq, _root);

        // ?. 을 쓰지 않는다 — UnityEngine.Object의 가짜 null은 null 조건 연산자가 걸러 주지 못한다.
        if (_opponent != null)
            this.StageEnter(t_seq, _opponent.Rect, _opponent.Group, t_axis,
                            this.bannerAt, this.bannerDuration, this.bannerDistance);

        if (_my != null)
            this.StageEnter(t_seq, _my.Rect, _my.Group, t_axis,
                            this.bannerAt, this.bannerDuration, this.bannerDistance);

        this.StageRiders(t_seq, _riders, t_axis);

        return t_seq;
    }

    // 어둠이 차오른다. 알파를 미는 축이라 ScreenDimTint(밝기만 미는 축)가 아니라 여기서 직접 한다 —
    // 걷어내는 쪽(MatchHandoffFx)이 알파를 쓰는 것과 같은 이유다.
    // 목표는 지금 칠해진 저작 알파다. 셸이 fx.Reset()으로 저작 색을 되돌린 뒤에 이걸 부르는 것이 전제다.
    void StageDim(Sequence _seq, Graphic _dim)
    {
        if (_dim == null) return;

        _dim.DOKill();

        float t_target = _dim.color.a;

        Color t_from = _dim.color;
        t_from.a     = 0f;
        _dim.color   = t_from;

        _seq.Insert(0f, _dim.DOFade(t_target, this.dimFade).SetEase(Ease.OutQuad));
    }

    // 화면이 로비 위로 다가온다. 덱 화면이 들어오는 축(MatchHandoffFx.StageZoom)과 같은 방향이라
    // 두 진입이 같은 카메라 움직임으로 읽힌다.
    void StageZoom(Sequence _seq, RectTransform _root)
    {
        if (_root == null) return;

        _root.DOKill();
        _root.localScale = Vector3.one * this.rootStartScale;

        _seq.Insert(0f, _root.DOScale(1f, this.rootDuration).SetEase(Ease.OutQuad));
    }

    // 부품 하나가 바깥에서 제자리로 들어온다. 방향은 그 부품이 VS의 어느 쪽에 앉는지가 정한다 —
    // 위에 앉을 것은 위에서, 아래에 앉을 것은 아래에서. 어느 부품이 어디 있는지 이 클래스는 몰라도 된다.
    void StageEnter(Sequence _seq, RectTransform _rect, CanvasGroup _group, float _axisY,
                    float _at, float _duration, float _distance)
    {
        if (_rect == null) return;

        _rect.DOKill();

        // 홈은 지금 자리다 — 셸이 저작 자리로 되돌린(RestoreHome·RestoreRiders) 직후에 부르는 것이 전제다.
        Vector2 t_home = _rect.anchoredPosition;
        float   t_sign = t_home.y >= _axisY ? 1f : -1f;

        // 이음매의 법선을 타고 들어온다 — 배경 두 판이 맞물리는 방향과 같아야 화면이 한 축으로 읽힌다.
        _rect.anchoredPosition = t_home + this.m_normal * (t_sign * _distance);

        // 감속이라 "자리에 앉았다"가 된다. 가속이면 무언가에 밀려 들어온 것으로 보인다.
        _seq.Insert(_at, _rect.DOAnchorPos(t_home, _duration).SetEase(Ease.OutCubic));

        if (_group == null) return;

        _group.DOKill();
        _group.alpha = 0f;

        // 앞에서 다 나타난다 — 끝까지 흐리면 제자리에서 생겨난 것으로 보여 들어온 사실이 지워진다.
        _seq.Insert(_at, _group.DOFade(1f, _duration * this.bannerFadeRatio).SetEase(Ease.OutQuad));
    }

    // 제목·취소 버튼처럼 배너에 실리지 않은 것들. 그냥 켜지면 진입 한복판에서 두 물건이 튀어나온다.
    void StageRiders(Sequence _seq, RectTransform[] _riders, float _axisY)
    {
        if (_riders == null) return;

        for (int t_i = 0; t_i < _riders.Length; t_i++)
        {
            var t_rider = _riders[t_i];
            if (t_rider == null) continue;

            this.StageEnter(_seq, t_rider, ResolveGroup(t_rider), _axisY,
                            this.riderAt, this.riderDuration, this.riderDistance);
        }
    }

    // 저작에 없어도 되게 런타임에 붙인다 — 제목·취소마다 하나씩 꽂게 하면 배선만 늘어난다.
    static CanvasGroup ResolveGroup(Component _target)
    {
        var t_group = _target.GetComponent<CanvasGroup>();

        return t_group != null ? t_group : _target.gameObject.AddComponent<CanvasGroup>();
    }
}
