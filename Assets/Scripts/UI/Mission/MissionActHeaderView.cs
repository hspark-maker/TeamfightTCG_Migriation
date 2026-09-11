using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>가이드 미션 목록의 막 헤더 한 줄. 제목·진행 수를 그리고 접기 토글을 받는다.</summary>
public class MissionActHeaderView : MonoBehaviour
{
    [SerializeField] TMP_Text titleText;

    [Tooltip("\"1 / 3\" 꼴 진행 수. 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text countText;

    [Tooltip("헤더 전체를 누르면 접고 편다. 비워 두면 접기 없이 항상 펼침.")]
    [SerializeField] Button toggleButton;

    [Tooltip("접힘 상태를 나타내는 화살표. 접히면 -90도로 돌린다.")]
    [SerializeField] RectTransform arrow;

    [Tooltip("막을 전부 받았을 때 켜는 표식. 비워 두면 그리지 않는다.")]
    [SerializeField] GameObject completeMark;

    Action m_onToggle;

    internal void Bind(string _label, int _claimed, int _total, bool _collapsed, Action _onToggle)
    {
        this.m_onToggle = _onToggle;
        if (this.titleText != null) this.titleText.text = _label;
        if (this.countText != null) this.countText.text = $"{_claimed} / {_total}";
        if (this.completeMark != null) this.completeMark.SetActive(_total > 0 && _claimed >= _total);

        if (this.toggleButton != null)
        {
            this.toggleButton.onClick.RemoveAllListeners();
            this.toggleButton.onClick.AddListener(this.HandleToggle);
            this.toggleButton.interactable = _onToggle != null;
        }
        this.SetCollapsed(_collapsed);
    }

    internal void SetCollapsed(bool _collapsed)
    {
        if (this.arrow != null) this.arrow.localEulerAngles = new Vector3(0f, 0f, _collapsed ? -90f : 0f);
    }

    void HandleToggle() => this.m_onToggle?.Invoke();
}
