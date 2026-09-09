using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비와 전투 설정창이 공유하는 환경설정 컨트롤.</summary>
public class SettingsOptionsView : MonoBehaviour
{
    [SerializeField] Slider   bgmSlider;
    [SerializeField] TMP_Text bgmValueText;
    [SerializeField] Slider   sfxSlider;
    [SerializeField] TMP_Text sfxValueText;

    [Header("Frame Rate")]
    // 프리팹의 FrameRateRow 버튼들. 순서는 GameManager.FrameRateOptions와 1:1로 맞춘다.
    [SerializeField] Button[] frameRateButtons;

    [Header("Screen Shake")]
    // 타격 화면 흔들림 켜기/끄기. FPS 행과 같은 규약(선택된 쪽만 밝게) — 미배선이면 옵션 줄만 빠진다.
    [SerializeField] Button screenShakeOnButton;
    [SerializeField] Button screenShakeOffButton;

    void Awake()
    {
        BindFrameRateButtons();
        this.screenShakeOnButton?.onClick.AddListener(() => OnScreenShakeChanged(true));
        this.screenShakeOffButton?.onClick.AddListener(() => OnScreenShakeChanged(false));
        this.bgmSlider?.onValueChanged.AddListener(OnBGMChanged);
        this.sfxSlider?.onValueChanged.AddListener(OnSFXChanged);
    }

    void OnEnable() => Refresh();

    public void Refresh()
    {
        if (SoundManager.Instance != null)
        {
            this.bgmSlider?.SetValueWithoutNotify(SoundManager.Instance.BGMVolume);
            this.sfxSlider?.SetValueWithoutNotify(SoundManager.Instance.SFXVolume);
            RefreshText(this.bgmValueText, SoundManager.Instance.BGMVolume);
            RefreshText(this.sfxValueText, SoundManager.Instance.SFXVolume);
        }

        RefreshFrameRateButtons();
        RefreshScreenShakeButtons();
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

    /// <summary>프리팹에 저작된 FPS 버튼에 옵션 값을 인덱스로 물린다.
    /// 배선이 옵션 수와 어긋나면 조용히 잘못 적용되는 대신 로그로 드러낸다.</summary>
    void BindFrameRateButtons()
    {
        if (this.frameRateButtons == null || this.frameRateButtons.Length == 0) return;

        if (this.frameRateButtons.Length != GameManager.FrameRateOptions.Length)
        {
            Debug.LogError($"[SettingsOptionsView] FPS 버튼 배선 {this.frameRateButtons.Length}개 ≠ 옵션 {GameManager.FrameRateOptions.Length}개");
            return;
        }

        for (int i = 0; i < this.frameRateButtons.Length; i++)
        {
            int t_frameRate = GameManager.FrameRateOptions[i];
            this.frameRateButtons[i]?.onClick.AddListener(() => OnFrameRateChanged(t_frameRate));
        }
    }

    void OnFrameRateChanged(int _frameRate)
    {
        GameManager.SetTargetFrameRate(_frameRate);
        RefreshFrameRateButtons();
    }

    void RefreshFrameRateButtons()
    {
        if (this.frameRateButtons == null) return;
        if (this.frameRateButtons.Length != GameManager.FrameRateOptions.Length) return;

        for (int i = 0; i < this.frameRateButtons.Length; i++)
            ApplySelectedTint(this.frameRateButtons[i],
                              GameManager.FrameRateOptions[i] == GameManager.CurrentFrameRate);
    }

    void OnScreenShakeChanged(bool _on)
    {
        GameManager.SetScreenShake(_on);
        RefreshScreenShakeButtons();
    }

    void RefreshScreenShakeButtons()
    {
        ApplySelectedTint(this.screenShakeOnButton,   GameManager.ScreenShakeEnabled);
        ApplySelectedTint(this.screenShakeOffButton, !GameManager.ScreenShakeEnabled);
    }

    /// <summary>선택 표시를 <b>칸 자신</b>(SelectionStateView)에게 넘긴다 — 스프라이트도 색도 그쪽이 소유한다.
    /// 여기(패널)는 "어느 칸이 선택인가"만 안다. 두 줄(FPS·화면 흔들림)이 같은 규약을 쓴다.
    /// 컴포넌트가 미배선이면 그 칸만 표시가 안 바뀌므로 조용히 넘어가지 않고 로그로 드러낸다.</summary>
    static void ApplySelectedTint(Button _button, bool _selected)
    {
        if (_button == null) return;

        SelectionStateView t_state = _button.GetComponent<SelectionStateView>();
        if (t_state == null)
        {
            Debug.LogError($"[SettingsOptionsView] {_button.name}에 SelectionStateView 미배선 — 선택 표시가 안 바뀐다");
            return;
        }

        t_state.SetSelected(_selected);
    }

    void RefreshText(TMP_Text _text, float _val)
    {
        if (_text != null) _text.text = $"{Mathf.RoundToInt(_val * 100)}%";
    }
}
