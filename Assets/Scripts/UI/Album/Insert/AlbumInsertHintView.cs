using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 삽입 세션 전용 안내 묶음 — 아래로 미는 손가락 + 하단 문구 + 건너뛰기.
//
// ⚠ 값(하단 문구 y 계산, 펄스 1.08f / 0.6s)의 원본은 OutgameTutorialGateUI다 —
//   손맛을 바꿀 땐 둘을 같이 본다(한쪽만 만지면 튜토리얼 안내와 삽입 안내가 갈라진다).
//
// 튜토리얼 게이트를 재사용하지 않는 이유: ShowBanner가 진입부에서 진행 중 게이트를 걷어 도감 튜토리얼과
// 싱글턴을 밟고, 그쪽 계약은 "배너 = 문구만"이라 손가락을 켤 수 없다. 연출도 다르다(제자리 펄스 ↔ 미는 이동).
// 게다가 문구·손가락·건너뛰기가 한 패널에 묶여야 세션 종료 시 한 번에 걷힌다.
public class AlbumInsertHintView : MonoBehaviour
{
    /// <summary>건너뛰기를 누른 순간. 세션이 유일한 구독자다.</summary>
    public event Action OnSkip;

    [SerializeField] RectTransform finger;
    [SerializeField] TMP_Text      guideLabel;
    [SerializeField] Button        skipButton;
    [Tooltip("건너뛰기 버튼의 글자. 남은 장수와 단계에 따라 세션이 갈아 끼운다.")]
    [SerializeField] TMP_Text      skipLabel;
    [Tooltip("하단 문구 y 계산 기준. 미배선이면 문구는 프리팹 저작 위치에 그대로 둔다.")]
    [SerializeField] RectTransform canvasRect;

    [Tooltip("건너뛰기가 떠오르는 시간. 탭바가 걷히는 시간과 맞춘다 —\n" +
             "빈자리에 버튼이 즉시 튀어나오면 탭을 누르려던 손가락이 그대로 눌러 버린다.")]
    [SerializeField] float skipFadeDuration = 0.2f;

    [Header("연출 (값 원본 = OutgameTutorialGateUI)")]
    [Tooltip("손가락이 아래로 미는 거리. 튜토리얼의 제자리 펄스와 달리 방향이 있어야 '민다'로 읽힌다.")]
    [SerializeField] float pushDistance = 120f;
    [SerializeField] float pulseScale   = 1.08f;
    [SerializeField] float pulseDuration = 0.6f;
    [Tooltip("문구 전용 모드(타깃 없음)의 하단 여백 — OutgameTutorialGateUI.messageBottom과 같은 값.")]
    [SerializeField] float messageBottom = 220f;

    RectTransform m_rect;
    Vector2       m_fingerHome;   // 손가락 왕복의 기준점. 미는 트윈이 도는 중에 다시 잡으면 자리가 밀린다
    Tween         m_moveTween;
    Tween         m_pulseTween;
    CanvasGroup   m_skipGroup;
    bool          m_skipShown;    // 이미 떠 있는 버튼을 다시 페이드인하면 장마다 깜빡인다

    /// <summary>안내를 켠다. <paramref name="_anchor"/>는 지금 밀어야 할 카드(홀더) rect.
    /// <b>건너뛰기는 건드리지 않는다</b> — 자동 진행 중에는 안내만 꺼지고 버튼은 남아야 한다.</summary>
    public void Show(string _message, RectTransform _anchor)
    {
        this.PlaceMessage(_message);
        this.PlaceFinger(_anchor);
        this.ResumeFinger();
    }

    /// <summary>건너뛰기의 표시 여부와 글자를 정한다. 안내(손가락·문구)와 수명이 갈리므로 따로 둔다.</summary>
    public void SetSkip(bool _visible, string _label)
    {
        if (this.skipButton == null) return;

        if (this.skipLabel != null && !string.IsNullOrEmpty(_label)) this.skipLabel.text = _label;

        if (!_visible)
        {
            this.HideSkip();
            return;
        }

        this.skipButton.gameObject.SetActive(true);

        if (this.m_skipShown) return;   // 이미 떠 있다 — 글자만 갈아 끼운다
        this.m_skipShown = true;

        if (this.m_skipGroup == null) return;

        this.m_skipGroup.DOKill();
        this.m_skipGroup.alpha = 0f;
        this.m_skipGroup.DOFade(1f, Mathf.Max(0.01f, this.skipFadeDuration))
                        .SetEase(Ease.OutQuad)
                        .SetUpdate(true)
                        .SetTarget(this.m_skipGroup)
                        .SetLink(this.skipButton.gameObject);
    }

    /// <summary>안내(손가락·문구)만 걷는다. <b>건너뛰기는 남는다</b> — 걷으려면 SetSkip(false)를 따로 부른다.</summary>
    public void Hide()
    {
        this.StopFinger();

        if (this.finger != null)     this.finger.gameObject.SetActive(false);
        if (this.guideLabel != null) this.guideLabel.gameObject.SetActive(false);
    }

    void HideSkip()
    {
        this.m_skipShown = false;

        if (this.m_skipGroup != null)
        {
            this.m_skipGroup.DOKill();
            this.m_skipGroup.alpha = 1f;   // 다음 등장이 0에서 출발하도록 기준값으로 되돌린다
        }

        if (this.skipButton != null) this.skipButton.gameObject.SetActive(false);
    }

    /// <summary>손이 이미 카드를 잡았으면 손가락은 방해다 — 문구는 남긴다(무엇을 하는 중인지의 설명이라).</summary>
    public void PauseFinger()
    {
        this.StopFinger();
        if (this.finger != null) this.finger.gameObject.SetActive(false);
    }

    public void ResumeFinger()
    {
        if (this.finger == null) return;

        this.finger.gameObject.SetActive(true);
        this.finger.anchoredPosition = this.m_fingerHome;

        if (this.m_moveTween == null)
            this.m_moveTween = this.finger.DOAnchorPosY(this.m_fingerHome.y - this.pushDistance, this.pulseDuration)
                                          .SetEase(Ease.InOutSine)
                                          .SetLoops(-1, LoopType.Yoyo)
                                          .SetLink(this.finger.gameObject);

        if (this.m_pulseTween == null)
            this.m_pulseTween = this.finger.DOScale(this.pulseScale, this.pulseDuration)
                                           .SetEase(Ease.InOutSine)
                                           .SetLoops(-1, LoopType.Yoyo)
                                           .SetLink(this.finger.gameObject);
    }

    void Awake()
    {
        this.m_rect = transform as RectTransform;

        // 런타임 RemoveAllListeners는 퍼시스턴트를 못 지운다 — 목업 onClick은 배선 단계에서 지워야 한다
        if (this.skipButton != null && this.skipButton.onClick.GetPersistentEventCount() > 0)
            Debug.LogWarning("[AlbumInsertHintView] Button_Skip에 목업 퍼시스턴트 onClick이 남아 있다 — 프리팹에서 제거할 것.", this);

        if (this.skipButton != null) this.skipButton.onClick.AddListener(() => OnSkip?.Invoke());

        // 저작에 없어도 연출은 돌아야 한다 — 없으면 페이드 없이 즉시 뜨는 대신 붙여서 쓴다.
        if (this.skipButton != null)
        {
            this.m_skipGroup = this.skipButton.GetComponent<CanvasGroup>();
            if (this.m_skipGroup == null) this.m_skipGroup = this.skipButton.gameObject.AddComponent<CanvasGroup>();
        }

        if (this.finger != null) this.m_fingerHome = this.finger.anchoredPosition;

        this.Hide();
        this.HideSkip();
    }

    void OnDisable()
    {
        // SetLink는 파괴에만 반응하고 비활성화에는 반응하지 않는다 → 직접 죽이고 기준 상태로 되돌린다.
        this.StopFinger();
    }

    void PlaceMessage(string _message)
    {
        if (this.guideLabel == null) return;

        bool t_has = !string.IsNullOrEmpty(_message);
        this.guideLabel.gameObject.SetActive(t_has);
        if (!t_has) return;

        this.guideLabel.text = _message;

        if (this.canvasRect == null) return;

        var t_rect = this.guideLabel.rectTransform;
        t_rect.anchoredPosition =
            new Vector2(0f, this.canvasRect.rect.yMin + t_rect.sizeDelta.y * 0.5f + this.messageBottom);
    }

    // 손가락은 밀어야 할 카드 위에 선다 — 허공을 미는 그림이 되지 않게.
    void PlaceFinger(RectTransform _anchor)
    {
        if (this.finger == null) return;

        if (_anchor != null && this.m_rect != null)
            this.m_fingerHome = UiGainBurst.ToLayerLocal(this.m_rect, _anchor);

        this.finger.anchoredPosition = this.m_fingerHome;
    }

    void StopFinger()
    {
        if (this.m_moveTween != null)
        {
            this.m_moveTween.Kill();
            this.m_moveTween = null;
        }

        if (this.m_pulseTween != null)
        {
            this.m_pulseTween.Kill();
            this.m_pulseTween = null;
        }

        if (this.finger == null) return;

        this.finger.anchoredPosition = this.m_fingerHome;
        this.finger.localScale       = Vector3.one;
    }
}
