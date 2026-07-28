using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 개봉으로 뽑힌 카드 한 장의 표시. 도감 타일(CardVisualView)과 분리한 이유는 요구가 다르기 때문 —
// 개봉 카드는 항상 소유라 잠금 표현이 없고, 대신 신규/중복이라는 이 화면에만 있는 축을 드러낸다.
// 순수 표시 뷰다. 더미 배치·스와이프 이동은 PackCardStack이 이 오브젝트의 RectTransform을 직접 다룬다.
public class PackCardView : MonoBehaviour
{
    [Header("카드")]
    [SerializeField] Image portrait;          // 카드 아트
    [SerializeField] TMP_Text nameText;       // 카드 이름

    [Header("신규 강조")]
    [SerializeField] GameObject newBadge;     // NEW 리본(신규일 때만)
    [SerializeField] Graphic glow;            // 신규 광채. 한 번 퍼졌다 잦아든다.
    [SerializeField] float glowPeakAlpha = 1f;
    [SerializeField] float glowRiseDuration = 0.18f;
    [SerializeField] float glowFallDuration = 0.45f;

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

    /// <summary>카드 데이터·신규여부·환급액을 태운다. 강조는 아직 재생하지 않는다(완전히 드러난 뒤가 발화 시점).</summary>
    public void Bind(DrawnCard _drawn)
    {
        IsNew = _drawn.IsNew;
        Refund = _drawn.Refund;
        m_accented = false;

        var t_card = _drawn.Card;

        if (portrait != null)
        {
            portrait.sprite = t_card != null ? t_card.fullImage : null;
            portrait.enabled = portrait.sprite != null;
        }

        if (nameText != null) nameText.text = t_card != null ? t_card.displayName : string.Empty;

        ResetAccent();
    }

    /// <summary>
    /// 카드가 완전히 드러난 순간의 강조. 신규는 광채+NEW, 중복은 환급 숫자가 떠오른다.
    /// _instant면 트윈 없이 최종 상태만 — 스킵으로 건너뛴 카드도 결과 표시는 남는다.
    /// </summary>
    public void PlayRevealAccent(bool _instant = false)
    {
        if (m_accented) return;
        m_accented = true;

        if (IsNew) PlayNewAccent(_instant);
        else PlayRefundAccent(_instant);
    }

    // 강조 요소를 전부 내린 초기 상태. 재사용(풀링 없이 Instantiate이지만 Bind 재호출 대비)에도 안전하게.
    void ResetAccent()
    {
        Group.alpha = 1f;   // 더미에서든 결과 격자에서든 카드는 선명한 상태로 시작한다.

        if (newBadge != null) newBadge.SetActive(false);

        if (glow != null)
        {
            glow.DOKill();
            SetAlpha(glow, 0f);
            glow.gameObject.SetActive(false);
        }

        if (refundBadge != null)
        {
            refundBadge.transform.DOKill();
            if (m_refundHomeCaptured) refundBadge.transform.localPosition = m_refundHome;
            refundBadge.SetActive(false);
        }
    }

    // 신규: NEW 배지가 튀어나오고 광채가 퍼졌다 잦아든다.
    void PlayNewAccent(bool _instant)
    {
        if (newBadge != null)
        {
            newBadge.SetActive(true);
            if (!_instant)
            {
                newBadge.transform.DOKill();
                newBadge.transform.localScale = Vector3.zero;
                newBadge.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetLink(newBadge);
            }
            else newBadge.transform.localScale = Vector3.one;
        }

        if (glow == null) return;

        glow.DOKill();
        glow.gameObject.SetActive(true);

        if (_instant)
        {
            // 스킵 시엔 광채를 남기지 않는다 — 배지만으로 신규가 읽힌다.
            SetAlpha(glow, 0f);
            glow.gameObject.SetActive(false);
            return;
        }

        SetAlpha(glow, 0f);
        DOTween.Sequence()
            .SetLink(glow.gameObject)
            .Append(glow.DOFade(glowPeakAlpha, glowRiseDuration))
            .Append(glow.DOFade(0f, glowFallDuration))
            .OnComplete(() => { if (glow != null) glow.gameObject.SetActive(false); });
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
