using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsPanel : PooledUIBase
{
    [SerializeField] Slider   bgmSlider;
    [SerializeField] TMP_Text bgmValueText;
    [SerializeField] Slider   sfxSlider;
    [SerializeField] TMP_Text sfxValueText;

    protected override void Awake()
    {
        base.Awake();
        this.bgmSlider?.onValueChanged.AddListener(OnBGMChanged);
        this.sfxSlider?.onValueChanged.AddListener(OnSFXChanged);
    }

    public override void Initialization(UIData _data) { }

    public override void Show()
    {
        this.contents.SetActive(true);
        this.isShow = true;

        if (SoundManager.Instance != null)
        {
            this.bgmSlider?.SetValueWithoutNotify(SoundManager.Instance.BGMVolume);
            this.sfxSlider?.SetValueWithoutNotify(SoundManager.Instance.SFXVolume);
            RefreshText(this.bgmValueText, SoundManager.Instance.BGMVolume);
            RefreshText(this.sfxValueText, SoundManager.Instance.SFXVolume);
        }

        this.animator?.Fade(this.contents.GetComponent<CanvasGroup>(), 1f);
    }

    public override void Hide()
    {
        CanvasGroup t_cg = this.contents.GetComponent<CanvasGroup>();
        if (t_cg != null)
            this.animator?.Fade(t_cg, 0f, () => { this.contents.SetActive(false); this.isShow = false; });
        else
        {
            this.contents.SetActive(false);
            this.isShow = false;
        }
    }

    public void OnBGMChanged(float _val)
    {
        SoundManager.Instance?.SetBGMVolume(_val);
        RefreshText(this.bgmValueText, _val);
    }

    public void OnSFXChanged(float _val)
    {
        SoundManager.Instance?.SetSFXVolume(_val);
        RefreshText(this.sfxValueText, _val);
    }

    void RefreshText(TMP_Text _text, float _val)
    {
        if (_text != null) _text.text = $"{Mathf.RoundToInt(_val * 100)}%";
    }
}
