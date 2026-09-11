using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 가이드 미션 화면(GuideMissionOverlay 에 부착). 풀(UIPoolManager)이 수명을 쥔다 —
/// 규약은 <see cref="MissionPanel"/> 과 같다(캔버스 기준 해상도 1080x1920 주의 포함).
///
/// <para>일일·주간과 달리 리셋이 없는 1회성 순차 미션만 그린다. 순서 해금 판정은
/// <see cref="MissionManager.IsGuideUnlocked"/> 하나다 — 화면이 자기 순서 판정을 가지면
/// 보이는 잠금과 서버 거절 조건이 갈린다. 잠긴 줄도 숨기지 않고 그린다(다음 목표가 보여야
/// 안내가 된다) — 수령 버튼은 CanClaim 이 알아서 죽인다.</para>
/// </summary>
public class GuideMissionPanel : PooledUIBase
{
    // 풀 계약. 표시 데이터는 MissionManager 에서 스스로 당기므로 UIData 가 필요 없다.
    public override void Initialization(UIData _data) { }

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    [Tooltip("켜고 끌 대상(딤 + 패널). 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [Header("목록")]
    [Tooltip("가이드 미션 행이 쌓일 Content(VerticalLayoutGroup).")]
    [SerializeField] Transform listContent;

    [Tooltip("가이드·일일·주간이 공유하는 미션 행 프리팹 에셋.")]
    [SerializeField] MissionRowView rowPrefab;

    [Tooltip("가이드 미션이 하나도 없을 때 켤 안내(서버 정의 미도착 포함).")]
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

    readonly List<MissionRowView> m_rows = new List<MissionRowView>();
    Action<string> m_claimHandler;
    Action<string> m_navigateHandler;

    const string PERIOD_GUIDE = "guide";

    // 씬 버튼 UnityEvent 가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.
    public void Open()
    {
        this.SetVisible(true);
        this.Rebuild();

        // 초기화 요청을 공유하고, 상태·기간·저장 버전이 유효하면 최근 조회를 재사용한다.
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

    void HandleMissionsChanged() => this.Rebuild();

    void Rebuild()
    {
        if (this.listContent == null || this.rowPrefab == null) return;

        if (this.rowPrefab.transform.parent == this.listContent) this.rowPrefab.gameObject.SetActive(false);

        int t_count = 0;
        this.m_claimHandler ??= this.HandleClaim;
        this.m_navigateHandler ??= this.HandleNavigate;
        IReadOnlyList<MissionDefinition> t_definitions = MissionManager.Definitions;
        for (int i = 0; i < t_definitions.Count; i++)
        {
            MissionDefinition t_definition = t_definitions[i];
            if (!string.Equals(t_definition.Period, PERIOD_GUIDE, StringComparison.Ordinal)) continue;

            if (t_count == this.m_rows.Count) this.m_rows.Add(null);
            MissionRowView t_row = this.m_rows[t_count];
            if (t_row == null) this.m_rows[t_count] = t_row = Instantiate(this.rowPrefab, this.listContent);
            t_row.Bind(t_definition, this.m_claimHandler,
                MissionContentNavigation.HasDestination(t_definition) ? this.m_navigateHandler : null);
            if (!t_row.gameObject.activeSelf) t_row.gameObject.SetActive(true);
            t_count++;
        }

        for (int i = t_count; i < this.m_rows.Count; i++)
            if (this.m_rows[i] != null && this.m_rows[i].gameObject.activeSelf) this.m_rows[i].gameObject.SetActive(false);
        if (this.emptyNotice != null) this.emptyNotice.SetActive(t_count == 0);
    }

    void HandleClaim(string _missionId)
    {
        this.ClaimAsync(_missionId).Forget();
    }

    void HandleNavigate(string _missionId)
    {
        if (!this.isShow) return;
        MissionContentNavigation.TryNavigate(MissionManager.Find(_missionId), this.Close);
    }

    async UniTaskVoid ClaimAsync(string _missionId)
    {
        // 왕복 동안 입력을 막는다. 진행도·낙인은 낙관 갱신하지 않는다(MissionPanel 과 같은 계약).
        ClaimMissionResult t_result = null;
        ServerWaitOverlay.Hold(this);
        try
        {
            t_result = await MissionCommands.ClaimAsync(_missionId);
        }
        finally
        {
            // **팝업보다 먼저 걷는다.** 순서를 뒤집으면 안내가 대기 딤에 묻힌다.
            ServerWaitOverlay.Release(this);
        }
        if (t_result != null)
        {
            if ((t_result.Cards?.Count ?? 0) > 0) this.Close();
            MissionPanel.ShowClaimedRewards(new[] { t_result });
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
