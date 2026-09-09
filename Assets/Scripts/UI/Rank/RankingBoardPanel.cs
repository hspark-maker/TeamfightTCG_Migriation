using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 서버 시즌 랭킹의 표시 창구. 풀 재사용 중 이전 요청의 응답은 버린다.
public class RankingBoardPanel : PooledUIBase
{
    [SerializeField] GameObject root;
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] RectTransform content;
    [SerializeField] RankingRowView rowPrefab;
    [SerializeField] RankingRowView[] podiumRows;
    [SerializeField] RankingRowView selfRow;
    [SerializeField] TMP_Text seasonText;
    [SerializeField] TMP_Text statusText;
    [SerializeField] Button retryButton;
    [SerializeField] Button closeButton;
    [SerializeField] PopupTransition transition = new PopupTransition();
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.72f;

    readonly List<RankingRowView> m_rows = new List<RankingRowView>();
    int m_requestVersion;
    bool m_loading;
    long m_endAtMs;
    float m_nextClockUpdate;

    protected override void Awake()
    {
        base.Awake();
        UiSortingOrder.LiftNested(gameObject, UiSortingOrder.PooledOverlay);
        ClearRows();
    }

    public override void Initialization(UIData _data) { }
    public override void Show() => Open();
    public override void Hide() => Close();

    public void Open()
    {
        SetVisible(true);
        Refresh();
    }

    public void Close()
    {
        ++m_requestVersion;
        m_loading = false;
        SetVisible(false);
    }

    void OnEnable()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (retryButton != null) retryButton.onClick.AddListener(Refresh);
    }

    void OnDisable()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (retryButton != null) retryButton.onClick.RemoveListener(Refresh);
        ++m_requestVersion;
        m_loading = false;
        isShow = false;
        ScreenDim.Hide(this);
        transition.HandleDisabled(ResolveTarget());
    }

    void Refresh()
    {
        if (m_loading) return;
        m_loading = true;
        m_endAtMs = 0;
        if (seasonText != null) seasonText.text = string.Empty;
        ClearRows();
        SetStatus("랭킹을 불러오는 중…", false);
        LoadAsync(++m_requestVersion).Forget();
    }

    async UniTaskVoid LoadAsync(int _version)
    {
        try
        {
            var t_result = await ServerSaveCommands.InvokeReadOnlyAsync<RankLeaderboardResult>(
                "getRankLeaderboard", new { env = ContentProfileConfig.Active.CloudEnvId });
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[RankingBoardPanel] Received env={ContentProfileConfig.Active.CloudEnvId}, entries={t_result?.Entries?.Length}\n"
                + Newtonsoft.Json.JsonConvert.SerializeObject(t_result, Newtonsoft.Json.Formatting.Indented));
#endif
            if (this == null || !isShow || _version != m_requestVersion) return;
            if (t_result?.Entries == null || t_result.Self == null || t_result.Season == null)
                throw new InvalidOperationException("Ranking response is incomplete.");
            m_endAtMs = t_result.Season.EndAtMs;
            UpdateClock();
            var t_entries = t_result.Entries;
            float t_height = ((RectTransform)rowPrefab.transform).rect.height;
            const float t_spacing = 13f;
            for (int t_i = 0; t_i < t_entries.Length; t_i++)
            {
                RankingRowView t_row;
                if (podiumRows != null && t_i < podiumRows.Length)
                    t_row = podiumRows[t_i];
                else
                {
                    int t_index = t_i - (podiumRows?.Length ?? 0);
                    if (t_index >= m_rows.Count) m_rows.Add(Instantiate(rowPrefab, content));
                    t_row = m_rows[t_index];
                }
                t_row.gameObject.SetActive(true);
                var t_rect = (RectTransform)t_row.transform;
                t_rect.anchorMin = t_rect.anchorMax = new Vector2(0.5f, 1f);
                t_rect.pivot = new Vector2(0.5f, 0.5f);
                t_rect.anchoredPosition = new Vector2(0f, -t_height / 2f - t_i * (t_height + t_spacing));
                t_row.Bind(t_entries[t_i]);
            }
            if (selfRow != null)
            {
                selfRow.gameObject.SetActive(true);
                selfRow.Bind(t_result.Self);
            }
            if (content != null)
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(0, t_entries.Length * (t_height + t_spacing) - t_spacing));
            if (scrollRect != null)
            {
                scrollRect.StopMovement();
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 1f;
            }
            SetStatus(t_entries.Length == 0 ? "이번 시즌 랭킹이 없습니다." : null, false);
        }
        catch (Exception t_error)
        {
            if (this == null || !isShow || _version != m_requestVersion) return;
            Debug.LogWarning($"[RankingBoardPanel] Ranking request failed: {t_error.GetBaseException().Message}");
            ClearRows();
            SetStatus("랭킹을 불러오지 못했습니다.\n눌러서 다시 시도", true);
        }
        finally
        {
            if (this != null && _version == m_requestVersion) m_loading = false;
        }
    }

    void ClearRows()
    {
        if (rowPrefab != null) rowPrefab.gameObject.SetActive(false);
        if (podiumRows != null)
            foreach (var t_row in podiumRows)
                if (t_row != null) t_row.gameObject.SetActive(false);
        foreach (var t_row in m_rows) t_row.gameObject.SetActive(false);
        if (selfRow != null) selfRow.gameObject.SetActive(false);
    }

    void SetStatus(string _message, bool _retry)
    {
        if (statusText != null)
        {
            statusText.text = _message ?? string.Empty;
            statusText.gameObject.SetActive(!string.IsNullOrEmpty(_message));
        }
        if (retryButton != null) retryButton.interactable = _retry;
    }

    void Update()
    {
        if (!isShow || m_endAtMs <= 0 || Time.unscaledTime < m_nextClockUpdate) return;
        m_nextClockUpdate = Time.unscaledTime + 1f;
        UpdateClock();
    }

    void UpdateClock()
    {
        if (seasonText == null || m_endAtMs <= 0) return;
        double t_hours = (m_endAtMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 3600000d;
        seasonText.text = t_hours <= 0 ? "시즌 종료"
            : t_hours > 24 ? $"종료까지 {Math.Ceiling(t_hours / 24):N0}일"
            : $"종료까지 {Math.Ceiling(t_hours):N0}시간";
    }

    void SetVisible(bool _visible)
    {
        if (_visible && !gameObject.activeSelf) gameObject.SetActive(true);
        if (_visible) ScreenDim.Show(this, dimAlpha, true, transition.OpenDuration);
        else ScreenDim.Hide(this);
        isShow = _visible;
        transition.SetVisible(ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => root != null ? root : gameObject;
}
