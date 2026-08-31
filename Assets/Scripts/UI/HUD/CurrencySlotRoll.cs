using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상단바 재화 칸이 다른 재화로 갈릴 때의 세로 롤. 옛 내용이 한쪽으로 빠지고 새 내용이 반대쪽에서 올라온다.
///
/// 요소(아이콘·숫자)마다 <b>창</b>을 한 겹 끼워 그 안에서만 움직인다. 창 rect는 요소 rect를 그대로 복사하므로
/// 정지 화면은 조립 전과 완전히 같고, <b>코인 도착 지점(창)이 롤 중에도 움직이지 않는다</b> —
/// 도착 좌표는 연출 조립 시점에 구워지므로 이게 깨지면 코인이 엉뚱한 곳으로 날아간다.
///
/// 마스크는 롤이 도는 동안에만 켠다. 아이콘이 알약 밖으로 삐져나오게 저작돼 있어 상시 클리핑하면 평소 그림이 잘린다.
/// </summary>
public class CurrencySlotRoll
{
    const string WINDOW_SUFFIX = "_RollWindow";

    // 나가는 옛 내용의 사본. 이름에 "Icon"을 넣지 말 것 —
    // CurrencyGainEffectPlayer가 도착 지점 부근에서 이름으로 아이콘을 찾을 때 이 사본을 집는다.
    const string GHOST_NAME = "Roll_Out";

    // 이동량 여유. 요소가 창보다 커도 꼬리가 남지 않을 만큼만.
    const float TRAVEL_MARGIN = 8f;

    Lane m_icon;
    Lane m_text;
    float m_travel;
    Sequence m_roll;

    /// <summary>롤이 도는 중인지. 도는 칸을 다른 재화가 또 뺏으면 한 세대 밀린 그림이 나간다.</summary>
    public bool IsRolling => m_roll != null && m_roll.IsActive();

    /// <summary>숫자가 앉은 창. 롤 중에도 움직이지 않으므로 코인 도착 지점은 이쪽을 쓴다.</summary>
    public RectTransform TextWindow => m_text.Window;

    /// <summary>칸에 창을 끼운다. 이미 끼워져 있으면 아무 일도 하지 않는다.</summary>
    public void Bind(RectTransform _slot, Image _icon, TMP_Text _text)
    {
        if (_slot == null || _icon == null || _text == null) return;
        if (m_icon.Window != null) return;

        // 창은 요소가 있던 자리를 그대로 물려받는다 — 요소가 칸의 직계 자식이 아니면 그 전제가 깨진다.
        if (_icon.transform.parent != _slot || _text.transform.parent != _slot) return;

        m_icon = Lane.Build(_slot, (RectTransform)_icon.transform);
        m_text = Lane.Build(_slot, (RectTransform)_text.transform);

        // 보이는 띠는 아이콘 창 높이다. 아이콘은 알약 밖으로 삐져나오게 저작돼 있어 그보다 좁게 자르면
        // 롤이 끝나고 마스크가 꺼지는 프레임에 잘려 있던 위아래가 한 번에 되살아나 툭 튄다.
        float t_band = m_icon.Window.rect.height;
        m_icon.SetBand(t_band);
        m_text.SetBand(t_band);

        // 두 줄이 한 띠로 읽히려면 같은 거리를 움직인다. 띠 밖으로 완전히 빠져야 꼬리가 안 남는다.
        m_travel = Mathf.Max(t_band, Mathf.Max(m_icon.Window.rect.height, m_text.Window.rect.height)) + TRAVEL_MARGIN;
    }

    /// <summary>옛 내용을 실어 롤을 재생한다. 교체는 호출부가 이미 끝낸 상태여야 한다(진짜 요소가 곧 새 내용이다).</summary>
    public void Play(bool _upward, Sprite _oldIcon, string _oldText, float _duration, Ease _ease)
    {
        if (m_icon.Window == null) return;
        if (_duration <= 0f) { this.Snap(); return; }

        // 앞선 롤은 먼저 앉힌다 — 그래야 지금 보이는 내용이 다음 사본의 출발점이 된다.
        this.Snap();

        float t_out = _upward ? m_travel : -m_travel;

        m_icon.Arm(_oldIcon, null, t_out);
        m_text.Arm(null, _oldText, t_out);

        m_roll = DOTween.Sequence().SetLink(m_icon.Window.gameObject);
        m_icon.Insert(m_roll, _duration, _ease, t_out);
        m_text.Insert(m_roll, _duration, _ease, t_out);

        // 끊겨도 반드시 앉는다 — 변위한 채로 굳으면 칸이 영영 어긋난다.
        m_roll.OnKill(this.Snap);
    }

    /// <summary>롤을 걷고 정지 상태로 되돌린다.</summary>
    public void Snap()
    {
        Sequence t_roll = m_roll;
        m_roll = null;
        if (t_roll != null && t_roll.IsActive()) t_roll.Kill();

        m_icon.Rest();
        m_text.Rest();
    }

    // 요소 한 줄: 창 + 진짜 요소 + 나가는 사본.
    struct Lane
    {
        public RectTransform Window;
        public RectMask2D Mask;
        public RectTransform Real;
        public RectTransform Ghost;
        public Image GhostIcon;
        public TMP_Text GhostText;

        // 보이는 띠 높이를 맞춘다. padding은 클립 영역만 좁힐 뿐 요소를 옮기지 않으므로 정지 화면은 그대로다.
        public void SetBand(float _band)
        {
            float t_trim = Mathf.Max(0f, (this.Window.rect.height - _band) * 0.5f);
            this.Mask.padding = new Vector4(0f, t_trim, 0f, t_trim);
        }

        public static Lane Build(RectTransform _slot, RectTransform _element)
        {
            var t_lane = new Lane();

            var t_window = new GameObject(_element.name + WINDOW_SUFFIX, typeof(RectTransform), typeof(RectMask2D));
            t_window.layer = _slot.gameObject.layer;
            t_lane.Window = (RectTransform)t_window.transform;
            t_lane.Window.SetParent(_slot, false);
            t_lane.Window.SetSiblingIndex(_element.GetSiblingIndex());

            // 창이 요소 자리를 그대로 물려받는다 — 그래야 정지 화면도, 코인 도착 좌표도 안 바뀐다.
            CopyRect(_element, t_lane.Window);

            t_lane.Mask = t_window.GetComponent<RectMask2D>();
            t_lane.Mask.enabled = false;

            _element.SetParent(t_lane.Window, false);
            Fill(_element);
            t_lane.Real = _element;

            var t_ghost = Object.Instantiate(_element, t_lane.Window);
            t_ghost.name = GHOST_NAME;
            Fill(t_ghost);
            t_lane.Ghost = t_ghost;
            t_lane.GhostIcon = t_ghost.GetComponent<Image>();
            t_lane.GhostText = t_ghost.GetComponent<TMP_Text>();
            SetRaycast(t_ghost, false);
            t_ghost.gameObject.SetActive(false);

            return t_lane;
        }

        // 나가는 사본에 옛 내용을 싣고 제자리에 세운다. 진짜 요소는 반대쪽 밖에서 대기한다.
        public void Arm(Sprite _oldIcon, string _oldText, float _out)
        {
            if (this.GhostIcon != null && _oldIcon != null) this.GhostIcon.sprite = _oldIcon;
            if (this.GhostText != null && _oldText != null) this.GhostText.text = _oldText;

            this.Ghost.gameObject.SetActive(true);
            SetY(this.Ghost, 0f);
            SetY(this.Real, -_out);
            this.Mask.enabled = true;
        }

        public void Insert(Sequence _seq, float _duration, Ease _ease, float _out)
        {
            _seq.Insert(0f, this.Ghost.DOAnchorPosY(_out, _duration).SetEase(_ease));
            _seq.Insert(0f, this.Real.DOAnchorPosY(0f, _duration).SetEase(_ease));
        }

        public void Rest()
        {
            if (this.Window == null) return;

            this.Ghost.DOKill();
            this.Real.DOKill();
            SetY(this.Real, 0f);
            this.Ghost.gameObject.SetActive(false);
            this.Mask.enabled = false;
        }
    }

    // 창이 요소와 똑같은 rect를 갖게 한다. 자식으로 들어간 요소는 Fill로 창을 가득 채우므로 결과 rect가 보존된다.
    static void CopyRect(RectTransform _from, RectTransform _to)
    {
        _to.anchorMin = _from.anchorMin;
        _to.anchorMax = _from.anchorMax;
        _to.pivot = _from.pivot;
        _to.sizeDelta = _from.sizeDelta;
        _to.anchoredPosition = _from.anchoredPosition;
        _to.localScale = Vector3.one;
        _to.localRotation = Quaternion.identity;
    }

    static void Fill(RectTransform _rect)
    {
        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = Vector2.one;
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta = Vector2.zero;
        _rect.anchoredPosition = Vector2.zero;
    }

    static void SetY(RectTransform _rect, float _y)
    {
        Vector2 t_pos = _rect.anchoredPosition;
        t_pos.y = _y;
        _rect.anchoredPosition = t_pos;
    }

    static void SetRaycast(RectTransform _root, bool _on)
    {
        var t_graphics = _root.GetComponentsInChildren<Graphic>(true);
        for (int t_i = 0; t_i < t_graphics.Length; t_i++) t_graphics[t_i].raycastTarget = _on;
    }
}
