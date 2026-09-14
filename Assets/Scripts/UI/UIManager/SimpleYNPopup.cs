using UnityEngine;
using System;
using UnityEngine.UI;
using TMPro;

public class SimpleYNPopup : ContentsPooledUI
{
    [SerializeField] Button yesButton;
    [SerializeField] Button noButton;
    [SerializeField] TextMeshProUGUI yesText;
    [SerializeField] TextMeshProUGUI noText;
    [SerializeField] TextMeshProUGUI titleText;

    protected override bool UsePopupTransition => false;
    protected override bool UseScreenDim => false;

    protected override void OnInitializeUI()
    {
        this.yesButton.onClick.AddListener(OnYes);
        this.noButton.onClick.AddListener(OnNo);
    }

    void OnYes()
    {
        (this.data as SimpleYNPopupData)?.yesAction?.Invoke();
        Hide();
    }

    void OnNo()
    {
        (this.data as SimpleYNPopupData)?.noAction?.Invoke();
        Hide();
    }

    public override void Initialization(UIData _data)
    {
        this.InitializeUI();
        this.data = _data;
        var t_d = _data as SimpleYNPopupData;

        this.titleText.text = t_d.titleText;

        this.yesText.text = t_d.yesText;
        this.noText.text = t_d.noText;
    }

    public override void Show()
    {
        this.SetContentsVisible(true);
        this.data?.showCustomMethod?.Invoke();
    }

    public override void Hide()
    {
        this.SetContentsVisible(false);
        this.data?.onHide?.Invoke();
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
