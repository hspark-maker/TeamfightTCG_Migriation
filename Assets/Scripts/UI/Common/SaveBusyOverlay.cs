using UnityEngine;
using UnityEngine.UI;

// 세이브 커밋이 끝날 때까지 화면 전체의 입력을 막는 대기 표시.
// 커밋이 네트워크로 나가면서 지연이 실재하게 됐고, 그동안 같은 화면을 한 번 더 눌러
// "화면엔 반영됐는데 서버엔 없는" 상태가 겹쳐 쌓이는 것을 막는 것이 목적이다 — 로딩 연출이 목적이 아니다.
//
// ⚠ 연출 때문에 저장이 막히면 안 된다. 프리팹을 얻지 못해도 경고 한 줄만 남기고 커밋은 그대로 진행된다.
//
// 커밋이 씬 전환과 겹칠 수 있어 DontDestroyOnLoad로 살아남는다.
// 그 대가로 어떻게 빠져나가든 반드시 걷혀야 한다 — 남으면 이후 모든 화면이 영구 입력 불가가 된다
// (LoadingCoverView와 같은 계약이라, 거는 쪽인 SaveTransaction이 finally에서 Hide를 보장한다).
public sealed class SaveBusyOverlay : SingletonOverlay<SaveBusyOverlay>
{
    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    // 겹친 커밋 수. 0이 될 때만 걷는다 — 안쪽 커밋이 먼저 끝났다고 걷으면 바깥 커밋이 무방비로 남는다.
    static int s_holdCount;

    // 프리팹을 못 얻은 상태. 매 커밋마다 다시 시도하면 경고가 쌓일 뿐 아니라
    // 에디터 폴백이 Addressables 동기 로드(WaitForCompletion)를 커밋마다 한 번씩 돌린다.
    static bool s_prefabUnavailable;
    static bool s_unavailableWarned;

    CanvasGroup m_group;

    /// <summary>대기 표시를 세운다. 겹쳐 불러도 되며, 부른 횟수만큼 <see cref="Hide"/>해야 걷힌다.</summary>
    public static void Show()
    {
        s_holdCount++;
        if (IsOpen) return;
        if (s_prefabUnavailable) return;

        if (!TryGetOrCreate(RuntimeOverlayPrefabs.Get<SaveBusyOverlay>, out SaveBusyOverlay t_overlay))
        {
            WarnUnavailableOnce();
            return;
        }

        IsOpen = true;
        t_overlay.SetVisible(true);
    }

    /// <summary>대기 표시를 하나 놓는다. 마지막 하나가 놓일 때 걷힌다.</summary>
    public static void Hide()
    {
        if (s_holdCount == 0) return;

        s_holdCount--;
        if (s_holdCount > 0) return;

        IsOpen = false;
        if (TryGetExisting(out SaveBusyOverlay t_overlay)) t_overlay.SetVisible(false);
    }

    void Awake()
    {
        // 커밋 중에 무대가 갈려도 입력 차단은 끝까지 남아야 한다.
        if (transform.parent == null) DontDestroyOnLoad(gameObject);

        this.m_group = GetComponent<CanvasGroup>();

        UiSortingOrder.Stamp(GetComponent<Canvas>(), UiSortingOrder.SaveBusy);
        WarnIfBlockingUnwired();
    }

    void SetVisible(bool _visible)
    {
        GameObject t_target = this.root != null ? this.root : gameObject;
        if (t_target.activeSelf != _visible) t_target.SetActive(_visible);

        if (this.m_group != null) this.m_group.blocksRaycasts = _visible;
    }

    // 이 화면의 존재 이유가 입력 차단이라, 막을 수단이 빠진 채 뜨면 겉보기만 대기 상태가 된다.
    void WarnIfBlockingUnwired()
    {
        if (GetComponent<Canvas>() == null)
            Debug.LogWarning("[SaveBusyOverlay] 루트에 Canvas가 없어 층(UiSortingOrder.SaveBusy)을 찍지 못한다.", this);

        if (GetComponent<GraphicRaycaster>() == null)
            Debug.LogWarning("[SaveBusyOverlay] 루트에 GraphicRaycaster가 없어 아래 화면의 탭이 그대로 통과한다.", this);

        if (this.m_group == null)
            Debug.LogWarning("[SaveBusyOverlay] 루트에 CanvasGroup이 없어 blocksRaycasts로 입력을 막을 수 없다.", this);
    }

    // 부트 전이면 타입 색인이 아직 없을 뿐이라 다음 커밋에 다시 시도한다.
    static void WarnUnavailableOnce()
    {
        if (DataLibrary.instance != null) s_prefabUnavailable = true;

        if (s_unavailableWarned) return;
        s_unavailableWarned = true;

        Debug.LogWarning("[SaveBusyOverlay] 프리팹을 얻지 못해 저장 대기 표시를 건너뛴다(저장 자체는 그대로 진행된다).");
    }
}
