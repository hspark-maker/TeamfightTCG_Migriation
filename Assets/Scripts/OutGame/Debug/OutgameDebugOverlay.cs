#if !DISABLE_SRDEBUGGER
using UnityEngine;

// F8은 에디터에서 SROptions+ 창, 플레이어에서 SRDebugger 옵션 탭으로 연결한다.
// 화면 표시·입력 차단은 SRDebugger, 전투 콜라이더 차단은 TurnState가 담당한다.
public class OutgameDebugOverlay : MonoBehaviour
{
    static OutgameDebugOverlay s_instance;
    SRDebugger.Services.IDebugService m_debugService;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Initialize()
    {
        if (s_instance != null || !SRDebugger.Settings.Instance.IsEnabled) return;
        SRDebug.Init();
        var t_go = new GameObject("[SROptionsInput]");
        DontDestroyOnLoad(t_go);
        s_instance = t_go.AddComponent<OutgameDebugOverlay>();
    }

    void OnEnable()
    {
        m_debugService = SRDebug.Instance;
        if (m_debugService == null) return;
        m_debugService.PanelVisibilityChanged += OnPanelVisibilityChanged;
        OnPanelVisibilityChanged(m_debugService.IsDebugPanelVisible);
    }

    void OnDisable()
    {
        // 종료 중에는 서비스 조회가 null을 반환할 수 있으므로 구독했던 인스턴스에서 해제한다.
        if (m_debugService != null) m_debugService.PanelVisibilityChanged -= OnPanelVisibilityChanged;
        m_debugService = null;
        TurnState.DebugUiBlocking = false;
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F8)) return;
#if UNITY_EDITOR
        if (SRDebug.Instance.IsDebugPanelVisible) SRDebug.Instance.HideDebugPanel();
        UnityEditor.EditorApplication.ExecuteMenuItem("Window/SRDebugger/SROptions Window (UXML)");
#else
        if (SRDebug.Instance.IsDebugPanelVisible) SRDebug.Instance.HideDebugPanel();
        else SRDebug.Instance.ShowDebugPanel(SRDebugger.DefaultTabs.Options);
#endif
    }

    static void OnPanelVisibilityChanged(bool _visible) => TurnState.DebugUiBlocking = _visible;
}
#endif
