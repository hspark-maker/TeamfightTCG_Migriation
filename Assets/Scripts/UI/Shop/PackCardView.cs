using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// 개봉으로 뽑힌 카드 한 장의 표시.
// 카드 비주얼 자체(아트/이름/HP/키워드/시너지)는 도감 타일과 동일하게 CardVisualView에 위임한다 —
// 뽑은 카드가 도감에서 보던 그 카드와 다르게 생기면 안 된다.
// 이 클래스가 따로 존재하는 이유는 강조 축이 다르기 때문 — 개봉 카드는 항상 소유라 잠금 표현이 없고,
// 대신 이 화면에만 있는 축을 얹는다: 한 장이 드러날 때마다 오는 타격(펀치·플래시)과, 신규/중복 구분(NEW 리본·환급 숫자).
// 순수 표시 뷰다. 더미 배치·스와이프 이동은 PackCardStack이 이 오브젝트의 RectTransform을 직접 다룬다.
public class PackCardView : MonoBehaviour
{
    [Header("카드")]
    [SerializeField] CardVisualView cardVisual;   // 카드 비주얼 단일 진실원

    // 카드가 드러날 때마다 오는 타격. 신규든 중복이든 "한 장 확인했다"의 감각은 같아야 하므로 전 카드 공통이다.
    [Header("등장 임팩트")]
    [Tooltip("드러나는 순간 즉시 이만큼 커진 뒤 제자리로 꽂힌다(배율 증가분). 서서히 커지면 타격이 아니라 숨쉬기가 된다.")]
    [SerializeField] float punchOvershoot = 0.2f;
    [Tooltip("가로로 더 벌리고 세로로 그만큼 눌러 준다(배율). 균일 확대는 \"커졌다\"로, 어긋난 확대는 \"맞았다\"로 읽힌다.")]
    [SerializeField] float punchStretch = 0.06f;
    [Tooltip("제자리로 회수하는 시간. 짧을수록 단단하다.")]
    [SerializeField] float punchDuration = 0.22f;

    // 아래 넷은 원래 신규 전용 광채(glow*)였다. 필드 이름이 곧 직렬화 키라, 개명하면서 [FormerlySerializedAs]로
    // 프리팹 배선을 승계시킨다 — 그러지 않으면 참조가 조용히 None이 되고 섬광만 사라진다.
    [Tooltip("드러나는 순간의 섬광. 향후 에셋 교체 지점 — 이 참조만 갈아끼우면 된다.")]
    [FormerlySerializedAs("glow")] [SerializeField] Graphic revealFlash;
    [FormerlySerializedAs("glowPeakAlpha")] [SerializeField] float flashPeakAlpha = 1f;
    [FormerlySerializedAs("glowRiseDuration")] [SerializeField] float flashRiseDuration = 0.18f;
    [FormerlySerializedAs("glowFallDuration")] [SerializeField] float flashFallDuration = 0.45f;

    [Header("신규 강조")]
    [SerializeField] GameObject newBadge;     // NEW 리본(신규일 때만)

    [Header("중복 환급")]
    [SerializeField] GameObject refundBadge;  // "+10" 묶음(중복이고 환급이 있을 때만)
    [SerializeField] TMP_Text refundText;
    [SerializeField] float refundRiseDistance = 40f;
    [SerializeField] float refundDuration = 0.6f;

    public bool IsNew { get; private set; }
    public long Refund { get; private set; }

    // 카드 한 장 통째로 페이드하는 손잡이(밀어내기가 쓴다). 프리팹에 없으면 인스턴스에 붙여준다 —
    // 이 값이 알파를 쥐는 단일 지점이라, 프리팹에 컴포넌트가 있든 없든 페이드는 항상 성립한다.
    CanvasGroup m_group;
    public CanvasGroup Group
    {
        get
        {
            if (m_group == null) m_group = GetComponent<CanvasGroup>();
            if (m_group == null) m_group = gameObject.AddComponent<CanvasGroup>();
            return m_group;
        }
    }

    // 강조가 이미 재생됐는지. 스킵과 정상 진행이 겹쳐도 두 번 터지지 않게 한다.
    bool m_accented;

    // refundBadge의 원위치. 떠오른 뒤 되돌릴 기준이라 최초 1회만 캡처한다.
    Vector3 m_refundHome;
    bool m_refundHomeCaptured;

    // 펀치를 걸기 전의 배율. 도중에 끊겼을 때 돌아갈 자리다(SnapPunchToRest).
    Vector3 m_punchRestScale = Vector3.one;

    /// <summary>카드 데이터·신규여부·환급액을 태운다. 강조는 아직 재생하지 않는다(완전히 드러난 뒤가 발화 시점).</summary>
    public void Bind(DrawnCard _drawn)
    {
        IsNew = _drawn.IsNew;
        Refund = _drawn.Refund;
        m_accented = false;

        // 개봉 카드는 항상 소유(위 헤더 주석) → _owned는 true 고정.
        if (cardVisual != null) cardVisual.Bind(_drawn.Card, true);

        ResetAccent();
    }

    /// <summary>
    /// 카드가 완전히 드러난 순간의 강조. 펀치·플래시는 전 카드 공통이고, 그 위에 신규는 NEW 리본,
    /// 중복은 환급 숫자가 얹힌다.
    /// _instant면 트윈 없이 최종 상태만 — 스킵으로 건너뛴 카드도 결과 표시는 남는다.
    /// </summary>
    public void PlayRevealAccent(bool _instant = false)
    {
        if (m_accented) return;
        m_accented = true;

        // 타격은 "지금 이 한 장을 확인했다"는 순간의 표현이라 즉시 모드엔 없다 —
        // 결과 격자(PackResultGrid)가 이 경로로 카드를 세우는데, 거기서 펀치가 걸리면 격자 배율과 스케일을 두고 다툰다.
        if (!_instant)
        {
            PlayPunch();
            PlayFlash();
        }

        if (IsNew) PlayNewBadge(_instant);
        else PlayRefundAccent(_instant);
    }

    // 강조 요소를 내린 초기 상태. 재사용(풀링 없이 Instantiate이지만 Bind 재호출 대비)에도 안전하게.
    // 펀치는 여기서 되돌리지 않는다 — 그 축은 이 트랜스폼이고, 트랜스폼은 PackCardStack이 쥐고 있다.
    // 걷어낼 시점을 아는 쪽이 그쪽이라 취소도 그쪽이 SnapPunchToRest로 부른다.
    void ResetAccent()
    {
        Group.alpha = 1f;   // 더미에서든 결과 격자에서든 카드는 선명한 상태로 시작한다.

        if (newBadge != null) newBadge.SetActive(false);

        if (revealFlash != null)
        {
            revealFlash.DOKill();
            SetAlpha(revealFlash, 0f);
            revealFlash.gameObject.SetActive(false);
        }

        if (refundBadge != null)
        {
            refundBadge.transform.DOKill();
            if (m_refundHomeCaptured) refundBadge.transform.localPosition = m_refundHome;
            refundBadge.SetActive(false);
        }
    }

    // 카드가 통째로 톡 커졌다 돌아온다. 이 트랜스폼의 위치·회전은 PackCardStack이 쥐고 있으므로
    // 여기서는 스케일만 건드리고 DOKill도 부르지 않는다 — 걷어내면 같은 트랜스폼에 걸린 부유(위치)까지 죽는다.
    // 카드 한 장의 강조는 m_accented 가드로 생애 1회라 중복 펀치도 생기지 않는다.
    void PlayPunch()
    {
        if (punchDuration <= 0f) return;

        // 출발 배율을 적어 둔다 — 회수 목표이자, 도중에 끊겼을 때 돌아갈 자리다.
        m_punchRestScale = transform.localScale;

        // 충격은 t=0에 전부 들어간다. 커지는 구간을 트윈에 맡기면(DOPunchScale이 그랬다) 그 시간만큼 타격이 뭉개져
        // "톡 부풀었다"로 읽힌다 — 눈이 봐야 하는 것은 부풀어 오르는 과정이 아니라 이미 맞은 뒤의 회복이다.
        transform.localScale = Vector3.Scale(m_punchRestScale, new Vector3(
            1f + punchOvershoot + punchStretch,
            1f + punchOvershoot - punchStretch,
            1f));

        // OutQuint — 첫 프레임이 가장 빠르고 끝에서 단단히 선다.
        // OutBack·OutElastic은 제자리를 지나쳐 흔들려 다시 말랑해지므로 쓰지 않는다.
        transform.DOScale(m_punchRestScale, punchDuration)
                 .SetEase(Ease.OutQuint)
                 .SetLink(gameObject);
    }

    /// <summary>재생 중이던 펀치를 끊은 뒤 배율을 제자리로 돌린다.
    /// 트윈을 걷는 것은 이 트랜스폼을 공유하는 PackCardStack의 몫이고(타깃 단위 DOKill이라 부유와 함께 걷힌다),
    /// "쉬는 배율이 얼마인가"는 펀치를 건 이쪽만 안다 — 그 한 조각만 여기 남긴다.</summary>
    public void SnapPunchToRest() => transform.localScale = m_punchRestScale;

    // 섬광이 한 번 퍼졌다 잦아든다.
    void PlayFlash()
    {
        if (revealFlash == null) return;

        revealFlash.DOKill();
        revealFlash.gameObject.SetActive(true);
        SetAlpha(revealFlash, 0f);

        DOTween.Sequence()
            .SetLink(revealFlash.gameObject)
            .Append(revealFlash.DOFade(flashPeakAlpha, flashRiseDuration))
            .Append(revealFlash.DOFade(0f, flashFallDuration))
            .OnComplete(() => { if (revealFlash != null) revealFlash.gameObject.SetActive(false); });
    }

    // 신규: NEW 배지가 튀어나온다. 섬광은 전 카드 공통(PlayFlash)이므로 여기 남은 것은 배지뿐이다 —
    // 신규를 가리는 신호는 배지 하나로 충분하고, 섬광까지 신규 전용으로 두면 중복 카드의 확인 순간이 밋밋해진다.
    void PlayNewBadge(bool _instant)
    {
        if (newBadge == null) return;

        newBadge.SetActive(true);

        if (_instant)
        {
            newBadge.transform.localScale = Vector3.one;
            return;
        }

        newBadge.transform.DOKill();
        newBadge.transform.localScale = Vector3.zero;
        newBadge.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetLink(newBadge);
    }

    // 중복: 환급 숫자가 떠오르며 사라진다. 환급이 0이면 아무 말도 하지 않는다(조용한 정산).
    void PlayRefundAccent(bool _instant)
    {
        if (refundBadge == null || Refund <= 0) return;

        if (refundText != null) refundText.text = $"+{Refund:N0}";

        var t_tr = refundBadge.transform;
        if (!m_refundHomeCaptured)
        {
            m_refundHome = t_tr.localPosition;
            m_refundHomeCaptured = true;
        }

        if (_instant)
        {
            // 스킵 시엔 제자리에 띄워두기만 — 떠오르는 도중 스킵돼도 숫자가 남는다.
            t_tr.DOKill();
            t_tr.localPosition = m_refundHome;
            refundBadge.SetActive(true);
            return;
        }

        t_tr.DOKill();
        t_tr.localPosition = m_refundHome;
        refundBadge.SetActive(true);
        t_tr.DOLocalMoveY(m_refundHome.y + refundRiseDistance, refundDuration)
            .SetEase(Ease.OutCubic)
            .SetLink(refundBadge);
    }

    static void SetAlpha(Graphic _graphic, float _alpha)
    {
        var t_c = _graphic.color;
        t_c.a = _alpha;
        _graphic.color = t_c;
    }
}
