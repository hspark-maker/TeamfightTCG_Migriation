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
    [Tooltip("하단 문구 y 계산 기준. 미배선이면 문구는 프리팹 저작 위치에 그대로 둔다.")]
    [SerializeField] RectTransform canvasRect;

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

    /// <summary>안내를 켠다. <paramref name="_anchor"/>는 지금 밀어야 할 카드(홀더) rect.</summary>
    public void Show(string _message, RectTransform _anchor)
    {
        this.PlaceMessage(_message);
        this.PlaceFinger(_anchor);
        this.ResumeFinger();

        if (this.skipButton != null) this.skipButton.gameObject.SetActive(true);
    }

    public void Hide()
    {
        this.StopFinger();

        if (this.finger != null)     this.finger.gameObject.SetActive(false);
        if (this.guideLabel != null) this.guideLabel.gameObject.SetActive(false);
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

        if (this.finger != null) this.m_fingerHome = this.finger.anchoredPosition;

        this.Hide();
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
