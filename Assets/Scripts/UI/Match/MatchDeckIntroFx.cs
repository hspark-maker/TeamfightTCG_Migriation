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

    [Header("인포바")]
    [Tooltip("인포바가 올라오는 시각(초). 매칭 프로필 카드가 흐려지는 구간과 겹쳐야 '교대'로 읽힌다.")]
    [Min(0f)] [SerializeField] float infoAt = 0.02f;

    [Min(0.01f)] [SerializeField] float infoDuration = 0.16f;

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
    Vector2 m_bottomHome;
    bool    m_homeCaptured;

    CanvasGroup m_enemyInfoGroup;
    CanvasGroup m_myInfoGroup;
    CanvasGroup m_bottomGroup;

    public RectTransform EnemySeat  => this.enemyInfoBar;
    public RectTransform MySeat     => this.myInfoBar;
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
        this.StageSlots(t_seq, _enemySlots, _nearFirst: false);
        this.StageSlots(t_seq, _mySlots,    _nearFirst: true);

        this.m_enemyInfoGroup = StageBar(t_seq, this.enemyInfoBar, this.m_enemyInfoGroup,
                                         this.infoAt, this.infoDuration);
        this.m_myInfoGroup    = StageBar(t_seq, this.myInfoBar, this.m_myInfoGroup,
                                         this.infoAt, this.infoDuration);

        this.StagePower(t_seq, _enemyPower, _enemyPowerValue);
        this.StagePower(t_seq, _myPower,    _myPowerValue);

        this.StageBottom(t_seq, _battleButton);

        return t_seq;
    }

    /// <summary>안무가 세운 중간값을 저작 상태로 되돌린다. 잘려도 화면이 반쯤 없는 채로 굳지 않게.</summary>
    public void Reset(CardVisualView[] _enemySlots, CardVisualView[] _mySlots)
    {
        RestoreSlots(_enemySlots);
        RestoreSlots(_mySlots);

        RestoreGroup(this.m_enemyInfoGroup);
        RestoreGroup(this.m_myInfoGroup);
        RestoreGroup(this.m_bottomGroup);

        if (this.bottomBar == null) return;

        this.bottomBar.DOKill();
        if (this.m_homeCaptured) this.bottomBar.anchoredPosition = this.m_bottomHome;
    }

    // 칸은 배율만 건드린다 — 좌표는 레이아웃 그룹이 쥐고 있어 밀어 봐야 다음 리빌드에 되돌아간다.
    void StageSlots(Sequence _seq, CardVisualView[] _slots, bool _nearFirst)
    {
        if (_slots == null) return;

        int t_order = 0;

        for (int t_i = 0; t_i < _slots.Length; t_i++)
        {
            // 빈 칸은 CardVisualView가 스스로 꺼 둔다 — 꺼진 칸을 안무하면 안 보이는 자리에 간격만 생긴다.
            int t_index = _nearFirst ? t_i : ReverseRow(t_i, _slots.Length);

            var t_slot = _slots[t_index];
            if (t_slot == null || !t_slot.gameObject.activeSelf) continue;

            var t_tr = t_slot.transform;

            t_tr.DOKill();
            t_tr.localScale = Vector3.one * this.cardStartScale;

            var t_group = ResolveGroup(t_tr);
            t_group.DOKill();
            t_group.alpha = 0f;

            float t_at = t_order * this.cardStagger;
            t_order++;

            _seq.Insert(t_at, t_tr.DOScale(1f, this.cardDuration).SetEase(Ease.OutBack));
            _seq.Insert(t_at, t_group.DOFade(1f, this.cardDuration * 0.6f).SetEase(Ease.OutQuad));
        }
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

    static CanvasGroup StageBar(Sequence _seq, RectTransform _bar, CanvasGroup _cached, float _at, float _duration)
    {
        if (_bar == null) return _cached;

        var t_group = _cached != null ? _cached : ResolveGroup(_bar);

        t_group.DOKill();
        t_group.alpha = 0f;
        _seq.Insert(_at, t_group.DOFade(1f, _duration).SetEase(Ease.OutQuad));

        return t_group;
    }

    void CaptureHome()
    {
        if (this.m_homeCaptured || this.bottomBar == null) return;

        this.m_homeCaptured = true;
        this.m_bottomHome   = this.bottomBar.anchoredPosition;
    }

    static void RestoreSlots(CardVisualView[] _slots)
    {
        if (_slots == null) return;

        for (int t_i = 0; t_i < _slots.Length; t_i++)
        {
            if (_slots[t_i] == null) continue;

            var t_tr = _slots[t_i].transform;
            t_tr.DOKill();
            t_tr.localScale = Vector3.one;

            RestoreGroup(t_tr.GetComponent<CanvasGroup>());
        }
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
