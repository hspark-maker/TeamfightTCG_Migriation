using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 일일·주간 미션 화면(MissionOverlay 에 부착). 풀(UIPoolManager)이 수명을 쥔다 —
/// 규약은 <see cref="RankRewardPanel"/>·<see cref="RoulettePanel"/> 과 같다
/// (캔버스 기준 해상도 1080x1920 주의 포함).
///
/// <para><b>미션 정의를 이 화면이 들고 있지 않는다.</b> 제목·설명·목표·보상은 전부 서버가
/// <c>getMissions</c> 로 준 값이고, 완료 판정은 <see cref="MissionManager"/> 한 곳이 소유한다.
/// 화면이 자기 사본을 두면 밸런스 수정이 앱 배포에 묶이고, 보이는 목표와 거절 조건이 갈린다.</para>
///
/// <para>열 때마다 조회를 한 번 던지지만 <b>기다리지 않는다</b> — 캐시된 상태로 즉시 그리고
/// 응답이 오면 <see cref="MissionManager.OnChanged"/> 로 다시 그린다. 미션은 부가 기능이라
/// 왕복 실패가 화면을 막아서는 안 된다.</para>
/// </summary>
public class MissionPanel : PooledUIBase
{
    // 풀 계약. 표시 데이터는 MissionManager 에서 스스로 당기므로 UIData 가 필요 없다.
    public override void Initialization(UIData _data) { }

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    [Tooltip("켜고 끌 대상(딤 + 패널). 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [Header("목록")]
    [Tooltip("일일 미션 행이 쌓일 Content(VerticalLayoutGroup).")]
    [SerializeField] Transform dailyContent;

    [Tooltip("주간 미션 행이 쌓일 Content(VerticalLayoutGroup).")]
    [SerializeField] Transform weeklyContent;

    [Tooltip("행 프리팹. Content 안의 목업 행을 물려도 된다 — 원본은 지우지 않고 숨긴다.")]
    [SerializeField] MissionRowView rowPrefab;

    [Tooltip("해당 주기에 미션이 하나도 없을 때 켤 안내. 지금은 활성 미션이 팩 개봉 축뿐이라 자주 빈다.")]
    [SerializeField] GameObject dailyEmptyNotice;

    [SerializeField] GameObject weeklyEmptyNotice;

    [Header("리셋 표시")]
    [Tooltip("남은 시간. 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text dailyResetText;

    [SerializeField] TMP_Text weeklyResetText;

    [Header("버튼")]
    [SerializeField] Button closeButton;

    [Tooltip("패널 밖(딤)을 눌러 닫는 판. 알파 0 Image 의 Button 에 배선한다.")]
    [SerializeField] Button dimButton;

    [Header("연출")]
    [Tooltip("panel 에는 Root/Panel 을 배선한다 — root 를 물리면 전체화면 딤까지 함께 커진다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("공용 ScreenDim(Full)에 요청할 암막 짙기.")]
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.72f;

    readonly List<MissionRowView> m_dailyRows = new List<MissionRowView>();
    readonly List<MissionRowView> m_weeklyRows = new List<MissionRowView>();

    // 지금 화면에 깔린 행이 어느 정의 목록으로 만들어졌는지. 정의가 바뀌면(첫 조회 응답 도착 등)
    // 다시 깐다 — 개수만 보면 활성 미션이 교체됐을 때 옛 제목이 남는다.
    string m_builtSignature;

    const string PERIOD_DAILY = "daily";
    const string PERIOD_WEEKLY = "weekly";

    // 씬 버튼 UnityEvent 가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.
    public void Open()
    {
        this.SetVisible(true);
        this.Rebuild();

        // 조회는 던져만 둔다. 실패해도 캐시로 그린 화면은 그대로 남고, 성공하면 OnChanged 가 다시 그린다.
        MissionCommands.RefreshAsync().Forget();
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

        MissionManager.OnChanged += this.HandleMissionsChanged;
    }

    void OnDisable()
    {
        MissionManager.OnChanged -= this.HandleMissionsChanged;

        // 안전망 — Close 를 거치지 않고 꺼지면 공용 딤이 남는다.
        ScreenDim.Hide(this);
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    void Update()
    {
        // 카운트다운만 매 프레임 갱신한다. 행 다시 그리기는 OnChanged 가 유발한다.
        if (!this.isShow) return;
        this.RefreshResetLabels();
    }

    // 조회 응답이 정의를 갈아끼웠을 수 있으므로 서명을 보고 필요할 때만 다시 깐다.
    void HandleMissionsChanged()
    {
        if (BuildSignature() != this.m_builtSignature) this.Rebuild();
        else this.RefreshRows();
    }

    void Rebuild()
    {
        BuildSection(this.dailyContent, this.m_dailyRows, PERIOD_DAILY, this.dailyEmptyNotice);
        BuildSection(this.weeklyContent, this.m_weeklyRows, PERIOD_WEEKLY, this.weeklyEmptyNotice);
        this.m_builtSignature = BuildSignature();
        this.RefreshResetLabels();
    }

    void BuildSection(Transform _content, List<MissionRowView> _rows, string _period, GameObject _emptyNotice)
    {
        _rows.Clear();
        if (_content == null || this.rowPrefab == null) return;

        // Destroy 는 프레임 끝에 처리되므로 먼저 비활성화한다 — 레이아웃 계산에서 빠져야 이번 프레임 배치가 맞는다.
        // rowPrefab 이 Content 안 목업 행으로 배선되는 저작도 허용해야 하므로 원본은 지우지 않고 숨긴다.
        GameObject t_template = this.rowPrefab.gameObject;
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            GameObject t_child = _content.GetChild(i).gameObject;
            t_child.SetActive(false);
            if (t_child != t_template) Destroy(t_child);
        }

        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        for (int i = 0; i < t_definitions.Count; i++)
        {
            MissionDefinition t_definition = t_definitions[i];
            if (!string.Equals(t_definition.Period, _period, StringComparison.Ordinal)) continue;

            MissionRowView t_row = Instantiate(this.rowPrefab, _content);
            t_row.gameObject.SetActive(true);   // 위에서 원본을 숨겼을 수 있다 — 사본은 항상 보이게.
            t_row.Bind(t_definition, this.HandleClaim);
            _rows.Add(t_row);
        }

        if (_emptyNotice != null) _emptyNotice.SetActive(_rows.Count == 0);
    }

    void RefreshRows()
    {
        for (int i = 0; i < this.m_dailyRows.Count; i++)
            if (this.m_dailyRows[i] != null) this.m_dailyRows[i].Refresh();
        for (int i = 0; i < this.m_weeklyRows.Count; i++)
            if (this.m_weeklyRows[i] != null) this.m_weeklyRows[i].Refresh();
    }

    // 정의 목록의 신원. id 와 순서가 그대로면 다시 깔 이유가 없다.
    static string BuildSignature()
    {
        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        if (t_definitions.Count == 0) return string.Empty;

        var t_builder = new System.Text.StringBuilder(t_definitions.Count * 24);
        for (int i = 0; i < t_definitions.Count; i++) t_builder.Append(t_definitions[i].Id).Append('|');
        return t_builder.ToString();
    }

    void HandleClaim(string _missionId)
    {
        this.ClaimAsync(_missionId).Forget();
    }

    async UniTaskVoid ClaimAsync(string _missionId)
    {
        // 왕복 동안 입력을 막는다. 딤·스피너는 임계 뒤에만 뜨므로 빠른 응답에서는 깜빡이지 않는다.
        ServerWaitOverlay.Hold(this);
        try
        {
            // 진행도·낙인은 낙관 갱신하지 않는다 — 응답 봉투를 ServerSaveCommands 가 중앙에서 채택하고,
            // 그 채택이 OnChanged 를 태워 화면이 갱신된다. 실패하면 화면은 그대로 남는다.
            await MissionCommands.ClaimAsync(_missionId);
        }
        finally
        {
            // **팝업보다 먼저 걷는다.** 순서를 뒤집으면 안내가 대기 딤에 묻힌다
            // (PackPurchaseFlow 와 같은 계약 — ServerWaitOverlay 는 자기 캔버스가 없다).
            ServerWaitOverlay.Release(this);
        }
    }

    // 리셋 시각의 진실원은 서버가 준 epoch ms 다. 남은 시간 표시에만 기기 시계를 쓴다 —
    // 실제 리셋 판정은 서버가 하므로 시계를 돌려도 미션이 앞당겨 열리지는 않는다.
    void RefreshResetLabels()
    {
        long t_nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SetResetLabel(this.dailyResetText, MissionManager.DailyResetAtMs, t_nowMs);
        SetResetLabel(this.weeklyResetText, MissionManager.WeeklyResetAtMs, t_nowMs);
    }

    static void SetResetLabel(TMP_Text _label, long _resetAtMs, long _nowMs)
    {
        if (_label == null) return;
        if (_resetAtMs <= 0)
        {
            // 아직 한 번도 조회하지 못한 상태. "0시간 남음"으로 거짓말하지 않는다.
            _label.text = string.Empty;
            return;
        }

        long t_remainMs = _resetAtMs - _nowMs;
        if (t_remainMs < 0) t_remainMs = 0;

        var t_span = TimeSpan.FromMilliseconds(t_remainMs);
        _label.text = t_span.TotalDays >= 1d
            ? $"{(int)t_span.TotalDays}일 {t_span.Hours}시간 남음"
            : $"{(int)t_span.TotalHours}시간 {t_span.Minutes}분 남음";
    }

    // 여는 순간 오버레이 자신을 켠다 — 저작본은 루트가 꺼진 채로 들어오므로, 켜 주지 않으면
    // 하위 Root 만 토글돼 화면에 아무것도 뜨지 않는다(RankRewardPanel 과 같은 규약).
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
