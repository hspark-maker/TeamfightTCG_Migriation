using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum EAccountServiceCellState
{
    Connected,
    Add,
}

/// <summary>서비스 셀의 상태별 표시. 인증과 클릭 동작은 소유 패널이 담당한다.</summary>
[ExecuteAlways]
public sealed class AccountServiceCellView : MonoBehaviour
{
    [Serializable]
    sealed class Appearance
    {
        [Tooltip("패널 이미지. 비워 두면 숨긴다.")] public Sprite panel;
        [Tooltip("기본 아이콘. 연결 상태에서는 전달받은 서비스 아이콘을 우선한다.")] public Sprite icon;
        [Tooltip("버튼 이미지. 비워 두면 숨긴다.")] public Sprite button;
        [Tooltip("버튼 문구.")] public string label;
    }

    [Header("표시 대상")]
    [SerializeField] Image panelImage;
    [SerializeField] Image icon;
    [SerializeField] Image buttonImage;
    [SerializeField] TMP_Text buttonLabel;
    [SerializeField] Button disconnectButton;

    [Header("상태별 표시")]
    [SerializeField] EAccountServiceCellState state;
    [SerializeField] Appearance connected = new Appearance { label = "연결 해제" };
    [SerializeField] Appearance add = new Appearance { label = "연결 추가" };

    Sprite serviceIcon;
    Action onClick;
    Button boundButton;

    void Awake()
    {
        this.boundButton = this.disconnectButton;
        if (this.boundButton != null)
            this.boundButton.onClick.AddListener(OnClick);
    }

    void OnEnable() => ApplyAppearance();

    void OnDestroy()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= ApplyEditorPreview;
#endif
        if (this.boundButton != null)
            this.boundButton.onClick.RemoveListener(OnClick);
        this.onClick = null;
    }

    public void Bind(EAccountServiceCellState _state, Sprite _icon, bool _interactable, Action _onClick)
    {
        this.state = _state;
        this.serviceIcon = _icon;
        this.onClick = _onClick;
        if (this.disconnectButton != null) this.disconnectButton.interactable = _interactable;
        ApplyAppearance();
    }

    public void ApplyAppearance()
    {
        Appearance t_appearance = this.state == EAccountServiceCellState.Add ? this.add : this.connected;
        Sprite t_icon = this.state == EAccountServiceCellState.Connected && this.serviceIcon != null
            ? this.serviceIcon : t_appearance?.icon;
        ApplySprite(this.panelImage, t_appearance?.panel);
        ApplySprite(this.icon, t_icon);
        ApplySprite(this.buttonImage, t_appearance?.button);
        if (this.buttonLabel != null) this.buttonLabel.text = t_appearance?.label ?? string.Empty;
    }

    static void ApplySprite(Image _image, Sprite _sprite)
    {
        if (_image == null) return;
        _image.sprite = _sprite;
        _image.enabled = _sprite != null;
    }

    void OnClick()
    {
        if (this.disconnectButton != null && this.disconnectButton.interactable)
            this.onClick?.Invoke();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall -= ApplyEditorPreview;
        UnityEditor.EditorApplication.delayCall += ApplyEditorPreview;
    }

    void OnDisable() => UnityEditor.EditorApplication.delayCall -= ApplyEditorPreview;

    void ApplyEditorPreview()
    {
        UnityEditor.EditorApplication.delayCall -= ApplyEditorPreview;
        if (this != null) ApplyAppearance();
    }
#endif
}
