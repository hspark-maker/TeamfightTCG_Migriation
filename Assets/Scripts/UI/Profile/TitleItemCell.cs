using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>칭호의 선택·장착·소지 상태를 구분하는 목록 칸.</summary>
public sealed class TitleItemCell : MonoBehaviour, IUIInitializable
{
    [SerializeField] Button button;
    [SerializeField] Image icon;
    [SerializeField] TMP_Text nameText;
    [SerializeField] GameObject selectionFrame;
    [SerializeField] GameObject equippedMark;
    [SerializeField] GameObject lockedMark;
    [SerializeField] CanvasGroup visualGroup;

    string m_id;
    Action<string> m_onClick;
    bool m_initialized;
    bool m_locked;
    List<UiGrayscale.Toned> m_toned;

    public string Id => this.m_id;

    /// <summary>풀 재사용과 무관하게 클릭을 한 번 연결한다.</summary>
    public void InitializeUI()
    {
        if (this.m_initialized) return;
        if (this.button != null) this.button.onClick.AddListener(this.Select);
        this.m_initialized = true;
    }

    /// <summary>칭호 표시와 미리보기 입력을 연결한다.</summary>
    public void Bind(TitleEntry _entry, Action<string> _onClick)
    {
        this.InitializeUI();
        this.m_id = _entry.id;
        this.m_onClick = _onClick;
        if (this.nameText != null)
        {
            this.nameText.text = _entry.displayName;
        }
        if (this.icon != null)
        {
            this.icon.sprite = _entry.icon;
            this.icon.enabled = _entry.icon != null;
        }
    }

    /// <summary>장착과 선택은 별개 표식으로 표시한다.</summary>
    public void Refresh(bool _selected, bool _equipped, bool _owned)
    {
        if (this.selectionFrame != null) this.selectionFrame.SetActive(_selected);
        if (this.equippedMark != null) this.equippedMark.SetActive(_equipped);
        if (this.lockedMark != null) this.lockedMark.SetActive(!_owned);
        if (this.visualGroup != null) this.visualGroup.alpha = _owned ? 1f : 0.55f;
        if (this.m_locked == !_owned) return;
        this.m_locked = !_owned;
        UiGrayscale.Restore(this.m_toned);
        if (this.m_locked)
            this.m_toned = UiGrayscale.Apply(this.visualGroup != null ? this.visualGroup.gameObject : this.gameObject,
                this.selectionFrame != null ? this.selectionFrame.transform : null,
                this.lockedMark != null ? this.lockedMark.transform : null);
    }

    void Select() => this.m_onClick?.Invoke(this.m_id);
}
