#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

static class OutgameDebugInputLock
{
    static readonly HashSet<object> s_owners = new HashSet<object>();
    static EventSystem s_events;
    // 잠그기 전 상태. 무조건 true로 복원하면 게임이 원래 꺼둔 EventSystem을 켜 버린다.
    static bool s_wasEnabled;

    public static void Acquire(object _owner)
    {
        if (_owner == null || !s_owners.Add(_owner)) return;
        if (s_owners.Count > 1) return;

        Lock(EventSystem.current);
    }

    public static void Release(object _owner)
    {
        if (_owner == null || !s_owners.Remove(_owner) || s_owners.Count > 0) return;

        Unlock();
    }

    /// <summary>씬 전환 등으로 EventSystem이 새로 생기면 그쪽도 잠근다.
    ///
    /// **null은 "바뀌었다"가 아니라 "이미 잠겼다"로 읽어야 한다** — `enabled = false`로 끈 EventSystem은
    /// `EventSystem.current`에서 빠지기 때문이다. 여기서 s_events를 null로 덮으면 복원 대상을 잃고,
    /// 패널을 닫아도 EventSystem이 꺼진 채 남아 화면 입력이 영구히 죽는다.</summary>
    public static void Refresh()
    {
        if (s_owners.Count == 0) return;

        EventSystem t_events = EventSystem.current;
        if (t_events == null || t_events == s_events) return;

        Unlock();
        Lock(t_events);
    }

    static void Lock(EventSystem _events)
    {
        s_events = _events;
        if (s_events == null) return;

        s_wasEnabled     = s_events.enabled;
        s_events.enabled = false;
    }

    static void Unlock()
    {
        if (s_events != null && s_wasEnabled) s_events.enabled = true;

        s_events     = null;
        s_wasEnabled = false;
    }
}

// 런타임 아웃게임 디버그 패널 (배선 없이 자동 생성, 우상단 [DEBUG] 또는 F8)
public class OutgameDebugOverlay : MonoBehaviour
{
    const KeyCode TOGGLE_KEY = KeyCode.F8;

    const float REFERENCE_HEIGHT = 900f;

    const float PANEL_WIDTH   = 190f;
    const float ROW_HEIGHT    = 26f;
    const float CLOSED_HEIGHT = 30f;
    // 열었을 때 쓸 최대 높이. 화면이 더 짧으면 그만큼만 쓴다.
    // 내용이 넘치면 잘리지 않고 스크롤된다 — 고정 높이만 두면 줄을 추가할 때 아래가 조용히 사라진다(실제로 그랬음).
    const float OPENED_HEIGHT = 460f;

    static OutgameDebugOverlay s_instance;

    bool m_open;

    // 패널이 화면보다 길어졌을 때의 스크롤 위치.
    Vector2 m_scroll;

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
        OutgameDebugInputLock.Refresh();

        float      t_scale = Mathf.Max(1f, Screen.height / REFERENCE_HEIGHT);
        Matrix4x4  t_prev  = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(t_scale, t_scale, 1f));

        float t_right = Screen.width / t_scale;
        // 화면 밖으로 나가지 않게 상하 여백을 뺀 값으로 제한한다(세로가 짧은 기기 대응).
        float t_maxHeight = Mathf.Max(CLOSED_HEIGHT, Screen.height / t_scale - 16f);
        float t_height    = m_open ? Mathf.Min(OPENED_HEIGHT, t_maxHeight) : CLOSED_HEIGHT;
        var   t_area      = new Rect(t_right - PANEL_WIDTH - 8f, 8f, PANEL_WIDTH, t_height);

        GUILayout.BeginArea(t_area, GUI.skin.box);

        // 라벨은 ASCII만 — IMGUI 기본 폰트에 한글 글리프가 없어 □로 깨진다
        if (GUILayout.Button(m_open ? "CLOSE (F8)" : "DEBUG (F8)", GUILayout.Height(22f))) SetOpen(!m_open);
        if (m_open) DrawBody();

        GUILayout.EndArea();

        GUI.matrix = t_prev;
    }

    void DrawBody()
    {
        // 스크롤로 감싼다 — 버튼을 하나 더 붙였을 때 아래가 잘려 "버튼이 없다"가 되지 않게.
        m_scroll = GUILayout.BeginScrollView(m_scroll);

        GUILayout.Label($"OWNED {OwnershipManager.OwnedCount} / {CardCatalog.Count}");

        if (GUILayout.Button("UNLOCK ALL CARDS", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.UnlockAllCards();
        if (GUILayout.Button("REVOKE ALL CARDS", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.RevokeAllCards();
        if (GUILayout.Button("SKIP TUTORIAL",    GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.SkipTutorial();
        if (GUILayout.Button("RESET TUTORIAL",   GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.ResetTutorial();
        if (GUILayout.Button("RESET TRIGGERS",   GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.ResetTriggeredTutorials();
        if (GUILayout.Button(OutgameFeatureLock.ForceUnlockAllForDebug ? "FEATURE LOCK: OFF" : "FEATURE LOCK: ON",
                                                 GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.ToggleFeatureLock();
        if (GUILayout.Button("MAX GROWTH (ALL)", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.MaxCardGrowth();
        if (GUILayout.Button("RESET GROWTH",     GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.ResetCardGrowth();
        if (GUILayout.Button("LOG OWNERSHIP",    GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.LogOwnership();
        if (GUILayout.Button("ALBUM INSERT x3",  GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.ForceAlbumInsertSession(3);
        if (GUILayout.Button("TOURNAMENT NODE",  GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.StartCurrentTournamentNode();

        DrawRarityPackControls();
        DrawCurrencyGrants();
        DrawTierControls();
        DrawChapterJumps();

        GUILayout.EndScrollView();
    }

    void DrawRarityPackControls()
    {
        ECardGrade t_grade = ECardGrade.Unknown;

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("RARE",   GUILayout.Height(ROW_HEIGHT))) t_grade = ECardGrade.Rare;
        if (GUILayout.Button("ARCANE", GUILayout.Height(ROW_HEIGHT))) t_grade = ECardGrade.Arcane;
        if (GUILayout.Button("MYTHIC", GUILayout.Height(ROW_HEIGHT))) t_grade = ECardGrade.Mythic;
        GUILayout.EndHorizontal();

        if (t_grade == ECardGrade.Unknown) return;
        SetOpen(false);
        OutgameDebugActions.OpenRarityTestPack(t_grade);
    }

    // 티어는 AI 카드 레벨의 입력이라 난이도 확인용으로 위아래 이동을 같이 둔다.
    void DrawTierControls()
    {
        RankInfo t_info = RankManager.GetInfo();

        string t_promo = RankManager.IsPromoPending ? "  [승급전]" : string.Empty;
        GUILayout.Label($"TIER {t_info.DisplayName}{t_promo}  (AI {GrowthStar.Label(RankManager.AiCardLevel)})");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("TIER -", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.LowerTier();
        if (GUILayout.Button("TIER +", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.RaiseTier();
        if (GUILayout.Button("PROMO", GUILayout.Height(ROW_HEIGHT)))  OutgameDebugActions.JumpToPromoStandby();
        if (GUILayout.Button("RESET", GUILayout.Height(ROW_HEIGHT)))  OutgameDebugActions.ResetTier();
        GUILayout.EndHorizontal();
    }

    void DrawCurrencyGrants()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"+G {CurrencyManager.Gold}",    GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.GrantGold();
        if (GUILayout.Button($"+D {CurrencyManager.Diamond}", GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.GrantDiamond();
        if (GUILayout.Button($"+E {CurrencyManager.Energy}",  GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.GrantEnergy();
        if (GUILayout.Button($"+S {CurrencyManager.Shard}",   GUILayout.Height(ROW_HEIGHT))) OutgameDebugActions.GrantShard();
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
        if (_open) OutgameDebugInputLock.Acquire(this);
        else OutgameDebugInputLock.Release(this);
    }
}
#endif
