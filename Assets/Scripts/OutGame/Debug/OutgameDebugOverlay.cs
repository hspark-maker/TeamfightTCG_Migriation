#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.EventSystems;

// 런타임 아웃게임 디버그 패널. 씬·프리팹 배선 없이 스스로 뜬다(우상단 [DEBUG] 버튼 또는 F8).
// 파일 전체를 #if로 감싼다 — 릴리스 빌드에는 클래스 자체가 존재하지 않아 새어나갈 경로가 없다.
//
// uGUI가 아니라 IMGUI를 쓰는 이유: 배선이 필요 없고 ScreenSpaceOverlay 캔버스 위에 그려져
// 로비·덱 편집·전투 어느 화면에서도 같은 방식으로 열린다(씬마다 패널을 심을 필요가 없다).
public class OutgameDebugOverlay : MonoBehaviour
{
    const KeyCode TOGGLE_KEY = KeyCode.F8;

    // 1080p를 기준 배율 1로 두고 해상도에 따라 키운다(기본 IMGUI 크기는 모바일 해상도에서 손톱만해진다).
    const float REFERENCE_HEIGHT = 900f;

    const float PANEL_WIDTH   = 190f;
    const float ROW_HEIGHT    = 26f;
    const float CLOSED_HEIGHT = 30f;
    const float OPENED_HEIGHT = 300f;   // 재화 지급·편 점프 각 한 줄 포함

    static OutgameDebugOverlay s_instance;

    bool m_open;

    // 패널을 열 때 잠근 EventSystem. 씬 전환으로 파괴될 수 있으므로 복구 시 null 검사한다.
    EventSystem m_lockedEvents;

    // 씬에 아무것도 심지 않아도 뜨게 하는 유일한 진입점. 부트 프리팹 배선에 기대지 않는다(테스트 씬 단독 Play 포함).
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
        SetOpen(false);   // 잠근 입력을 들고 죽지 않게
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

        // 배율을 GUI.matrix로 주므로 좌표계 폭도 배율로 나눠야 한다.
        float t_right = Screen.width / t_scale;
        var   t_area  = new Rect(t_right - PANEL_WIDTH - 8f, 8f, PANEL_WIDTH, m_open ? OPENED_HEIGHT : CLOSED_HEIGHT);

        GUILayout.BeginArea(t_area, GUI.skin.box);

        // 라벨은 ASCII만 쓴다 — IMGUI 기본 폰트에 한글 글리프가 없어 한글은 □로 깨진다.
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
        if (GUILayout.Button("LOG OWNERSHIP",    GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.LogOwnership();

        DrawCurrencyGrants();
        DrawChapterJumps();
    }

    // 강화·진화 비용 테스트용 재화 지급. 잔액은 CurrencyManager가 보여준다(지급 즉시 영속).
    void DrawCurrencyGrants()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"+G {CurrencyManager.Gold}",    GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.GrantGold();
        if (GUILayout.Button($"+D {CurrencyManager.Diamond}", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.GrantDiamond();
        GUILayout.EndHorizontal();
    }

    // 튜토리얼 N편 되감기. 저작된 편 수만큼 버튼을 자동 생성한다(에셋을 고쳐도 여기는 따라올 필요가 없다).
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

    // 열려 있는 동안 uGUI 입력을 잠근다 — IMGUI는 EventSystem 레이캐스트를 막지 못해
    // 패널 버튼 클릭이 그 밑에 깔린 uGUI(뒤로가기·편성 칸)까지 같이 때린다.
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
