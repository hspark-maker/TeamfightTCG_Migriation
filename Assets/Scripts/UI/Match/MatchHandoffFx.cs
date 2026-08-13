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
    /// <summary>상대 프로필이 수렴할 자리(EnemyInfoBar).</summary>
    public readonly RectTransform OpponentSeat;

    /// <summary>내 프로필이 수렴할 자리(MyInfoBar).</summary>
    public readonly RectTransform MySeat;

    /// <summary>VS가 수렴할 자리(VsBadge).</summary>
    public readonly RectTransform VersusSeat;

    /// <summary>덱 화면이 스스로 세운 등장 안무. 이미 감춘 상태로 와야 한다 — 여기서 다시 감추지 않는다.</summary>
    public readonly Sequence Intro;

    public MatchHandoffTargets(RectTransform _opponentSeat, RectTransform _mySeat,
                               RectTransform _versusSeat, Sequence _intro)
    {
        this.OpponentSeat = _opponentSeat;
        this.MySeat       = _mySeat;
        this.VersusSeat   = _versusSeat;
        this.Intro        = _intro;
    }
}

// 매칭 화면 → 덱 화면 전환의 안무. 커튼으로 덮지 않는다 —
// 두 화면은 상대 위 / VS 가운데 / 나 아래로 축이 같아서, 가리면 오히려 같은 무대라는 사실이 지워진다.
// 대신 세 부품(내 카드·상대 카드·VS)이 새 화면의 제자리로 옮겨 앉고 그 자리에서 진짜 부품과 교대한다.
//
// ⚠ 오브젝트 참조가 하나도 없다. 전부 C# 이니셜라이저 기본값이라 배선 없이 돈다 —
//   이 클래스를 든 LobbyMatchLauncher는 LobbyCanvas에 있어 프리팹을 저장하면 관계없는 좌표가 함께 커밋된다.
[Serializable]
public class MatchHandoffFx
{
    [Header("이사")]
    [Tooltip("프로필 카드가 덱 화면의 제자리로 옮겨 앉는 시간. 0.35를 넘기면 '이동을 구경하는' 구간이 생긴다.")]
    [Min(0.01f)] public float moveDuration = 0.25f;

    [Tooltip("도착 시점의 배율. 인포바는 카드보다 훨씬 납작해 크기를 맞추려 들면 글자만 남는다 — " +
             "자리만 맞추고 나머지는 교대(페이드)에 맡긴다.")]
    [Min(0.05f)] public float cardEndScale = 0.45f;

    [Tooltip("이동의 몇 지점부터 흐려지기 시작하는가(0~1). 1에 가까울수록 늦게 사라져 두 화면이 겹쳐 보인다.")]
    [Range(0f, 1f)] public float fadeStart = 0.5f;

    [Tooltip("VS가 덱 화면 배지 자리에서 갖는 배율.")]
    [Min(0.05f)] public float versusEndScale = 0.7f;

    [Header("딤")]
    [Tooltip("어둠이 걷히기 시작하는 시각(초). 0이면 카드가 움직이기도 전에 덱 화면이 드러난다.")]
    [Min(0f)] public float dimAt = 0.06f;

    [Tooltip("어둠이 걷히는 시간. 이 구간이 '카메라가 뒤로 빠지는' 체감의 대부분이다.")]
    [Min(0.01f)] public float dimFade = 0.24f;

    [Header("덱 등장")]
    [Tooltip("덱 화면 등장 안무가 시작하는 시각(초). 이사가 끝나기 전에 겹쳐야 두 화면이 한 사건으로 읽힌다.")]
    [Min(0f)] public float introAt = 0.2f;

    /// <summary>
    /// 전환 안무를 만들어 돌려준다(재생은 호출자). 카드는 목적지 좌표로 트윈만 하고 부모를 옮기지 않는다 —
    /// 부모를 갈아타면 덱 화면 레이아웃이 이 카드를 칸으로 세어 실제 화면이 밀린다.
    /// </summary>
    public Sequence Build(MatchProfileView _my, MatchProfileView _opponent, RectTransform _versus,
                          Graphic _dim, in MatchHandoffTargets _targets)
    {
        var t_seq = DOTween.Sequence();

        this.StageCard(t_seq, _my,       _targets.MySeat);
        this.StageCard(t_seq, _opponent, _targets.OpponentSeat);
        this.StageVersus(t_seq, _versus, _targets.VersusSeat);

        // ScreenDimTint는 밝기만 미는 축이라(알파는 저작값 고정) 걷어내는 일은 여기서 직접 한다.
        if (_dim != null)
        {
            _dim.DOKill();
            t_seq.Insert(this.dimAt, _dim.DOFade(0f, this.dimFade).SetEase(Ease.InQuad));
        }

        if (_targets.Intro != null) t_seq.Insert(this.introAt, _targets.Intro);

        return t_seq;
    }

    // 카드 하나를 목적지 자리로 옮겨 앉히고, 도착 즈음 흐려 없앤다. 흐려지는 그 구간에 덱 화면의 진짜 인포바가 올라온다.
    void StageCard(Sequence _seq, MatchProfileView _view, RectTransform _seat)
    {
        if (_view == null || _seat == null) return;

        var t_rect = _view.Rect;

        t_rect.DOKill();

        Vector2 t_to = ToLocal(t_rect, _seat);

        _seq.Insert(0f, t_rect.DOAnchorPos(t_to, this.moveDuration).SetEase(Ease.InOutQuad));
        _seq.Insert(0f, t_rect.DOScale(this.cardEndScale, this.moveDuration).SetEase(Ease.InOutQuad));

        var t_group = _view.Group;
        t_group.DOKill();
        t_group.alpha = 1f;

        float t_fadeAt = this.moveDuration * this.fadeStart;
        _seq.Insert(t_fadeAt, t_group.DOFade(0f, this.moveDuration - t_fadeAt).SetEase(Ease.InQuad));
    }

    void StageVersus(Sequence _seq, RectTransform _versus, RectTransform _seat)
    {
        if (_versus == null || _seat == null) return;

        _versus.DOKill();

        Vector2 t_to = ToLocal(_versus, _seat);

        _seq.Insert(0f, _versus.DOAnchorPos(t_to, this.moveDuration).SetEase(Ease.InOutQuad));
        _seq.Insert(0f, _versus.DOScale(this.versusEndScale, this.moveDuration).SetEase(Ease.InOutQuad));

        // VS는 덱 화면에도 같은 자리에 있다 — 겹친 채로 꺼져야 "그대로 남았다"로 읽힌다.
        float t_fadeAt = this.moveDuration * this.fadeStart;
        _seq.Insert(t_fadeAt, _versus.DOScale(this.versusEndScale * 0.9f, this.moveDuration - t_fadeAt));
        _seq.InsertCallback(this.moveDuration, () => _versus.gameObject.SetActive(false));
    }

    // 다른 계층에 있는 자리를 이 카드의 부모 좌표로 옮긴다. 두 오버레이가 같은 SafeArea 아래 형제라
    // 스케일·회전이 같고, 그래서 위치 변환 하나로 충분하다.
    static Vector2 ToLocal(RectTransform _rect, RectTransform _seat)
    {
        var t_parent = _rect.parent as RectTransform;
        if (t_parent == null) return _rect.anchoredPosition;

        return t_parent.InverseTransformPoint(_seat.position);
    }
}
