#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.EventSystems;

// 런타임 아웃게임 디버그 패널 (배선 없이 자동 생성, 우상단 [DEBUG] 또는 F8)
public class OutgameDebugOverlay : MonoBehaviour
{
    const KeyCode TOGGLE_KEY = KeyCode.F8;

    const float REFERENCE_HEIGHT = 900f;

    const float PANEL_WIDTH   = 190f;
    const float ROW_HEIGHT    = 26f;
    const float CLOSED_HEIGHT = 30f;
    const float OPENED_HEIGHT = 326f;

    static OutgameDebugOverlay s_instance;

    bool m_open;

    EventSystem m_lockedEvents;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (s_instance != null) return;

        var t_go = new GameObject("[OutgameDebugOverlay]");
        DontDestroyOnLoad(t_go);
        s_instance = t_go.AddComponent<OutgameDebugOverlay>();
    }

    void OnDisable()
    {
        SetOpen(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(TOGGLE_KEY)) SetOpen(!m_open);
    }

    void OnGUI()
    {
        float      t_scale = Mathf.Max(1f, Screen.height / REFERENCE_HEIGHT);
        Matrix4x4  t_prev  = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(t_scale, t_scale, 1f));

        float t_right = Screen.width / t_scale;
        var   t_area  = new Rect(t_right - PANEL_WIDTH - 8f, 8f, PANEL_WIDTH, m_open ? OPENED_HEIGHT : CLOSED_HEIGHT);

        GUILayout.BeginArea(t_area, GUI.skin.box);

        // 라벨은 ASCII만 — IMGUI 기본 폰트에 한글 글리프가 없어 □로 깨진다
        if (GUILayout.Button(m_open ? "CLOSE (F8)" : "DEBUG (F8)", GUILayout.Height(22f))) SetOpen(!m_open);
        if (m_open) DrawBody();

        GUILayout.EndArea();

        GUI.matrix = t_prev;
    }

    void DrawBody()
    {
        GUILayout.Label($"OWNED {OwnershipManager.OwnedCount} / {CardCatalog.Count}");

        if (GUILayout.Button("UNLOCK ALL CARDS", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.UnlockAllCards();
        if (GUILayout.Button("REVOKE ALL CARDS", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.RevokeAllCards();
        if (GUILayout.Button("SKIP TUTORIAL",    GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.SkipTutorial();
        if (GUILayout.Button("RESET TUTORIAL",   GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.ResetTutorial();
        if (GUILayout.Button("RESET TRIGGERS",   GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.ResetTriggeredTutorials();
        if (GUILayout.Button(OutgameFeatureLock.ForceUnlockAllForDebug ? "FEATURE LOCK: OFF" : "FEATURE LOCK: ON",
                                                 GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.ToggleFeatureLock();
        if (GUILayout.Button("RESET GROWTH",     GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.ResetCardGrowth();
        if (GUILayout.Button("LOG OWNERSHIP",    GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.LogOwnership();

        DrawCurrencyGrants();
        DrawTierControls();
        DrawChapterJumps();
    }

    // 티어는 AI 카드 레벨의 입력이라 난이도 확인용으로 위아래 이동을 같이 둔다.
    void DrawTierControls()
    {
        RankInfo t_info = RankManager.GetInfo();

        GUILayout.Label($"TIER {t_info.DisplayName}  (AI Lv{RankManager.AiCardLevel})");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("TIER -", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.LowerTier();
        if (GUILayout.Button("TIER +", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.RaiseTier();
        if (GUILayout.Button("RESET", GUILayout.Height(ROW_HEIGHT)))  OutgameDebugActions.ResetTier();
        GUILayout.EndHorizontal();
    }

    void DrawCurrencyGrants()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"+G {CurrencyManager.Gold}",    GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.GrantGold();
        if (GUILayout.Button($"+D {CurrencyManager.Diamond}", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.GrantDiamond();
        GUILayout.EndHorizontal();
    }

    void DrawChapterJumps()
    {
        int t_count = OutgameTutorialRunner.ChapterCount;
        if (t_count <= 0) return;

        GUILayout.BeginHorizontal();
        for (int i = 0; i < t_count; i++)
        {
            if (GUILayout.Button($"CH{i + 1}", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.RestartTutorialFromChapter(i);
        }
        GUILayout.EndHorizontal();
    }

    void SetOpen(bool _open)
    {
        m_open = _open;

        if (_open)
        {
            m_lockedEvents = EventSystem.current;
            if (m_lockedEvents != null) m_lockedEvents.enabled = false;
            return;
        }

        if (m_lockedEvents != null) m_lockedEvents.enabled = true;
        m_lockedEvents = null;
    }
}
#endif
