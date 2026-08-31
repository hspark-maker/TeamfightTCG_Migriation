using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>덱의 전투 참여 가능 시너지를 현재/다음 티어 진행도와 함께 표시한다.</summary>
public class DeckSynergyStrip : MonoBehaviour
{
    /// <summary>롱프레스로 시너지 하나를 들여다보는 중이면 그 시너지, 손을 떼면 null.
    /// 어떤 카드를 강조할지는 편성 상태를 아는 쪽(DeckEditController)이 정한다.</summary>
    public Action<SynergyData> onFocusChanged;

    [SerializeField] SynergyCountIcon[] icons;

    [Tooltip("표시할 시너지가 하나도 없을 때 같이 숨길 배경판. 미배선이면 이 오브젝트의 Graphic을 쓴다.")]
    [SerializeField] Graphic background;

    [Header("Filter")]
    [Tooltip("해제하면 아직 활성화되지 않은 시너지는 숨긴다.")]
    [SerializeField] bool showInactive = true;
    [Tooltip("0보다 크면 이 개수까지만 표시한다.")]
    [SerializeField] int maxIcons;

    readonly List<int> eligibleCards = new List<int>();

    // 내가 연 popup이 떠 있는 동안만 true. 안 연 채로 닫으면 남의 popup을 대신 지운다.
    bool m_explainOpen;

    // 설명창이 떠 있는 동안 잠근 스크롤과 그 원래 축 설정.
    // 아이콘은 IDragHandler를 구현하지 않으므로(부모 스크롤을 죽이지 않으려고) 롱프레스가 발화한 뒤에도
    // 손가락 이동이 그대로 부모 ScrollRect의 드래그로 흘러간다 — 설명창을 띄운 채 화면이 스와이프된다.
    ScrollRect m_lockedScroll;
    bool       m_scrollWasVertical;
    bool       m_scrollWasHorizontal;

    void Awake()
    {
        if (this.icons == null) return;

        foreach (SynergyCountIcon t_icon in this.icons)
        {
            if (t_icon == null) continue;
            t_icon.onLongPress    = ShowExplain;
            t_icon.onLongPressEnd = _ => HideExplain();
            t_icon.gameObject.SetActive(false);
        }
    }

    public void Refresh(IEnumerable<int> _deck)
    {
        this.eligibleCards.Clear();
        if (_deck != null)
        {
            foreach (int t_card in _deck)
            {
                if (t_card <= 0) continue;
                if (DeckConfig.IsMultiplayer)
                {
                    this.eligibleCards.Add(t_card);
                    continue;
                }

                CardGrowth t_growth = CardGrowthManager.GrowthOf(t_card);
                if (!t_growth.Applied || t_growth.SynergyUnlocked)
                    this.eligibleCards.Add(t_card);
            }
        }

        List<SynergyProgress> t_all = SynergyPreview.Resolve(this.eligibleCards);

        // 배경 판정은 **필터를 걸기 전** 값으로 한다 — 기준은 "시너지를 가진 카드가 한 장이라도 있는가"다.
        // 아래 t_shown은 showInactive·maxIcons로 걸러진 뒤라, 그걸로 재면
        // "시너지 카드는 있는데 아직 티어 미달"인 덱에서 판이 사라진다(설정에 따라 의미가 흔들린다).
        ShowBackground(t_all.Count > 0);

        var t_shown = new List<SynergyProgress>();
        foreach (SynergyProgress t_progress in t_all)
        {
            if (!this.showInactive && !t_progress.IsActive) continue;
            t_shown.Add(t_progress);
            if (this.maxIcons > 0 && t_shown.Count >= this.maxIcons) break;
        }

        if (this.icons == null) return;
        for (int i = 0; i < this.icons.Length; i++)
        {
            SynergyCountIcon t_icon = this.icons[i];
            if (t_icon == null) continue;
            if (i < t_shown.Count) t_icon.Set(t_shown[i]);
            else                   t_icon.gameObject.SetActive(false);
        }
    }

    // 시너지를 가진 카드가 한 장도 없으면 빈 판때기만 남는다(ContentSizeFitter가 줄여도 배경은 그려진다).
    // GameObject를 끄지 않고 Graphic만 끈다 — 이 스크립트가 그 오브젝트에 붙어 있어서,
    // 통째로 끄면 다음 Refresh를 받을 주체가 사라지고 OnDisable의 설명창 정리까지 딸려 나간다.
    void ShowBackground(bool _show)
    {
        if (this.background == null) this.background = GetComponent<Graphic>();
        if (this.background == null) return;

        this.background.enabled = _show;
    }

    public void Clear() => Refresh(null);

    void OnDisable() => HideExplain();

    void ShowExplain(SynergyCountIcon _icon)
    {
        if (_icon == null || _icon.Progress?.Synergy == null) return;

        ExplainPopupData t_data = ExplainPopupData.ForSynergy(_icon.Progress.Synergy, _icon.Progress.Count);
        if (t_data == null) return;

        t_data.iconRect = (RectTransform)_icon.transform;
        UIPoolManager.Instance?.AddOrUpdateUI<ExplainPopupUI>(t_data);

        this.m_explainOpen = true;
        LockScroll(true);
        this.onFocusChanged?.Invoke(_icon.Progress.Synergy);
    }

    // 설명 popup과 강조는 항상 같이 뜨고 같이 진다 — 한쪽만 남으면 화면이 흐린 채로 굳는다.
    void HideExplain()
    {
        if (!this.m_explainOpen) return;

        this.m_explainOpen = false;
        LockScroll(false);
        UIPoolManager.Instance?.HideUI<ExplainPopupUI>();
        this.onFocusChanged?.Invoke(null);
    }

    // 스크롤 자체를 비활성(enabled=false)하지 않는다 — 드래그 도중 꺼지면 ScrollRect가 OnEndDrag를 못 받아
    // 내부 드래그 상태가 켜진 채 남고, 다음 터치에서 관성이 튄다. 축만 닫으면 이벤트는 정상적으로 끝난다.
    // 원래 값을 기억했다 되돌린다 — true로 되돌리면 가로 스크롤이 없던 목록에 없던 축이 생긴다.
    void LockScroll(bool _on)
    {
        if (_on)
        {
            if (this.m_lockedScroll != null) return;   // 이미 잠갔다(원래 값을 덮어쓰면 복구 불능)

            ScrollRect t_scroll = GetComponentInParent<ScrollRect>(true);
            if (t_scroll == null) return;

            this.m_lockedScroll        = t_scroll;
            this.m_scrollWasVertical   = t_scroll.vertical;
            this.m_scrollWasHorizontal = t_scroll.horizontal;

            t_scroll.StopMovement();          // 이미 굴러가던 관성은 여기서 끊는다
            t_scroll.velocity   = Vector2.zero;
            t_scroll.vertical   = false;
            t_scroll.horizontal = false;

            return;
        }

        if (this.m_lockedScroll == null) return;

        this.m_lockedScroll.StopMovement();   // 잠긴 동안 쌓인 이동량이 풀리는 순간 튀지 않게
        this.m_lockedScroll.velocity   = Vector2.zero;
        this.m_lockedScroll.vertical   = this.m_scrollWasVertical;
        this.m_lockedScroll.horizontal = this.m_scrollWasHorizontal;
        this.m_lockedScroll            = null;
    }
}
