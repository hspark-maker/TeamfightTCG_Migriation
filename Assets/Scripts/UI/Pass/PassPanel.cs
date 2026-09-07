using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 배틀패스 화면(PassOverlay 에 부착). 풀(UIPoolManager)이 수명을 쥔다 —
/// 규약은 <see cref="MissionPanel"/> 과 같다.
///
/// <para><b>시즌·곡선·보상을 이 화면이 들고 있지 않는다.</b> 전부 서버 <c>getPass</c> 응답이고
/// 레벨 판정도 서버 값이다. 화면이 사본을 두면 밸런스 수정이 앱 배포에 묶인다.</para>
///
/// <para>열 때 조회를 던지되 기다리지 않는다 — 캐시로 즉시 그리고
/// <see cref="PassManager.OnChanged"/> 가 오면 다시 그린다.</para>
/// </summary>
public class PassPanel : PooledUIBase
{
    // 풀 계약. 표시 데이터는 PassManager 에서 스스로 당기므로 UIData 가 필요 없다.
    public override void Initialization(UIData _data) { }

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    [Tooltip("켜고 끌 대상(딤 + 패널). 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [Header("시즌 머리")]
    [Tooltip("시즌 표시명. 시즌이 없으면 안내 문구로 바뀐다.")]
    [SerializeField] TMP_Text seasonText;

    [Tooltip("남은 기간. 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text remainText;

    [Tooltip("현재 레벨 표시.")]
    [SerializeField] TMP_Text levelText;

    [Tooltip("경험치 표시(현재/다음 문턱).")]
    [SerializeField] TMP_Text expText;

    [Tooltip("0~1 로 채우는 게이지(Image.Type = Filled). 비워 두면 그리지 않는다.")]
    [SerializeField] Image expFill;

    [Header("목록")]
    [Tooltip("레벨 행이 쌓일 Content(VerticalLayoutGroup).")]
    [SerializeField] Transform levelContent;

    [Tooltip("행 프리팹. Content 안의 목업 행을 물려도 된다 — 원본은 지우지 않고 숨긴다.")]
    [SerializeField] PassLevelRowView rowPrefab;

    [Tooltip("활성 시즌이 없거나 아직 조회 전일 때 켤 안내.")]
    [SerializeField] GameObject emptyNotice;

    [Header("버튼")]
    [SerializeField] Button closeButton;

    [Tooltip("패널 밖(딤)을 눌러 닫는 판. 알파 0 Image 의 Button 에 배선한다.")]
    [SerializeField] Button dimButton;

    [Header("연출")]
    [Tooltip("panel 에는 Root/Panel 을 배선한다 — root 를 물리면 전체화면 딤까지 함께 커진다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("공용 ScreenDim(Full)에 요청할 암막 짙기.")]
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.72f;

    readonly List<PassLevelRowView> m_rows = new List<PassLevelRowView>();

    // 지금 깔린 행이 어느 시즌·곡선으로 만들어졌는지. 시즌이 바뀌면 다시 깐다.
    string m_builtSignature;

    // 씬 버튼 UnityEvent 가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.
    public void Open()
    {
        this.SetVisible(true);
        this.Rebuild();

        // 캐시가 있으면 그것으로 먼저 그리고, 조회는 던져만 둔다. 시즌 경계가 지났을 수 있어
        // 열 때마다 한 번은 새로 묻는다 — 미션과 달리 초기화 선조회가 없다.
        PassCommands.RefreshAsync().Forget();
    }

    public void Close() => this.SetVisible(false);

    void OnEnable()
    {
        // 재활성마다 중복 등록 방지.
        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveAllListeners();
            this.closeButton.onClick.AddListener(this.Close);
        }
        if (this.dimButton != null)
        {
            this.dimButton.onClick.RemoveAllListeners();
            this.dimButton.onClick.AddListener(this.Close);
        }

        PassManager.OnChanged += this.HandlePassChanged;
    }

    void OnDisable()
    {
        PassManager.OnChanged -= this.HandlePassChanged;

        // 안전망 — Close 를 거치지 않고 꺼지면 공용 딤이 남는다.
        ScreenDim.Hide(this);
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    void Update()
    {
        // 남은 기간만 매 프레임 갱신한다. 행 다시 그리기는 OnChanged 가 유발한다.
        if (!this.isShow) return;
        this.RefreshHeader();
    }

    void HandlePassChanged()
    {
        if (BuildSignature() != this.m_builtSignature) this.Rebuild();
        else this.RefreshRows();
    }

    void Rebuild()
    {
        this.BuildRows();
        this.m_builtSignature = BuildSignature();
        this.RefreshHeader();
    }

    void BuildRows()
    {
        this.m_rows.Clear();
        if (this.levelContent == null || this.rowPrefab == null) return;

        // Destroy 는 프레임 끝에 처리되므로 먼저 비활성화한다 — 레이아웃 계산에서 빠져야 이번 프레임 배치가 맞는다.
        // rowPrefab 이 Content 안 목업 행으로 배선되는 저작도 허용해야 하므로 원본은 지우지 않고 숨긴다.
        GameObject t_template = this.rowPrefab.gameObject;
        for (int i = this.levelContent.childCount - 1; i >= 0; i--)
        {
            GameObject t_child = this.levelContent.GetChild(i).gameObject;
            t_child.SetActive(false);
            if (t_child != t_template) Destroy(t_child);
        }

        IReadOnlyList<PassLevelDefinition> t_levels = PassManager.Levels;
        for (int i = 0; i < t_levels.Count; i++)
        {
            PassLevelDefinition t_level = t_levels[i];
            if (t_level == null) continue;

            PassLevelRowView t_row = Instantiate(this.rowPrefab, this.levelContent);
            t_row.gameObject.SetActive(true);   // 위에서 원본을 숨겼을 수 있다 — 사본은 항상 보이게.
            t_row.Bind(t_level, this.HandleClaim);
            this.m_rows.Add(t_row);
        }

        if (this.emptyNotice != null) this.emptyNotice.SetActive(this.m_rows.Count == 0);
    }

    void RefreshRows()
    {
        for (int i = 0; i < this.m_rows.Count; i++)
            if (this.m_rows[i] != null) this.m_rows[i].Refresh();

        this.RefreshHeader();
    }

    // 곡선의 신원. 시즌과 레벨 수가 그대로면 다시 깔 이유가 없다.
    static string BuildSignature()
    {
        IReadOnlyList<PassLevelDefinition> t_levels = PassManager.Levels;
        return $"{PassManager.Season?.SeasonId ?? string.Empty}:{t_levels.Count}";
    }

    void RefreshHeader()
    {
        PassSeasonDefinition t_season = PassManager.Season;

        if (this.seasonText != null)
            this.seasonText.text = t_season != null ? t_season.DisplayName
                : PassManager.IsReady ? "진행 중인 시즌이 없다" : "불러오는 중…";

        if (this.levelText != null)
            this.levelText.text = t_season != null
                ? $"Lv.{PassManager.CurrentLevel} / {t_season.MaxLevel}"
                : string.Empty;

        long t_exp = PassManager.Exp;
        long? t_next = PassManager.NextRequiredExp;
        if (this.expText != null)
            this.expText.text = t_season == null ? string.Empty
                : t_next.HasValue ? $"{t_exp} / {t_next.Value} EXP" : $"{t_exp} EXP (MAX)";

        if (this.expFill != null)
            this.expFill.fillAmount = FillOf(t_exp, t_next);

        this.RefreshRemainLabel(t_season);
    }

    // 게이지는 현재 레벨 문턱과 다음 문턱 사이 비율이다 — 0 부터 재면 뒷레벨에서 거의 안 움직인다.
    static float FillOf(long _exp, long? _next)
    {
        if (!_next.HasValue) return 1f;

        long t_floor = 0L;
        IReadOnlyList<PassLevelDefinition> t_levels = PassManager.Levels;
        for (int i = 0; i < t_levels.Count; i++)
        {
            PassLevelDefinition t_level = t_levels[i];
            if (t_level == null || t_level.RequiredExp > _exp) break;
            t_floor = t_level.RequiredExp;
        }

        long t_span = _next.Value - t_floor;
        if (t_span <= 0L) return 1f;
        return Mathf.Clamp01((float)(_exp - t_floor) / t_span);
    }

    // 종료 시각의 진실원은 서버가 준 epoch ms 다. 남은 시간 표시에만 기기 시계를 쓴다 —
    // 실제 시즌 판정은 서버가 하므로 시계를 돌려도 시즌이 바뀌지 않는다.
    void RefreshRemainLabel(PassSeasonDefinition _season)
    {
        if (this.remainText == null) return;
        if (_season == null || _season.EndAtMs <= 0L)
        {
            this.remainText.text = string.Empty;
            return;
        }

        long t_remainMs = _season.EndAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (t_remainMs < 0L) t_remainMs = 0L;

        var t_span = TimeSpan.FromMilliseconds(t_remainMs);
        this.remainText.text = t_span.TotalDays >= 1d
            ? $"{(int)t_span.TotalDays}일 {t_span.Hours}시간 남음"
            : $"{(int)t_span.TotalHours}시간 {t_span.Minutes}분 남음";
    }

    void HandleClaim(int _level) => this.ClaimAsync(_level).Forget();

    async UniTaskVoid ClaimAsync(int _level)
    {
        // 왕복 동안 입력을 막는다. 딤·스피너는 임계 뒤에만 뜨므로 빠른 응답에서는 깜빡이지 않는다.
        ServerWaitOverlay.Hold(this);
        try
        {
            // 낙관 갱신하지 않는다 — 응답의 진행 상태를 PassCommands 가 채택하고 OnChanged 가 화면을 갱신한다.
            await PassCommands.ClaimAsync(_level);
        }
        finally
        {
            // **팝업보다 먼저 걷는다.** 순서를 뒤집으면 안내가 대기 딤에 묻힌다.
            ServerWaitOverlay.Release(this);
        }
    }

    // 여는 순간 오버레이 자신을 켠다 — 저작본은 루트가 꺼진 채로 들어오므로, 켜 주지 않으면
    // 하위 Root 만 토글돼 화면에 아무것도 뜨지 않는다(MissionPanel 과 같은 규약).
    void SetVisible(bool _visible)
    {
        if (_visible && !this.gameObject.activeSelf) this.gameObject.SetActive(true);

        if (_visible) ScreenDim.Show(this, this.dimAlpha, true, this.transition.OpenDuration);
        else ScreenDim.Hide(this);

        this.isShow = _visible;
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
