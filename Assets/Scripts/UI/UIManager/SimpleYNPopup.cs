using UnityEngine;
using System;
using UnityEngine.UI;
using TMPro;

public class SimpleYNPopup : PooledUIBase
{
    [SerializeField] Button yesButton;
    [SerializeField] Button noButton;
    [SerializeField] TextMeshProUGUI yesText;
    [SerializeField] TextMeshProUGUI noText;
    [SerializeField] TextMeshProUGUI titleText;

    public override void Initialization(UIData _data)
    {
        this.data = _data;
        var t_d = _data as SimpleYNPopupData;

        this.titleText.text = t_d.titleText;

        this.yesText.text = t_d.yesText;
        this.noText.text = t_d.noText;

        this.yesButton.onClick.RemoveAllListeners();
        this.noButton.onClick.RemoveAllListeners();

        this.yesButton.onClick.AddListener(() => { t_d.yesAction?.Invoke(); Hide(); });
        this.noButton.onClick.AddListener(() => { t_d.noAction?.Invoke(); Hide(); });
    }

    public override void Show()
    {
        this.contents.SetActive(true);
        this.isShow = true;
        this.data.showCustomMethod?.Invoke();
    }

    public override void Hide()
    {
        this.contents.SetActive(false);
        this.isShow = false;
        this.data.onHide?.Invoke();
    }
}


public class SimpleYNPopupData : UIData
{
    public string titleText;
    public Action yesAction;
    public string yesText;
    public Action noAction;
    public string noText;
}
