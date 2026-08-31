using TMPro;
using UnityEngine;

/// <summary>클라우드 세이브 동기화가 밀리고 있다는 것을 알리는 상시 배너. 표시 판정은 하지 않는다 —
/// 열고 닫는 명령만 CloudSyncStatusWatcher에게서 받는다.</summary>
public class CloudSyncBannerView : SingletonOverlayBase
{
    static CloudSyncBannerView s_instance;

    [SerializeField] TMP_Text messageText;
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] Canvas overlayCanvas;

    const string MESSAGE = "저장 동기화가 지연되고 있습니다. 연결을 확인해 주세요.";

    /// <summary>배너를 띄우거나 내린다. 내리는 요청인데 아직 인스턴스가 없으면 프리팹을 만들지 않는다.</summary>
    internal static void SetVisible(bool _visible)
    {
        if (!_visible)
        {
            if (s_instance != null) s_instance.Apply(false);
            return;
        }

        if (!TryGetOrCreate(out CloudSyncBannerView t_banner)) return;
        t_banner.Apply(true);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_instance = null;
    }

    static bool TryGetOrCreate(out CloudSyncBannerView _banner)
    {
        if (s_instance != null)
        {
            _banner = s_instance;
            return true;
        }

        GameObject t_prefab = RuntimeOverlayPrefabs.Get<CloudSyncBannerView>();
        if (t_prefab == null)
        {
            _banner = null;
            return false;
        }

        GameObject t_object = Instantiate(t_prefab);
        s_instance = t_object.GetComponent<CloudSyncBannerView>();
        if (s_instance == null)
        {
            Debug.LogError($"[CloudSyncBannerView] {t_prefab.name} 루트에 CloudSyncBannerView가 없습니다.", t_prefab);
            Destroy(t_object);
            _banner = null;
            return false;
        }

        DontDestroyOnLoad(t_object);
        s_instance.Bind();

        _banner = s_instance;
        return true;
    }

    void Bind()
    {
        if (this.overlayCanvas == null) this.overlayCanvas = GetComponent<Canvas>();
        if (this.canvasGroup == null) this.canvasGroup = GetComponent<CanvasGroup>();

        if (this.overlayCanvas == null || this.canvasGroup == null)
            Debug.LogError("[CloudSyncBannerView] 프리팹 루트에 Canvas·CanvasGroup이 필요합니다.", this);

        UiSortingOrder.Stamp(this.overlayCanvas, UiSortingOrder.CloudSyncBanner);

        if (this.canvasGroup != null)
        {
            // 배너는 알림일 뿐이라 그 밑의 게임 입력을 먹으면 안 된다.
            this.canvasGroup.blocksRaycasts = false;
            this.canvasGroup.interactable = false;
        }

        if (this.messageText != null) this.messageText.text = MESSAGE;
    }

    void Apply(bool _visible)
    {
        this.gameObject.SetActive(_visible);
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }
}
