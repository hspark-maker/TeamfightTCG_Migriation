using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsPanel : PooledUIBase
{
    [SerializeField] Slider   bgmSlider;
    [SerializeField] TMP_Text bgmValueText;
    [SerializeField] Slider   sfxSlider;
    [SerializeField] TMP_Text sfxValueText;

    [Header("Battle")]
    // 전투 전용. 로비 등 TurnRunner가 없는 씬에서는 Show가 통째로 숨긴다.
    [SerializeField] Button surrenderButton;
    // 디버그 강제 승리. 에디터 전용 — 빌드에서는 리스너도 안 걸고 오브젝트도 항상 꺼둔다.
    [SerializeField] Button winDebugButton;

    protected override void Awake()
    {
        base.Awake();
        this.bgmSlider?.onValueChanged.AddListener(OnBGMChanged);
        this.sfxSlider?.onValueChanged.AddListener(OnSFXChanged);
        this.surrenderButton?.onClick.AddListener(OnSurrender);
#if UNITY_EDITOR
        this.winDebugButton?.onClick.AddListener(OnDebugWin);
#endif
        // 버튼의 창 닫기(Hide)는 프리팹 onClick 영속 호출에 이미 배선돼 있다 — 여기서 또 걸지 않는다.
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

        RefreshBattleButtons();

        this.animator?.Fade(this.contents.GetComponent<CanvasGroup>(), 1f);
    }

    /// <summary>전투 전용 버튼 노출 판정. 기준은 TurnRunner.Instance 하나 —
    /// 씬 이름/플래그로 판정하면 전투 씬이 늘 때마다 갈라진다.</summary>
    void RefreshBattleButtons()
    {
        bool t_inBattle = TurnRunner.Instance != null;

        if (this.surrenderButton != null) this.surrenderButton.gameObject.SetActive(t_inBattle);

#if UNITY_EDITOR
        if (this.winDebugButton != null) this.winDebugButton.gameObject.SetActive(t_inBattle);
#else
        // 빌드: 심볼이 없어 OnDebugWin 자체가 컴파일되지 않는다 → 눌러도 아무 일 없는 버튼이 남지 않게 항상 끈다.
        if (this.winDebugButton != null) this.winDebugButton.gameObject.SetActive(false);
#endif
    }

    void OnSurrender() => TurnRunner.Instance?.Surrender();

#if UNITY_EDITOR
    void OnDebugWin() => TurnRunner.Instance?.DebugForceWin();
#endif

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
