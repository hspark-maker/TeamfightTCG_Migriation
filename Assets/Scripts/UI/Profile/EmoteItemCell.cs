using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 장착 슬롯과 목록 셀의 입력·표시. 편집 상태는 ProfileEditPanel이 관리한다.
public class EmoteItemCell : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] Image icon;
    [SerializeField] GameObject equippedMark;
    [SerializeField] GameObject emptyMark;
    [SerializeField] GameObject hoverHighlight;
    [SerializeField] GameObject swapGlow;
    [SerializeField] GameObject swapMark;
    [SerializeField] Button button;
    [SerializeField] CanvasGroup dimGroup;
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.4f;

    int m_key;
    Action<int> m_onClick;
    Action<EmoteItemCell, PointerEventData> m_onDrag;
    LongPressDetector m_longPress;
    PointerEventData m_pointerData;
    bool m_suppressClick;
    bool m_owned;
    bool m_equipped;
    bool m_focusDimmed;
    Tween m_focusTween;
    Tween m_swapTween;
    Tween m_punchTween;
    UiGlowBlink m_swapGlowBlink;

    public int EmoteId { get; private set; }
    public bool IsSlot { get; private set; }
    public RectTransform Rect => (RectTransform)this.transform;
    public Sprite Sprite => this.icon != null ? this.icon.sprite : null;
    public int Key => this.m_key;

    public void BindSlot(int _slot, EmoteEntry _entry, Action<int> _onSlotClick)
    {
        this.IsSlot = true;
        this.Bind(_slot, _entry, true, _onSlotClick);
    }

    public void BindEmote(int _emoteId, EmoteEntry _entry, bool _owned, Action<int> _onEmoteClick)
    {
        this.IsSlot = false;
        this.Bind(_emoteId, _entry, _owned, _onEmoteClick);
    }

    public void SetEquipped(bool _on)
    {
        this.m_equipped = !this.IsSlot && _on;
        if (this.equippedMark != null) this.equippedMark.SetActive(this.m_equipped);
        this.ApplyAlpha();
        this.RefreshInput();
    }

    public void SetFocus(bool _focusing, bool _match)
    {
        this.m_focusDimmed = _focusing && !_match;
        this.ApplyAlpha();
        this.ApplyFocusScale(_focusing && _match, false);
    }

    public void SetHighlight(bool _on)
    {
        if (this.hoverHighlight != null) this.hoverHighlight.SetActive(this.IsSlot && _on);
    }

    public void SetSwapTarget(bool _on, bool _instant = false)
    {
        _on &= this.IsSlot;
        if (_on && this.dimGroup != null) this.dimGroup.alpha = 1f;
        if (this.swapGlow != null)
        {
            if (_on)
            {
                if (this.m_swapGlowBlink == null)
                    this.m_swapGlowBlink = this.swapGlow.GetComponentInChildren<UiGlowBlink>(true);
                if (this.m_swapGlowBlink != null) this.m_swapGlowBlink.SetPhase(this.m_key * 0.17f);
            }
            this.swapGlow.SetActive(_on);
        }
        if (this.swapMark != null) this.swapMark.SetActive(_on && this.EmoteId > 0);
        this.ApplySwapScale(_on, _instant);
    }

    public void PlayEquipPunch()
    {
        if (!this.IsSlot || this.icon == null || this.EmoteId <= 0) return;
        this.m_punchTween?.Complete();
        this.m_punchTween = UiPunch.Play(this.icon.transform, 0.22f, 0.22f);
    }

    public void ConfigureDrag(Action<EmoteItemCell, PointerEventData> _onDrag)
    {
        this.m_onDrag = _onDrag;
        if (this.m_longPress == null) this.m_longPress = this.GetComponent<LongPressDetector>();
        if (!this.IsSlot && this.m_longPress == null)
            this.m_longPress = this.gameObject.AddComponent<LongPressDetector>();
        if (this.m_longPress != null)
        {
            this.m_longPress.OnLongPress -= this.BeginDrag;
            this.m_longPress.OnLongPress += this.BeginDrag;
        }
        this.RefreshInput();
    }

    public void OnPointerDown(PointerEventData _data)
    {
        this.m_pointerData = _data;
        this.m_suppressClick = false;
    }

    void BeginDrag()
    {
        if (this.IsSlot || !this.m_owned || this.m_equipped || this.EmoteId <= 0 ||
            this.m_onDrag == null || this.m_pointerData == null ||
            this.m_pointerData.button != PointerEventData.InputButton.Left) return;
        this.m_suppressClick = true;
        this.m_pointerData.eligibleForClick = false;
        this.m_onDrag.Invoke(this, this.m_pointerData);
    }

    public void CancelGesture()
    {
        this.m_suppressClick = true;
        if (this.m_pointerData != null)
        {
            this.m_pointerData.eligibleForClick = false;
            if (this.m_longPress != null) this.m_longPress.OnPointerUp(this.m_pointerData);
        }
        this.m_pointerData = null;
    }

    void Bind(int _key, EmoteEntry _entry, bool _owned, Action<int> _onClick)
    {
        this.CancelGesture();
        this.m_key = _key;
        this.m_onClick = _onClick;
        this.EmoteId = _entry != null ? _entry.id : 0;
        this.m_owned = _owned;
        this.m_focusDimmed = false;
        bool t_hasSprite = _entry != null && _entry.sprite != null;
        if (this.icon != null)
        {
            this.icon.gameObject.SetActive(t_hasSprite);
            this.icon.sprite = t_hasSprite ? _entry.sprite : null;
        }
        if (this.emptyMark != null) this.emptyMark.SetActive(this.IsSlot && this.EmoteId <= 0);
        if (this.button != null)
        {
            this.button.onClick.RemoveAllListeners();
            if (_onClick != null) this.button.onClick.AddListener(this.HandleClick);
        }
        this.SetHighlight(false);
        this.SetEquipped(false);
        this.ApplyFocusScale(false, true);
        this.SetSwapTarget(false, true);
    }

    void RefreshInput()
    {
        bool t_allowed = this.m_owned && (this.IsSlot || !this.m_equipped);
        if (this.button != null) this.button.interactable = t_allowed;
        if (this.m_longPress != null)
        {
            bool t_dragAllowed = t_allowed && !this.IsSlot && this.m_onDrag != null;
            if (!t_dragAllowed) this.CancelGesture();
            this.m_longPress.enabled = t_dragAllowed;
        }
    }

    void ApplyAlpha()
    {
        if (this.dimGroup != null)
            this.dimGroup.alpha = this.m_focusDimmed ? 0.2f
                : this.m_equipped ? 0.45f : this.m_owned ? 1f : this.dimAlpha;
    }

    void ApplyFocusScale(bool _up, bool _instant)
    {
        float t_target = _up ? 1.08f : 1f;
        this.m_focusTween?.Kill();
        this.m_focusTween = null;
        if (_instant || !Application.isPlaying || Mathf.Approximately(this.transform.localScale.x, t_target))
            this.transform.localScale = Vector3.one * t_target;
        else
            this.m_focusTween = this.transform.DOScale(t_target, 0.12f)
                .SetEase(_up ? Ease.OutBack : Ease.OutQuad).SetLink(this.gameObject);
    }

    void ApplySwapScale(bool _down, bool _instant)
    {
        this.m_swapTween?.Kill();
        this.m_swapTween = null;
        this.m_punchTween?.Kill();
        this.m_punchTween = null;
        if (this.icon == null) return;
        Transform t_tr = this.icon.transform;
        float t_target = _down ? 0.86f : 1f;
        if (_instant || !Application.isPlaying || Mathf.Approximately(t_tr.localScale.x, t_target))
            t_tr.localScale = Vector3.one * t_target;
        else
            this.m_swapTween = t_tr.DOScale(t_target, 0.14f)
                .SetEase(_down ? Ease.OutBack : Ease.OutQuad).SetLink(this.gameObject);
    }

    void OnDisable()
    {
        this.CancelGesture();
        this.m_focusDimmed = false;
        this.ApplyAlpha();
        this.SetHighlight(false);
        this.ApplyFocusScale(false, true);
        this.SetSwapTarget(false, true);
    }

    void OnDestroy()
    {
        if (this.m_longPress != null) this.m_longPress.OnLongPress -= this.BeginDrag;
        this.m_focusTween?.Kill();
        this.m_swapTween?.Kill();
        this.m_punchTween?.Kill();
    }

    void HandleClick()
    {
        if (!this.m_suppressClick && this.m_owned && (this.IsSlot || !this.m_equipped))
            this.m_onClick?.Invoke(this.m_key);
    }
}
