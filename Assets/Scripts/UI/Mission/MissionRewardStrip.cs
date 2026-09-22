using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보상 미리보기 전용. 수령 콜백이나 서버 명령을 갖지 않는다.
public sealed class MissionRewardStrip : MonoBehaviour, IUIInitializable
{
    [System.Serializable]
    sealed class Slot
    {
        public GameObject root;
        public Image background;
        public Image icon;
        public TMP_Text label;
        public TMP_Text amount;
    }

    [SerializeField] Slot[] slots;
    [SerializeField] Button moreButton;
    [SerializeField] SynergyIconButton morePress;
    [SerializeField] Sprite accountExpIcon;
    [SerializeField] Sprite passExpIcon;
    [SerializeField] RectTransform detailsRoot;
    [SerializeField] Slot detailsItem;
    [Tooltip("행과 툴팁에서 공유하는 보상 종류별 배경색 설정.")]
    [SerializeField] MissionRewardColors backgroundColors;

    readonly List<Entry> m_entries = new List<Entry>();
    readonly List<Slot> m_detailsItems = new List<Slot>();
    bool m_initialized;

    readonly struct Entry
    {
        public readonly string Label;
        public readonly long Amount;
        public readonly Sprite Icon;
        public readonly Color Background;
        public Entry(string _label, long _amount, Sprite _icon, Color _background)
        { Label = _label; Amount = _amount; Icon = _icon; Background = _background; }
    }

    public void InitializeUI()
    {
        if (this.m_initialized) return;
        this.m_initialized = true;
        if (this.morePress != null)
        {
            this.morePress.onPointerDown += this.ShowDetails;
            this.morePress.onPointerUp += this.HideDetails;
        }
        this.HideDetails();
    }

    internal void BindCurrencies(List<ClaimRewardGain> _currencies)
        => Bind(new MissionReward { Currencies = _currencies });

    internal void Bind(MissionReward _reward)
    {
        this.InitializeUI();
        this.HideDetails();
        this.m_entries.Clear();
        if (_reward?.Currencies != null)
            foreach (var t_gain in _reward.Currencies)
            {
                if (t_gain == null || t_gain.Amount <= 0) continue;
                bool t_known = CurrencyCode.TryParse(t_gain.Currency, out var t_type);
                this.m_entries.Add(new Entry(t_known ? CurrencyLook.NameOf(t_type) : t_gain.Currency,
                    t_gain.Amount, t_known ? CurrencyLook.IconOf(t_type) : null,
                    this.backgroundColors != null && t_known ? this.backgroundColors.Currency(t_type) : FallbackColor));
            }
        if ((_reward?.AccountExp ?? 0) > 0)
            this.m_entries.Add(new Entry("경험치", _reward.AccountExp, this.accountExpIcon,
                this.backgroundColors != null ? this.backgroundColors.AccountExp : FallbackColor));
        if ((_reward?.PassExp ?? 0) > 0)
            this.m_entries.Add(new Entry("패스 경험치", _reward.PassExp, this.passExpIcon,
                this.backgroundColors != null ? this.backgroundColors.PassExp : FallbackColor));
        if (_reward?.Items != null)
            foreach (var t_item in _reward.Items)
            {
                if (t_item == null || t_item.Amount <= 0) continue;
                this.m_entries.Add(new Entry(t_item.RewardType == "Title" ? "칭호"
                        : RewardItemDisplay.NameOf(t_item.RewardType, t_item.RewardId),
                    t_item.Amount, t_item.RewardType == "Pack" ? PackSpec.Art(t_item.RewardId)
                        : RewardItemDisplay.ItemIcon(t_item.RewardType, t_item.RewardId),
                    this.backgroundColors != null ? this.backgroundColors.Item(t_item.RewardType) : FallbackColor));
            }

        int t_visible = Mathf.Min(3, this.slots?.Length ?? 0);
        for (int i = 0; i < (this.slots?.Length ?? 0); i++)
        {
            Slot t_slot = this.slots[i];
            bool t_show = i < t_visible && i < this.m_entries.Count;
            t_slot.root.SetActive(t_show);
            if (!t_show) continue;
            BindSlot(t_slot, this.m_entries[i]);
        }
        if (this.moreButton != null)
            this.moreButton.gameObject.SetActive(this.m_entries.Count > t_visible);
    }

    void ShowDetails()
    {
        if (this.detailsRoot == null || this.detailsItem?.root == null || this.m_entries.Count == 0) return;
        // 스크롤 마스크 밖, 소유 패널의 마지막 자식에 띄워 행 끝에서도 잘리지 않는다.
        var t_owner = GetComponentInParent<ContentsPooledUI>();
        Canvas t_canvas = GetComponentInParent<Canvas>();
        Transform t_parent = t_owner != null ? t_owner.contents.transform : t_canvas?.transform;
        if (t_parent == null) return;
        this.detailsRoot.SetParent(t_parent, false);
        this.detailsRoot.anchorMin = this.detailsRoot.anchorMax = new Vector2(0.5f, 0.5f);
        this.detailsRoot.pivot = new Vector2(0.5f, 0.5f);
        this.detailsRoot.SetAsLastSibling();
        int t_columns = this.m_entries.Count > 12 ? 3 : 2;
        int t_rows = Mathf.CeilToInt((float)this.m_entries.Count / t_columns);
        this.detailsRoot.sizeDelta = new Vector2(32f + t_columns * 214f - 10f, 62f + t_rows * 72f);
        for (int i = 0; i < this.m_entries.Count; i++)
        {
            if (i == this.m_detailsItems.Count)
            {
                GameObject t_root = Instantiate(this.detailsItem.root, this.detailsRoot);
                this.m_detailsItems.Add(new Slot
                {
                    root = t_root,
                    background = t_root.GetComponent<Image>(),
                    icon = t_root.transform.Find("Icon").GetComponent<Image>(),
                    label = t_root.transform.Find("Label").GetComponent<TMP_Text>(),
                    amount = t_root.transform.Find("Amount").GetComponent<TMP_Text>(),
                });
            }
            Slot t_slot = this.m_detailsItems[i];
            t_slot.root.SetActive(true);
            ((RectTransform)t_slot.root.transform).anchoredPosition = new Vector2(16f + i % t_columns * 214f, -50f - i / t_columns * 72f);
            BindSlot(t_slot, this.m_entries[i]);
        }
        for (int i = this.m_entries.Count; i < this.m_detailsItems.Count; i++)
            this.m_detailsItems[i].root.SetActive(false);
        this.detailsRoot.gameObject.SetActive(true);
        PopupPlacer.PlaceAboveAnchor(this.detailsRoot, (RectTransform)this.moreButton.transform, 18f, 16f);
    }

    static void BindSlot(Slot _slot, Entry _entry)
    {
        if (_slot.background != null) _slot.background.color = _entry.Background;
        _slot.icon.sprite = _entry.Icon;
        _slot.icon.enabled = _entry.Icon != null;
        _slot.label.text = _entry.Label;
        _slot.amount.text = $"+{_entry.Amount:N0}";
    }

    Color FallbackColor => this.backgroundColors != null ? this.backgroundColors.Fallback : new Color32(255, 247, 226, 255);

    void HideDetails()
    {
        if (this.detailsRoot != null) this.detailsRoot.gameObject.SetActive(false);
    }

    void OnDisable() => this.HideDetails();

    void OnApplicationFocus(bool _focused)
    {
        if (!_focused) this.HideDetails();
    }

    void OnDestroy()
    {
        // 상세창은 첫 열기부터 행 밖에 있으므로 행이 폐기될 때 함께 정리한다.
        if (this.detailsRoot != null && !this.detailsRoot.IsChildOf(transform))
            Destroy(this.detailsRoot.gameObject);
    }
}
