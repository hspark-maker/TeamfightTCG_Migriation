using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsPanel : PooledUIBase
{
    static readonly Color FrameRateSelected   = Color.white;
    static readonly Color FrameRateUnselected = new Color(0.55f, 0.55f, 0.55f, 1f);

    // 등장/퇴장 floating 연출 값. 배경 dim은 제자리에 두고 패널만 뜬다 —
    // dim까지 움직이면 화면 끝에 안 덮인 띠가 드러난다.
    const float FloatEnterOffsetY = -60f;
    const float FloatExitOffsetY  = -90f;
    const float FloatEnterScale   = 0.94f;
    const float FloatExitScale    = 0.92f;
    const float FloatEnterTime    = 0.28f;
    const float FloatExitTime     = 0.22f;

    // 설정창은 전투 UI(UIPoolManager 캔버스 400)·멀리건 오버레이(500) 위, 로딩 커버(1000) 아래에 선다.
    // 창이 떠 있는 동안 뒤에서 다른 UI가 등장해도 dim 아래로 깔려 튀어나오지 않는다.
    const int SortingOrder = 900;

    // 페이지 전환(메뉴 ↔ 환경설정) 피드백.
    const float PageSlideX    = 70f;
    const float PageSlideTime = 0.18f;
    const float TitlePopScale = 0.88f;

    [Header("Float")]
    // dim(DimCloseButton)을 제외한 창 본체. BG·타이틀·두 페이지가 전부 이 밑에 있다.
    [SerializeField] RectTransform panelRoot;
    Vector2 panelHomePos;
    bool    hiding;   // 퇴장 연출 진행 중. 중복 Hide로 시퀀스가 겹치지 않게 잠근다.

    [Header("Pages")]
    // 창 하나에 두 페이지. 켜고 끄기만 하고 생성하지 않는다 — 둘 다 프리팹에 저작돼 있다.
    [SerializeField] GameObject menuPage;
    [SerializeField] GameObject optionsPage;
    Vector2 menuHomePos;
    Vector2 optionsHomePos;
    [SerializeField] TMP_Text   titleText;
    [SerializeField] Button     optionsButton;       // 메뉴 → 환경설정
    [SerializeField] Button     optionsBackButton;   // 환경설정 → 메뉴(창을 닫지 않는다)

    [SerializeField] Slider   bgmSlider;
    [SerializeField] TMP_Text bgmValueText;
    [SerializeField] Slider   sfxSlider;
    [SerializeField] TMP_Text sfxValueText;

    [Header("Frame Rate")]
    // 프리팹의 FrameRateRow 버튼들. 순서는 GameManager.FrameRateOptions와 1:1로 맞춘다.
    [SerializeField] Button[] frameRateButtons;

    [Header("Battle")]
    // 전투 전용. 로비 등 TurnRunner가 없는 씬에서는 Show가 통째로 숨긴다.
    [SerializeField] Button surrenderButton;
    // 디버그 강제 승리. 에디터 전용 — 빌드에서는 리스너도 안 걸고 오브젝트도 항상 꺼둔다.
    [SerializeField] Button winDebugButton;

    protected override void Awake()
    {
        base.Awake();
        // 정렬 승격은 코드가 확정한다 — overrideSorting은 부모 캔버스가 있어야 의미가 있어
        // 프리팹(단독 루트) 상태로는 저장되지 않는다. 캔버스 컴포넌트 자체는 프리팹에 저작돼 있다.
        Canvas t_canvas = GetComponent<Canvas>();
        if (t_canvas != null)
        {
            t_canvas.overrideSorting = true;
            t_canvas.sortingOrder    = SortingOrder;
        }

        // 저작된 자리가 곧 복귀 목표. 풀 재사용으로 Show가 여러 번 돌아도 이 값은 안 변한다.
        if (this.panelRoot != null) this.panelHomePos = this.panelRoot.anchoredPosition;
        BindFrameRateButtons();
        CachePageHome(this.menuPage,    ref this.menuHomePos);
        CachePageHome(this.optionsPage, ref this.optionsHomePos);
        // 페이지 전환은 리스너를 Awake에서 한 번만 건다 — 풀에서 재사용되므로 Show에서 걸면 눌린 횟수만큼 중복된다.
        // 버튼으로 넘길 때만 전환 연출을 켠다(창이 처음 열릴 때는 등장 연출과 겹쳐 산만해진다).
        this.optionsButton?.onClick.AddListener(() => ShowPage(_options: true,  _animate: true));
        this.optionsBackButton?.onClick.AddListener(() => ShowPage(_options: false, _animate: true));
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
        this.hiding = false;   // 퇴장 연출 도중 다시 열렸으면 잠금 해제 — 안 풀면 다음 Hide가 통째로 무시된다.

        // 퇴장이 껐던 입력을 되돌린다(퇴장 중 재오픈 경로).
        CanvasGroup t_cg = this.contents.GetComponent<CanvasGroup>();
        if (t_cg != null) t_cg.blocksRaycasts = true;

        // 창이 떠 있는 동안 필드 카드 조작을 막는다(덱 보기 창과 같은 규약).
        // InputAllowed가 아니라 UiBlocking인 이유: 창을 닫을 때 생각시간 예산이 리셋되면 안 된다.
        TurnState.UiBlocking = true;

        PlayFloatIn();

        // 풀 재사용: 지난번에 환경설정을 열어둔 채 닫았어도 항상 메뉴부터 시작한다.
        ShowPage(_options: false);

        if (SoundManager.Instance != null)
        {
            this.bgmSlider?.SetValueWithoutNotify(SoundManager.Instance.BGMVolume);
            this.sfxSlider?.SetValueWithoutNotify(SoundManager.Instance.SFXVolume);
            RefreshText(this.bgmValueText, SoundManager.Instance.BGMVolume);
            RefreshText(this.sfxValueText, SoundManager.Instance.SFXVolume);
        }

        RefreshBattleButtons();
        RefreshFrameRateButtons();

        this.animator?.Fade(this.contents.GetComponent<CanvasGroup>(), 1f);
    }

    /// <summary>메뉴 ↔ 환경설정 페이지 전환. 배경·타이틀은 두 페이지가 공유하므로 여기서 갈아끼운다.
    /// _animate면 새 페이지가 진행 방향에서 밀려 들어오고 타이틀이 살짝 튄다 — 눌렀다는 피드백.</summary>
    void ShowPage(bool _options, bool _animate = false)
    {
        if (this.menuPage    != null) this.menuPage.SetActive(!_options);
        if (this.optionsPage != null) this.optionsPage.SetActive(_options);
        if (this.titleText   != null) this.titleText.text = _options ? "환경설정" : "메뉴";

        if (!_animate)
        {
            // 전환 트윈이 돌던 중에 창이 닫혔다면 페이지가 중간 위치에 굳어 있다 — 저작 자리로 즉시 되돌린다.
            ResetPage(this.menuPage,    this.menuHomePos);
            ResetPage(this.optionsPage, this.optionsHomePos);
            if (this.titleText != null)
            {
                this.titleText.transform.DOKill();
                this.titleText.transform.localScale = Vector3.one;
            }
            return;
        }

        // 앞으로(환경설정) 갈 땐 오른쪽에서, 뒤로(메뉴) 갈 땐 왼쪽에서 들어온다.
        PlayPageIn(_options ? this.optionsPage : this.menuPage,
                   _options ? this.optionsHomePos : this.menuHomePos,
                   _options ? 1f : -1f);
        PlayTitlePop();
    }

    /// <summary>페이지 슬라이드 인. 저작 위치는 Awake에 캐시해 둔 값이 진실원 —
    /// 트윈 도중 다시 눌러도 그 순간의 좌표를 기준으로 삼지 않는다(연타 시 자리가 밀리던 함정).</summary>
    void PlayPageIn(GameObject _page, Vector2 _home, float _dir)
    {
        if (_page == null || _page.transform is not RectTransform t_rt) return;

        t_rt.DOKill();
        t_rt.anchoredPosition = _home + new Vector2(PageSlideX * _dir, 0f);
        t_rt.DOAnchorPos(_home, PageSlideTime).SetEase(Ease.OutCubic).SetLink(_page);
    }

    /// <summary>타이틀은 자리를 지키고 크기만 튄다 — 두 페이지가 공유하는 요소라 같이 밀리면 전환이 어색해진다.</summary>
    void PlayTitlePop()
    {
        if (this.titleText == null) return;

        Transform t_tr = this.titleText.transform;
        t_tr.DOKill();
        t_tr.localScale = Vector3.one * TitlePopScale;
        t_tr.DOScale(1f, PageSlideTime).SetEase(Ease.OutBack, 2f).SetLink(this.titleText.gameObject);
    }

    static void ResetPage(GameObject _page, Vector2 _home)
    {
        if (_page == null || _page.transform is not RectTransform t_rt) return;
        t_rt.DOKill();
        t_rt.anchoredPosition = _home;
    }

    static void CachePageHome(GameObject _page, ref Vector2 _home)
    {
        if (_page != null && _page.transform is RectTransform t_rt) _home = t_rt.anchoredPosition;
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

    /// <summary>닫기 진입점. 실제 정리는 퇴장 연출을 <b>기다린 뒤</b> 한다 —
    /// 예전엔 페이드 콜백에만 매달려 있어서 연출이 끝나기 전에 창이 사라진 것처럼 보였다.</summary>
    public override void Hide()
    {
        if (this.hiding || !this.isShow) return;   // 연타/중복 Hide로 시퀀스가 겹치지 않게.
        HideRoutine().Forget();
    }

    async UniTaskVoid HideRoutine()
    {
        this.hiding = true;

        // 연출이 도는 동안 창 자체 입력을 끊는다 — 사라지는 중인 버튼이 눌리면 안 된다.
        // 레이캐스트는 여전히 막으므로 뒤쪽 필드로 터치가 새지도 않는다.
        CanvasGroup t_cg = this.contents.GetComponent<CanvasGroup>();
        if (t_cg != null) t_cg.blocksRaycasts = false;

        bool t_canceled = await PlayFloatOut(t_cg);
        if (t_canceled) return;   // 파괴됨 — 아래 정리는 의미 없다(플래그는 OnDestroy가 푼다).

        this.contents.SetActive(false);
        this.isShow = false;
        this.hiding = false;

        // 카드 조작 차단 해제는 창이 완전히 사라진 뒤. 연출 도중에 풀면
        // 창을 끈 그 터치가 그대로 필드 카드 선택으로 이어진다.
        TurnState.UiBlocking = false;
    }

    /// <summary>등장: 살짝 아래에서 떠오르며 커진다. 트윈은 매번 DOKill로 갈아엎는다 —
    /// 풀 재사용이라 이전 여닫기의 트윈이 남아 있으면 중간 위치에서 굳는다.</summary>
    void PlayFloatIn()
    {
        if (this.panelRoot == null) return;

        this.panelRoot.DOKill();
        this.panelRoot.anchoredPosition = this.panelHomePos + new Vector2(0f, FloatEnterOffsetY);
        this.panelRoot.localScale       = Vector3.one * FloatEnterScale;

        this.panelRoot.DOAnchorPos(this.panelHomePos, FloatEnterTime)
            .SetEase(Ease.OutCubic).SetLink(this.panelRoot.gameObject);
        this.panelRoot.DOScale(1f, FloatEnterTime)
            .SetEase(Ease.OutBack, 1.1f).SetLink(this.panelRoot.gameObject);
    }

    /// <summary>퇴장: 살짝 위로 튀었다가 가라앉으며 작아지고, 같은 길이로 페이드아웃.
    /// 이동·축소·페이드를 한 시퀀스로 묶어 await한다 — 예전처럼 페이드(0.3s)와 길이가 어긋나면
    /// 알파가 먼저 빠져 움직임이 안 보인다. 반환값 true = 파괴로 취소됨.</summary>
    async UniTask<bool> PlayFloatOut(CanvasGroup _cg)
    {
        if (this.panelRoot == null)
        {
            if (_cg == null) return false;
            return await _cg.DOFade(0f, FloatExitTime).SetLink(gameObject)
                .ToUniTask(cancellationToken: this.GetCancellationTokenOnDestroy())
                .SuppressCancellationThrow();
        }

        this.panelRoot.DOKill();
        if (_cg != null) _cg.DOKill();

        Sequence t_seq = DOTween.Sequence().SetLink(this.panelRoot.gameObject);
        t_seq.Join(this.panelRoot.DOAnchorPos(this.panelHomePos + new Vector2(0f, FloatExitOffsetY), FloatExitTime)
                       .SetEase(Ease.InBack, 1.2f));
        t_seq.Join(this.panelRoot.DOScale(FloatExitScale, FloatExitTime).SetEase(Ease.InCubic));
        if (_cg != null) t_seq.Join(_cg.DOFade(0f, FloatExitTime).SetEase(Ease.InCubic));

        return await t_seq.ToUniTask(cancellationToken: this.GetCancellationTokenOnDestroy())
                          .SuppressCancellationThrow();
    }

    protected override void OnDestroy()
    {
        // 창이 떠 있는 채로 파괴되면(씬 전환 등) 차단 플래그가 켜진 채 남아 카드 입력이 영영 죽는다.
        if (this.isShow) TurnState.UiBlocking = false;
        this.panelRoot?.DOKill();
        base.OnDestroy();
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
            Debug.LogError($"[SettingsPanel] FPS 버튼 배선 {this.frameRateButtons.Length}개 ≠ 옵션 {GameManager.FrameRateOptions.Length}개");
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
        {
            Button t_button = this.frameRateButtons[i];
            if (t_button == null) continue;

            bool t_selected = GameManager.FrameRateOptions[i] == GameManager.CurrentFrameRate;
            Color t_tint = t_selected ? FrameRateSelected : FrameRateUnselected;

            // ColorTint 버튼이라 targetGraphic.color를 직접 쓰면 상태 전이가 바로 덮어쓴다 — ColorBlock을 갈아끼운다.
            ColorBlock t_colors = t_button.colors;
            t_colors.normalColor      = t_tint;
            t_colors.highlightedColor = t_tint;
            t_colors.selectedColor    = t_tint;
            t_colors.pressedColor     = new Color(t_tint.r * 0.75f, t_tint.g * 0.75f, t_tint.b * 0.75f, t_tint.a);
            t_button.colors = t_colors;
        }
    }

    void RefreshText(TMP_Text _text, float _val)
    {
        if (_text != null) _text.text = $"{Mathf.RoundToInt(_val * 100)}%";
    }
}
