using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 덱 타이틀 옆의 시너지 아이콘 줄. 덱을 받아 <see cref="SynergyPreview"/>로 집계해 표시한다.
///
/// 표시 전용이고 상태를 안 들고 있다 — <see cref="DeckGroup"/>이 덱이 바뀔 때마다 Refresh를 부른다.
/// 아이콘은 풀링한다(매번 Destroy/Instantiate 하면 레이아웃이 튄다).
/// 전투용 SynergyResolver가 아니라 SynergyPreview를 쓰는 이유: 편성 중에는 아직 활성이 아닌
/// 시너지의 진행도도 보여야 하는데 Resolver는 활성만 반환한다.
/// </summary>
public class DeckSynergyStrip : MonoBehaviour
{
    [SerializeField] SynergyCountIcon iconPrefab;
    [SerializeField] Transform        iconParent;   // 보통 HorizontalLayoutGroup이 붙은 오브젝트
    [SerializeField] SynergyTooltip   tooltip;      // 롱프레스 설명(선택 — 미배선이면 롱프레스 무동작)

    [Header("Filter")]
    [Tooltip("체크 해제하면 아직 활성이 아닌 시너지는 숨긴다.")]
    [SerializeField] bool showInactive = true;
    [Tooltip("0보다 크면 이 개수까지만 표시한다(타이틀 옆 공간이 좁을 때).")]
    [SerializeField] int maxIcons = 0;

    readonly List<SynergyCountIcon> icons = new List<SynergyCountIcon>();

    /// <summary>덱(빈 슬롯 null 허용)을 받아 아이콘 줄을 다시 그린다.</summary>
    public void Refresh(IEnumerable<CardData> _deck)
    {
        List<SynergyProgress> t_all = SynergyPreview.Resolve(_deck);

        var t_shown = new List<SynergyProgress>();
        foreach (SynergyProgress t_p in t_all)
        {
            if (!this.showInactive && !t_p.IsActive) continue;
            t_shown.Add(t_p);
            if (this.maxIcons > 0 && t_shown.Count >= this.maxIcons) break;
        }

        EnsureIconCount(t_shown.Count);

        for (int i = 0; i < this.icons.Count; i++)
        {
            if (this.icons[i] == null) continue;
            if (i < t_shown.Count) this.icons[i].Set(t_shown[i]);
            else                   this.icons[i].gameObject.SetActive(false);   // 남는 건 숨김(풀 유지)
        }
    }

    public void Clear() => Refresh(null);

    void OnDisable() => this.tooltip?.Hide();

    void EnsureIconCount(int _needed)
    {
        if (this.iconPrefab == null || this.iconParent == null) return;
        while (this.icons.Count < _needed)
        {
            SynergyCountIcon t_icon = Instantiate(this.iconPrefab, this.iconParent);
            // 아이콘은 풀링되어 재사용되므로 구독은 생성 시 1회만(Refresh마다 걸면 중복 구독).
            t_icon.onLongPress    = ShowTooltip;
            t_icon.onLongPressEnd = _ => this.tooltip?.Hide();
            this.icons.Add(t_icon);
        }
    }

    void ShowTooltip(SynergyCountIcon _icon)
    {
        if (this.tooltip == null || _icon == null) return;
        this.tooltip.Show(_icon.Progress, (RectTransform)_icon.transform);
    }
}
