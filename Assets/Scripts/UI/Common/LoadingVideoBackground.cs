using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>로딩 배경 영상을 무음 반복 재생하고, 좌우 전체를 유지하며 세로로 확대해 상하를 자른다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage), typeof(VideoPlayer))]
public sealed class LoadingVideoBackground : MonoBehaviour
{
    [SerializeField] Texture2D firstFrame;
    [SerializeField, Min(1f), Tooltip("화면에 맞춘 영상의 세로 확대 배율. 1이면 전체 표시, 1보다 크면 위아래를 동일하게 자른다.")]
    float verticalZoom = 1.1f;

    RawImage image;
    VideoPlayer player;

    void OnEnable()
    {
        this.image = GetComponent<RawImage>();
        this.player = GetComponent<VideoPlayer>();
        this.image.raycastTarget = false;
        ShowFirstFrame();
        if (this.player.clip == null) return;

        this.player.playOnAwake = false;
        this.player.isLooping = true;
        this.player.renderMode = VideoRenderMode.APIOnly;
        this.player.audioOutputMode = VideoAudioOutputMode.None;
        this.player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        this.player.waitForFirstFrame = true;
        this.player.sendFrameReadyEvents = true;
        this.player.prepareCompleted += OnPrepared;
        this.player.frameReady += OnFirstFrame;
        this.player.errorReceived += OnError;
        this.player.Prepare();
    }

    void OnPrepared(VideoPlayer _player) => _player.Play();

    void ShowFirstFrame()
    {
        if (this.image == null) return;

        this.image.texture = this.firstFrame;
        this.image.enabled = this.firstFrame != null;
        FitToScreen();
    }

    void OnFirstFrame(VideoPlayer _player, long _frame)
    {
        if (_player.texture == null) return;

        this.image.texture = _player.texture;
        FitToScreen();
        this.image.enabled = true;
        _player.sendFrameReadyEvents = false;
        _player.frameReady -= OnFirstFrame;
    }

    void OnRectTransformDimensionsChange() => FitToScreen();

    void FitToScreen()
    {
        if (this.image == null || this.image.texture == null) return;

        // 가로 UV는 전부 사용하고, 세로만 중앙을 기준으로 확대한다. 원본 종횡비는 유지하지 않는다.
        float t_height = 1f / Mathf.Max(1f, this.verticalZoom);
        this.image.uvRect = new Rect(0f, (1f - t_height) * 0.5f, 1f, t_height);
    }

    void OnError(VideoPlayer _player, string _message)
    {
        // 영상 실패 시에도 첫 프레임을 유지하며 로딩은 계속 진행한다.
        ShowFirstFrame();
        StopVideo();
        Debug.LogWarning($"[LoadingVideoBackground] Background video failed: {_message}", this);
    }

    void OnDisable()
    {
        // Stop이 영상 텍스처를 해제하기 전에 기본 이미지로 되돌린다.
        ShowFirstFrame();
        StopVideo();
    }

    void StopVideo()
    {
        if (this.player != null)
        {
            this.player.prepareCompleted -= OnPrepared;
            this.player.frameReady -= OnFirstFrame;
            this.player.errorReceived -= OnError;
            this.player.sendFrameReadyEvents = false;
            this.player.Stop();
        }
    }
}
