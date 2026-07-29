using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class SceneTransitionVideo : MonoBehaviour
{
    public static SceneTransitionVideo Instance { get; private set; }

    [SerializeField] VideoPlayer videoPlayer;
    [SerializeField] RawImage    rawImage;
    [SerializeField] VideoClip   toBattleClip;

    public bool IsPlaying { get; private set; }

    RenderTexture renderTexture;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(transform.root.gameObject);   // 부트 프리팹의 자식이라 루트 기준(단독 배치면 자기 자신)
        gameObject.SetActive(false);
    }

    /// <summary>영상 재생 시작 (fire-and-forget). 씬 전환과 동시 진행용.</summary>
    public void PlayOverlay()
    {
        if (this.toBattleClip == null) return;
        PlayAsync().Forget();
    }

    async UniTaskVoid PlayAsync()
    {
        this.IsPlaying = true;
        gameObject.SetActive(true);

        if (this.renderTexture == null
            || this.renderTexture.width  != Screen.width
            || this.renderTexture.height != Screen.height)
        {
            this.renderTexture?.Release();
            this.renderTexture = new RenderTexture(Screen.width, Screen.height, 0);
            this.videoPlayer.targetTexture = this.renderTexture;
            this.rawImage.texture          = this.renderTexture;
        }

        SoundManager.Instance?.StopBGM();

        this.videoPlayer.clip = this.toBattleClip;

        var t_tcs = new UniTaskCompletionSource();
        void OnEnd(VideoPlayer _vp) { _vp.loopPointReached -= OnEnd; t_tcs.TrySetResult(); }
        this.videoPlayer.loopPointReached += OnEnd;
        this.videoPlayer.Play();

        await t_tcs.Task;

        this.IsPlaying = false;
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        this.renderTexture?.Release();
    }
}
