using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 프로필 편집 팝업의 감정표현 칸 하나. 두 자리에서 함께 쓴다 —
// 위쪽 장착 줄(칸=슬롯)과 아래쪽 풀 그리드(칸=감정표현).
//
// 그래서 콜백이 실어 보내는 숫자의 뜻이 자리마다 다르다. 둘 다 int라 섞여도 컴파일러가 못 잡으므로
// 묶는 문(BindSlot / BindEmote)을 나눠 호출부에서 무엇을 묶는지가 드러나게 한다.
//
// 미소유 룩(딤 + 클릭 차단)은 지금 항상 소유(true)로만 부르지만 미리 심어 둔다(ProfileItemCell과 같음).
public class EmoteItemCell : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("감정표현 그림. 빈 칸(id 0)이면 꺼진다.")]
    [SerializeField] Image icon;
    [Tooltip("선택 배지. 장착 줄에서는 '지금 고르는 슬롯', 풀에서는 '그 슬롯에 끼워진 감정표현'을 가리킨다.")]
    [SerializeField] GameObject selectedMark;
    [SerializeField] GameObject equippedMark;
    [SerializeField] Button button;
    [Tooltip("미소유 딤. 미배선이면 딤 없이 버튼만 잠근다.")]
    [SerializeField] CanvasGroup dimGroup;

    [Range(0f, 1f)]
    [Tooltip("미소유일 때의 알파.")]
    [SerializeField] float dimAlpha = 0.4f;

    int m_key;
    Action<int> m_onClick;
    Action<EmoteItemCell, PointerEventData> m_onDrag;
    LongPressDetector m_longPress;
    PointerEventData m_pointerData;
    bool m_suppressClick;
    bool m_owned;
    bool m_selected;
    bool m_highlighted;

    public int EmoteId { get; private set; }
    public bool IsSlot { get; private set; }
    public RectTransform Rect => (RectTransform)this.transform;
    public Sprite Sprite => this.icon != null ? this.icon.sprite : null;

    /// <summary>클릭 때 되돌려주는 숫자. 뜻은 어느 Bind로 묶였는지가 정한다 —
    /// 장착 줄이면 슬롯 번호, 풀이면 감정표현 id다.</summary>
    public int Key => this.m_key;

    /// <summary>장착 줄의 칸 하나. 클릭하면 그 슬롯이 '고르는 자리'가 된다.
    /// 장착 줄은 잠그지 않는다 — 빈 칸도 눌러서 채워야 하기 때문이다.</summary>
    public void BindSlot(int _slot, EmoteEntry _entry, Action<int> _onSlotClick)
    {
        this.IsSlot = true;
        this.Bind(_slot, _entry, true, _onSlotClick);
    }

    /// <summary>풀 그리드의 칸 하나. 클릭하면 그 감정표현이 고른 슬롯에 들어간다.</summary>
    public void BindEmote(int _emoteId, EmoteEntry _entry, bool _owned, Action<int> _onEmoteClick)
    {
        this.IsSlot = false;
        this.Bind(_emoteId, _entry, _owned, _onEmoteClick);
    }

    /// <summary>선택 표시를 켜고 끈다. 선택은 패널이 단일 진실원으로 쥔다 — 칸이 스스로 뒤집지 않는다.</summary>
    public void SetSelected(bool _on)
    {
        this.m_selected = _on;
        this.RefreshMark();
    }

    public void SetEquipped(bool _on)
    {
        if (this.equippedMark != null) this.equippedMark.SetActive(_on);
    }

    public void SetHighlight(bool _on)
    {
        this.m_highlighted = _on;
        this.RefreshMark();
    }

    void RefreshMark()
    {
        if (this.selectedMark != null) this.selectedMark.SetActive(this.m_selected || this.m_highlighted);
    }

    public void ConfigureDrag(Action<EmoteItemCell, PointerEventData> _onDrag)
    {
        this.m_onDrag = _onDrag;
        if (this.m_longPress == null)
            this.m_longPress = this.GetComponent<LongPressDetector>() ?? this.gameObject.AddComponent<LongPressDetector>();
        this.m_longPress.OnLongPress -= this.BeginDrag;
        this.m_longPress.OnLongPress += this.BeginDrag;
    }

    public void OnPointerDown(PointerEventData _data)
    {
        this.m_pointerData = _data;
        this.m_suppressClick = false;
    }

    void BeginDrag()
    {
        if (!this.m_owned || this.EmoteId <= 0 || this.m_pointerData == null ||
            this.m_pointerData.button != PointerEventData.InputButton.Left) return;
        this.m_suppressClick = true;
        this.m_pointerData.eligibleForClick = false;
        this.m_onDrag?.Invoke(this, this.m_pointerData);
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

    void OnDisable() => this.CancelGesture();

    void OnDestroy()
    {
        if (this.m_longPress != null) this.m_longPress.OnLongPress -= this.BeginDrag;
    }

    // 클릭 리스너는 매번 갈아 끼운다(재바인딩 시 중복 등록 방지).
    void Bind(int _key, EmoteEntry _entry, bool _owned, Action<int> _onClick)
    {
        this.m_key = _key;
        this.m_onClick = _onClick;
        this.EmoteId = _entry != null ? _entry.id : 0;
        this.m_owned = _owned;

        bool t_hasSprite = _entry != null && _entry.sprite != null;
        if (this.icon != null)
        {
            this.icon.gameObject.SetActive(t_hasSprite);
            this.icon.sprite = t_hasSprite ? _entry.sprite : null;
        }

        if (this.dimGroup != null) this.dimGroup.alpha = _owned ? 1f : this.dimAlpha;

        if (this.button != null)
        {
            this.button.onClick.RemoveAllListeners();
            this.button.interactable = _owned;
            if (_owned && _onClick != null) this.button.onClick.AddListener(this.HandleClick);
        }

        this.SetSelected(false);
        this.SetEquipped(false);
    }

    void HandleClick()
    {
        if (!this.m_suppressClick) this.m_onClick?.Invoke(this.m_key);
    }
}
