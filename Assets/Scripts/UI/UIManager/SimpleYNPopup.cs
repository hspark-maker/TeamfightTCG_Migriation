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

        // TODO 임시 진단용 — 폰트 이슈 확인 끝나면 제거할 것.
        DumpFont("title", this.titleText);
        DumpFont("yes", this.yesText);
        DumpFont("no", this.noText);

        this.yesButton.onClick.RemoveAllListeners();
        this.noButton.onClick.RemoveAllListeners();

        this.yesButton.onClick.AddListener(() => { t_d.yesAction?.Invoke(); Hide(); });
        this.noButton.onClick.AddListener(() => { t_d.noAction?.Invoke(); Hide(); });
    }

    // TODO 임시 진단용 — 폰트 이슈 확인 끝나면 제거할 것.
    static void DumpFont(string _label, TextMeshProUGUI _text)
    {
        if (_text == null) { Debug.Log($"[YNPopup:{_label}] 텍스트 미배선"); return; }

        _text.ForceMeshUpdate();   // 폴백 서브메시는 메시가 만들어져야 생긴다.

        string t_font = _text.font != null ? _text.font.name : "null";
        string t_mat  = _text.fontSharedMaterial != null ? _text.fontSharedMaterial.name : "null";

        // 폴백이 발생하면 TMP가 'TMP SubMeshUI [폰트명]' 자식을 만든다. 이게 있으면 폰트에 없는 글자가 있다는 뜻.
        var t_subs = _text.GetComponentsInChildren<TMP_SubMeshUI>(true);
        string t_fallback = t_subs.Length == 0
            ? "없음"
            : string.Join(", ", Array.ConvertAll(t_subs, _s => _s.name));

        Debug.Log($"[YNPopup:{_label}] font={t_font} / mat={t_mat} / 폴백서브메시={t_fallback}");
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