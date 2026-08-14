using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 덱 화면이 서는 안무. 매칭에서 넘어올 때만 쓰인다 — 직접 열 때(디버그·튜토리얼)는 지금처럼 그냥 뜬다.
//
// 대치가 덱으로 번지는 그림이라, 칸은 가운데 VS에서 바깥으로 퍼진다. 순서가 뒤집히면 두 화면이
// 한 사건으로 읽히지 않고 그냥 다음 화면이 된다.
//
// 퍼지는 것은 순서만이 아니다 — 칸은 VS 쪽에 붙어 있다가 밀려나 제자리에 꽂히고, 인포바는
// 배너가 밀려 나간 그 방향에서 되돌아 들어온다. 앞 화면의 갈라짐(MatchHandoffFx)이 만든 결을
// 이 화면이 그대로 이어받는 것이라, 두 클래스의 방향 규약이 어긋나면 사건이 둘로 쪼개진다.
//
// VsBadge는 여기서 건드리지 않는다 — 매칭 VS와 교대하는 시각을 아는 것은 전환뿐이라 그쪽이 전담한다.
//
// ⚠ 오브젝트 참조를 뺀 모든 필드에 C# 이니셜라이저로 기본값을 준다(ScreenFlashCover와 같은 규약).
[Serializable]
public class MatchDeckIntroFx
{
    [Header("자리")]
    [Tooltip("상대 프로필이 수렴할 자리. 매칭 화면이 이 좌표로 카드를 옮겨 앉힌다 — 미배선이면 그쪽 카드는 제자리에서 꺼진다.")]
    [SerializeField] RectTransform enemyInfoBar;

    [SerializeField] RectTransform myInfoBar;

    [Tooltip("VS가 수렴할 자리(VsBadge).")]
    [SerializeField] RectTransform vsBadge;

    [Tooltip("하단 3버튼 바. 미배선이면 이 축만 빠진다.")]
    [SerializeField] RectTransform bottomBar;

    [Header("칸")]
    [Tooltip("칸 하나가 꽂히는 시간.")]
    [Min(0.01f)] [SerializeField] float cardDuration = 0.2f;

    [Tooltip("칸끼리 어긋나는 간격(초). 0이면 12칸이 한꺼번에 나타나 '화면이 켜졌다'가 된다.")]
    [Min(0f)] [SerializeField] float cardStagger = 0.03f;

    [Tooltip("칸이 시작하는 배율.")]
    [Min(0f)] [SerializeField] float cardStartScale = 0.6f;

    [Tooltip("칸이 VS 쪽으로 붙어 있다가 밀려나는 거리(px). 갈라짐이 만든 결을 이어받는 축이라 " +
             "0이면 칸이 제자리에서 커질 뿐이고, 앞 화면과의 인과가 끊긴다.\n" +
             "좌표를 밀어도 되는 이유: 레이아웃 그룹이 쥔 것은 칸(MySlot_N)이고 여기서 미는 것은 그 자식(CardUIView)이다.")]
    [Min(0f)] [SerializeField] float cardPush = 34f;

    [Header("인포바")]
    [Tooltip("인포바가 올라오는 시각(초). 매칭 배너가 밀려 나가는 구간과 겹쳐야 '자리를 물려받았다'로 읽힌다.")]
    [Min(0f)] [SerializeField] float infoAt = 0.02f;

    [Min(0.01f)] [SerializeField] float infoDuration = 0.16f;

    [Tooltip("인포바가 밀려 들어오는 거리(px). 배너가 나간 그 방향에서 되돌아와야 두 화면이 이어진다 — " +
             "0이면 페이드만 남아 '다른 것이 켜졌다'가 된다.")]
    [Min(0f)] [SerializeField] float infoSlide = 64f;

    [Tooltip("덱 파워가 0에서 실제값까지 올라가는 시간. 0이면 처음부터 확정값이 찍힌다.")]
    [Min(0f)] [SerializeField] float powerCountDuration = 0.3f;

    [Header("하단 바")]
    [Min(0f)] [SerializeField] float bottomAt = 0.3f;
    [Min(0.01f)] [SerializeField] float bottomDuration = 0.18f;

    [Tooltip("아래에서 밀려 올라오는 거리(px). 화면 크기에서 계산하지 않는다 — 첫 프레임엔 rect가 0이다.")]
    [SerializeField] float bottomRise = 120f;

    [Tooltip("마지막에 전투 버튼이 한 번 튀는 정도. 시선이 어디에 착지해야 하는지를 이 한 번이 정한다.")]
    [Min(0f)] [SerializeField] float battlePulse = 0.16f;

    // 저작 위치는 한 번만 잡는다 — 이미 밀린 값을 다시 캡처하면 열 때마다 바가 아래로 내려앉는다.
    // 아래 인포바·칸의 홈도 전부 같은 이유의 1회 캡처다.
    Vector2 m_bottomHome;
    bool    m_homeCaptured;

    Vector2 m_enemyInfoHome;
    bool    m_enemyInfoCaptured;
    Vector2 m_myInfoHome;
    bool    m_myInfoCaptured;

    // 칸의 홈은 두 벌(상대/나)을 따로 든다. 배열 하나로 합치면 인덱스가 어긋날 때 조용히 틀어진다.
    Vector2[] m_enemySlotHomes;
    Vector2[] m_mySlotHomes;

    CanvasGroup m_enemyInfoGroup;
    CanvasGroup m_myInfoGroup;
    CanvasGroup m_bottomGroup;

    /// <summary>매칭 VS가 교대할 자리. 감추고 띄우는 일은 전환(MatchHandoffFx)이 한다 — 교대 시각을 아는 것은 그쪽뿐이다.</summary>
    public RectTransform VersusSeat => this.vsBadge;

    /// <summary>
    /// 등장 안무를 만들어 돌려준다(재생은 호출자). 감추는 일은 이 안에서 <b>즉시</b> 끝난다 —
    /// 시퀀스가 첫 프레임에 재생되지 않아도 완성된 덱 화면이 한 프레임 비치면 안 된다.
    /// </summary>
    public Sequence BuildIntro(CardVisualView[] _enemySlots, CardVisualView[] _mySlots,
                               TMP_Text _enemyPower, int _enemyPowerValue,
                               TMP_Text _myPower,    int _myPowerValue,
                               Button _battleButton)
    {
        this.CaptureHome();

        var t_seq = DOTween.Sequence();

        // 상대는 위, 나는 아래 — 양쪽 모두 VS에 가까운 줄(뒤쪽 3칸 / 앞쪽 3칸)부터 꽂힌다.
        // 밀려나는 방향도 같은 축이다(위 칸은 위로, 아래 칸은 아래로) — 부호를 뒤집으면 두 줄이 서로를 향해 모인다.
        this.StageSlots(t_seq, _enemySlots, _nearFirst: false, _push: +1f, _homes: ref this.m_enemySlotHomes);
        this.StageSlots(t_seq, _mySlots,    _nearFirst: true,  _push: -1f, _homes: ref this.m_mySlotHomes);

        // 배너가 나간 그 방향에서 되돌아 들어온다 — 상대 배너는 위로 나갔으니 상대 인포바도 위에서 온다.
        this.m_enemyInfoGroup = this.StageBar(t_seq, this.enemyInfoBar, this.m_enemyInfoGroup,
                                              ref this.m_enemyInfoHome, ref this.m_enemyInfoCaptured, +1f);
        this.m_myInfoGroup    = this.StageBar(t_seq, this.myInfoBar, this.m_myInfoGroup,
                                              ref this.m_myInfoHome, ref this.m_myInfoCaptured, -1f);

        this.StagePower(t_seq, _enemyPower, _enemyPowerValue);
        this.StagePower(t_seq, _myPower,    _myPowerValue);

        this.StageBottom(t_seq, _battleButton);

        return t_seq;
    }

    /// <summary>안무가 세운 중간값을 저작 상태로 되돌린다. 잘려도 화면이 반쯤 없는 채로 굳지 않게.</summary>
    public void Reset(CardVisualView[] _enemySlots, CardVisualView[] _mySlots)
    {
        RestoreSlots(_enemySlots, this.m_enemySlotHomes);
        RestoreSlots(_mySlots,    this.m_mySlotHomes);

        RestoreGroup(this.m_enemyInfoGroup);
        RestoreGroup(this.m_myInfoGroup);
        RestoreGroup(this.m_bottomGroup);

        RestoreBar(this.enemyInfoBar, this.m_enemyInfoHome, this.m_enemyInfoCaptured);
        RestoreBar(this.myInfoBar,    this.m_myInfoHome,    this.m_myInfoCaptured);
        RestoreBar(this.bottomBar,    this.m_bottomHome,    this.m_homeCaptured);

        // 배지를 감춘 것은 전환이지만 되돌리는 일은 여기서 한다 — 전환을 타지 않는 길(디버그·튜토리얼)은
        // 그쪽 코드가 아예 돌지 않아, 저쪽에 맡기면 배지가 투명한 채로 열리는 화면이 생긴다.
        if (this.vsBadge == null) return;

        this.vsBadge.DOKill();
        this.vsBadge.localScale = Vector3.one;

        RestoreGroup(this.vsBadge.GetComponent<CanvasGroup>());
    }

    // 칸은 배율과 좌표를 함께 민다. 좌표를 밀어도 되는 이유는 레이아웃 그룹이 쥔 것이 칸(MySlot_N)이고
    // 여기서 잡는 것은 그 자식(MySlot_N/CardUIView)이기 때문이다 — 그룹은 직계 자식만 자리를 정한다.
    void StageSlots(Sequence _seq, CardVisualView[] _slots, bool _nearFirst, float _push, ref Vector2[] _homes)
    {
        if (_slots == null) return;

        CaptureSlotHomes(_slots, ref _homes);

        int t_order = 0;

        for (int t_i = 0; t_i < _slots.Length; t_i++)
        {
            // 빈 칸은 CardVisualView가 스스로 꺼 둔다 — 꺼진 칸을 안무하면 안 보이는 자리에 간격만 생긴다.
            int t_index = _nearFirst ? t_i : ReverseRow(t_i, _slots.Length);

            var t_slot = _slots[t_index];
            if (t_slot == null || !t_slot.gameObject.activeSelf) continue;

            var t_rt   = (RectTransform)t_slot.transform;
            var t_home = _homes[t_index];

            t_rt.DOKill();
            t_rt.localScale = Vector3.one * this.cardStartScale;

            // VS 쪽(안쪽)에 붙어 있다가 바깥으로 밀려나 제자리에 꽂힌다 — 출발점이 홈의 반대편(-_push)이다.
            t_rt.anchoredPosition = t_home - new Vector2(0f, _push * this.cardPush);

            var t_group = ResolveGroup(t_rt);
            t_group.DOKill();
            t_group.alpha = 0f;

            float t_at = t_order * this.cardStagger;
            t_order++;

            _seq.Insert(t_at, t_rt.DOScale(1f, this.cardDuration).SetEase(Ease.OutBack));
            _seq.Insert(t_at, t_rt.DOAnchorPos(t_home, this.cardDuration).SetEase(Ease.OutBack));
            _seq.Insert(t_at, t_group.DOFade(1f, this.cardDuration * 0.6f).SetEase(Ease.OutQuad));
        }
    }

    // 칸의 저작 좌표를 한 번만 잡는다. 이미 밀린 값을 다시 캡처하면 열 때마다 칸이 조금씩 걸어 나간다.
    static void CaptureSlotHomes(CardVisualView[] _slots, ref Vector2[] _homes)
    {
        if (_homes != null && _homes.Length == _slots.Length) return;

        _homes = new Vector2[_slots.Length];

        for (int t_i = 0; t_i < _slots.Length; t_i++)
            if (_slots[t_i] != null) _homes[t_i] = ((RectTransform)_slots[t_i].transform).anchoredPosition;
    }

    // 6칸을 두 줄로 보고 줄 순서만 뒤집는다(3,4,5,0,1,2). 상대는 위쪽이라 VS에 가까운 줄이 뒤쪽 3칸이다.
    static int ReverseRow(int _i, int _length)
    {
        int t_half = _length / 2;
        if (t_half <= 0) return _i;

        return _i < t_half ? _i + t_half : _i - t_half;
    }

    void StagePower(Sequence _seq, TMP_Text _text, int _value)
    {
        if (_text == null) return;

        if (this.powerCountDuration <= 0f)
        {
            _text.text = _value.ToString();
            return;
        }

        _text.text = "0";

        int t_shown = 0;
        _seq.Insert(this.infoAt,
                    DOTween.To(() => t_shown, _v => { t_shown = _v; _text.text = _v.ToString(); },
                               _value, this.powerCountDuration).SetEase(Ease.OutCubic));
    }

    void StageBottom(Sequence _seq, Button _battleButton)
    {
        if (this.bottomBar == null) return;

        this.m_bottomGroup = ResolveGroup(this.bottomBar);
        this.m_bottomGroup.DOKill();
        this.m_bottomGroup.alpha = 0f;

        this.bottomBar.DOKill();
        this.bottomBar.anchoredPosition = this.m_bottomHome - new Vector2(0f, this.bottomRise);

        _seq.Insert(this.bottomAt,
                    this.bottomBar.DOAnchorPos(this.m_bottomHome, this.bottomDuration).SetEase(Ease.OutBack));
        _seq.Insert(this.bottomAt, this.m_bottomGroup.DOFade(1f, this.bottomDuration).SetEase(Ease.OutQuad));

        if (_battleButton == null || this.battlePulse <= 0f) return;

        _seq.InsertCallback(this.bottomAt + this.bottomDuration,
                            () => UiPunch.Play(_battleButton.transform, this.battlePulse, 0.22f));
    }

    // 인포바 하나가 밀려 들어온다. _push는 배너가 나간 방향(위 +1 / 아래 -1)이고, 바는 그 방향에서 되돌아온다.
    CanvasGroup StageBar(Sequence _seq, RectTransform _bar, CanvasGroup _cached,
                         ref Vector2 _home, ref bool _captured, float _push)
    {
        if (_bar == null) return _cached;

        // 저작 위치는 한 번만 잡는다 — 이미 밀린 값을 다시 캡처하면 열 때마다 바가 바깥으로 걸어 나간다.
        if (!_captured)
        {
            _captured = true;
            _home     = _bar.anchoredPosition;
        }

        var t_group = _cached != null ? _cached : ResolveGroup(_bar);

        t_group.DOKill();
        t_group.alpha = 0f;

        _bar.DOKill();
        _bar.anchoredPosition = _home + new Vector2(0f, _push * this.infoSlide);

        _seq.Insert(this.infoAt, _bar.DOAnchorPos(_home, this.infoDuration).SetEase(Ease.OutCubic));
        _seq.Insert(this.infoAt, t_group.DOFade(1f, this.infoDuration).SetEase(Ease.OutQuad));

        return t_group;
    }

    void CaptureHome()
    {
        if (this.m_homeCaptured || this.bottomBar == null) return;

        this.m_homeCaptured = true;
        this.m_bottomHome   = this.bottomBar.anchoredPosition;
    }

    static void RestoreSlots(CardVisualView[] _slots, Vector2[] _homes)
    {
        if (_slots == null) return;

        for (int t_i = 0; t_i < _slots.Length; t_i++)
        {
            if (_slots[t_i] == null) continue;

            var t_rt = (RectTransform)_slots[t_i].transform;
            t_rt.DOKill();
            t_rt.localScale = Vector3.one;

            // 홈은 안무가 한 번이라도 돌아야 잡힌다 — 그 전이면 애초에 밀린 적이 없어 되돌릴 것도 없다.
            if (_homes != null && t_i < _homes.Length) t_rt.anchoredPosition = _homes[t_i];

            RestoreGroup(t_rt.GetComponent<CanvasGroup>());
        }
    }

    static void RestoreBar(RectTransform _bar, Vector2 _home, bool _captured)
    {
        if (_bar == null) return;

        _bar.DOKill();
        if (_captured) _bar.anchoredPosition = _home;
    }

    static void RestoreGroup(CanvasGroup _group)
    {
        if (_group == null) return;

        _group.DOKill();
        _group.alpha = 1f;
    }

    // 저작에 없어도 되게 런타임에 붙인다 — 12칸 + 바 셋에 하나씩 꽂게 하면 배선만 늘어난다.
    static CanvasGroup ResolveGroup(Component _target)
    {
        var t_group = _target.GetComponent<CanvasGroup>();

        return t_group != null ? t_group : _target.gameObject.AddComponent<CanvasGroup>();
    }
}
