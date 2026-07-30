using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 카드 컷씬 동영상 재생기. 클립을 받아 전체화면으로 틀고 끝날 때까지 대기시켜주는 "재생기"일 뿐,
/// 어떤 카드에 어떤 클립을 틀지(등급 판정·트리거)는 호출부 책임이다.
/// SceneTransitionVideo 패턴(VideoPlayer + 동적 RenderTexture + RawImage + TCS) 승계.
/// 전투 씬 안에만 사는 오브젝트라 DontDestroyOnLoad를 하지 않는다.
/// </summary>
public class CardCinematicPlayer : MonoBehaviour
{
    public static CardCinematicPlayer Instance { get; private set; }

    /// <summary>현재 컷씬이 화면에 떠 있는지(페이드 아웃 완료 전까지 true).</summary>
    public bool IsPlaying { get; private set; }

    [SerializeField] VideoPlayer videoPlayer;
    [SerializeField] RawImage    rawImage;      // 전체화면 RawImage
    [SerializeField] CanvasGroup group;         // 페이드 + 입력 차단(blocksRaycasts)

    [Header("Background / Skip")]
    // 배경: 영상이 화면비와 안 맞아 생기는 여백으로 전투 화면이 비쳐 보이지 않게 뒤를 덮는다.
    // rawImage보다 **앞선 형제/자식 순서**여야 영상 뒤에 깔린다(uGUI는 계층 순서가 곧 그리기 순서).
    [SerializeField] Graphic background;
    [SerializeField] Button    skipButton;
    // 버튼을 곧바로 띄우면 컷씬을 보기도 전에 눌러버린다 → 이 시간만큼 늦게 나타난다.
    [SerializeField] float     skipButtonDelay = 0.6f;

    [SerializeField] float fadeDuration = 0.2f;
    // 화면 아무 곳이나 눌러도 스킵할지. 기본은 끔 — 전투 중 오조작으로 컷씬이 날아가는 걸 막고,
    // 스킵은 명시적으로 버튼으로만 받는다(버튼은 이 값과 무관하게 항상 동작).
    [SerializeField] bool  allowSkip = false;

    /// <summary>컷씬을 띄운 그 탭이 그대로 스킵으로 먹히는 것을 막는 최소 입력 봉인 시간(초).</summary>
    const float SkipInputGuard = 0.25f;

    bool skipRequested;   // 스킵 버튼이 눌렸나(재생 1회마다 리셋)

    RenderTexture renderTexture;

    // 재진입 직렬화용 번호표(lock 없이 호출 순서 보존). nextTicket에서 뽑고 nowServing이 오면 내 차례.
    int nextTicket;
    int nowServing;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (this.videoPlayer != null)
        {
            // 씬 배치 실수 방어: 자동 재생·루프 금지, 출력은 RenderTexture 고정.
            this.videoPlayer.playOnAwake = false;
            this.videoPlayer.isLooping   = false;
            this.videoPlayer.renderMode  = VideoRenderMode.RenderTexture;
        }

        if (this.group != null)
        {
            this.group.alpha          = 0f;
            this.group.blocksRaycasts = false;
        }

        gameObject.SetActive(false);   // 시작은 비활성(SceneTransitionVideo와 동형)
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;   // 씬 재진입 시 죽은 참조 방지

        if (this.group != null) this.group.DOKill();
        ReleaseRenderTexture();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// 정적 파사드. Instance가 없거나 _clip이 null이면 즉시 완료된다(호출부가 널체크 안 하게).
    /// 컷씬 유무와 무관하게 전투 흐름은 그대로 이어진다.
    /// </summary>
    public static UniTask Play(VideoClip _clip)
    {
        CardCinematicPlayer t_inst = Instance;
        if (t_inst == null || _clip == null) return UniTask.CompletedTask;
        return t_inst.PlayAsync(_clip);
    }

    /// <summary>클립을 재생하고 끝(또는 스킵)까지 대기. 재생 중 재호출되면 앞 재생이 끝난 뒤 이어 재생한다.</summary>
    public async UniTask PlayAsync(VideoClip _clip)
    {
        // 배치 미완/클립 없음 → 조용히 즉시 완료. 예외도 로그도 남기지 않는다.
        if (_clip == null || this.videoPlayer == null || this.rawImage == null) return;

        int t_ticket = this.nextTicket++;
        if (this.nowServing != t_ticket)
        {
            // 겹쳐 재생 금지: 앞 순번이 끝나 내 번호가 호출될 때까지 대기(프레임 폴링, lock 없음).
            bool t_canceled = await UniTask
                .WaitUntil(() => this.nowServing == t_ticket, cancellationToken: this.GetCancellationTokenOnDestroy())
                .SuppressCancellationThrow();
            if (t_canceled) return;   // 대기 중 파괴됨 → 호출부는 그냥 진행
        }

        try
        {
            await PlayOneAsync(_clip);
        }
        finally
        {
            this.nowServing = t_ticket + 1;   // 다음 순번 깨우기
        }
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    async UniTask PlayOneAsync(VideoClip _clip)
    {
        this.IsPlaying     = true;
        this.skipRequested = false;
        gameObject.SetActive(true);
        EnsureRenderTexture();

        if (this.background != null) this.background.gameObject.SetActive(true);

        // 스킵 버튼: 지연 후 등장, 누르면 skipRequested만 세운다(실제 종료는 WatchAsync 한 곳에서).
        if (this.skipButton != null)
        {
            this.skipButton.onClick.RemoveListener(RequestSkip);
            this.skipButton.onClick.AddListener(RequestSkip);
            this.skipButton.gameObject.SetActive(false);
            ShowSkipButtonAfterDelay().Forget();
        }

        if (this.group != null)
        {
            this.group.DOKill();
            this.group.alpha          = 0f;
            this.group.blocksRaycasts = true;                                    // 컷씬 중 하위 UI 입력 차단
            this.group.DOFade(1f, this.fadeDuration).SetLink(gameObject);        // 페이드 인(영상은 그 사이 이미 재생)
        }

        var t_tcs = new UniTaskCompletionSource();
        void OnEnd(VideoPlayer _vp) { t_tcs.TrySetResult(); }

        var t_cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        try
        {
            this.videoPlayer.loopPointReached += OnEnd;
            this.videoPlayer.clip = _clip;
            this.videoPlayer.Play();

            // 스킵 입력 + 오브젝트 파괴 감시. 어떤 경우에도 t_tcs를 완료시켜 hang을 막는다.
            WatchAsync(t_tcs, t_cts.Token).Forget();

            await t_tcs.Task;

            if (this.videoPlayer != null)
            {
                this.videoPlayer.loopPointReached -= OnEnd;   // 핸들러 누수 방지(스킵 경로 포함)
                this.videoPlayer.Stop();
            }

            if (this.group != null)
            {
                this.group.DOKill();
                bool t_canceled = await this.group.DOFade(0f, this.fadeDuration)
                    .SetLink(gameObject)
                    .ToUniTask(cancellationToken: this.GetCancellationTokenOnDestroy())
                    .SuppressCancellationThrow();
                if (t_canceled) return;
            }
        }
        finally
        {
            // 방어적 정리(중복 호출 안전). 파괴된 뒤라면 각 참조가 null로 잡혀 건너뛴다.
            if (this.videoPlayer != null)
            {
                this.videoPlayer.loopPointReached -= OnEnd;
                this.videoPlayer.Stop();
            }

            t_cts.Cancel();
            t_cts.Dispose();

            if (this.group != null)
            {
                this.group.DOKill();
                this.group.alpha          = 0f;
                this.group.blocksRaycasts = false;
            }

            if (this.skipButton != null)
            {
                this.skipButton.onClick.RemoveListener(RequestSkip);
                this.skipButton.gameObject.SetActive(false);
            }
            if (this.background != null) this.background.gameObject.SetActive(false);

            this.IsPlaying     = false;
            this.skipRequested = false;
            if (this != null) gameObject.SetActive(false);
        }
    }

    void RequestSkip() => this.skipRequested = true;

    // 재생 시작 후 skipButtonDelay 뒤에 스킵 버튼 노출. 재생이 그 전에 끝났으면 띄우지 않는다.
    async UniTaskVoid ShowSkipButtonAfterDelay()
    {
        float t_delay = Mathf.Max(0f, this.skipButtonDelay);
        if (t_delay > 0f)
        {
            bool t_canceled = await UniTask.Delay((int)(t_delay * 1000), DelayType.UnscaledDeltaTime,
                    cancellationToken: this.GetCancellationTokenOnDestroy())
                .SuppressCancellationThrow();
            if (t_canceled) return;
        }
        if (this.IsPlaying && this.skipButton != null) this.skipButton.gameObject.SetActive(true);
    }

    /// <summary>스킵 입력과 오브젝트 파괴를 감시해 재생 대기를 반드시 끝내준다.</summary>
    async UniTaskVoid WatchAsync(UniTaskCompletionSource _tcs, CancellationToken _token)
    {
        float t_elapsed = 0f;

        while (true)
        {
            bool t_canceled = await UniTask.Yield(PlayerLoopTiming.Update, _token).SuppressCancellationThrow();
            if (t_canceled) { _tcs.TrySetResult(); return; }   // 재생 종료 후 정리 or 파괴 → 대기 해제

            // 스킵 버튼은 봉인 시간과 무관하게 즉시 유효(버튼 자체가 지연 후 나타난다).
            if (this.skipRequested) { _tcs.TrySetResult(); return; }

            t_elapsed += Time.unscaledDeltaTime;
            if (!this.allowSkip || t_elapsed < SkipInputGuard) continue;

            if (Input.anyKeyDown || Input.GetMouseButtonDown(0)) { _tcs.TrySetResult(); return; }
        }
    }

    /// <summary>화면 해상도에 맞춘 RenderTexture 확보. 크기가 달라졌으면 새로 만든다.</summary>
    void EnsureRenderTexture()
    {
        if (this.renderTexture != null
            && this.renderTexture.width  == Screen.width
            && this.renderTexture.height == Screen.height) return;

        ReleaseRenderTexture();

        this.renderTexture             = new RenderTexture(Screen.width, Screen.height, 0);
        this.videoPlayer.targetTexture = this.renderTexture;
        this.rawImage.texture          = this.renderTexture;
    }

    void ReleaseRenderTexture()
    {
        if (this.renderTexture == null) return;

        this.renderTexture.Release();
        Destroy(this.renderTexture);
        this.renderTexture = null;
    }
}
