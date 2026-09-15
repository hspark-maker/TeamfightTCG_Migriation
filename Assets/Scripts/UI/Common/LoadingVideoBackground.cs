using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>로딩 배경 영상을 무음 반복 재생하고, 비율을 유지해 화면 전체를 채운다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage), typeof(VideoPlayer))]
public sealed class LoadingVideoBackground : MonoBehaviour
{
    [SerializeField] Texture2D firstFrame;

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

        Rect t_rect = this.image.rectTransform.rect;
        if (t_rect.width <= 0f || t_rect.height <= 0f) return;

        float t_sourceAspect = (float)this.image.texture.width / this.image.texture.height;
        float t_targetAspect = t_rect.width / t_rect.height;
        // 긴 쪽의 가장자리만 잘라 여백이나 영상 왜곡 없이 덮는다.
        float t_width = Mathf.Min(1f, t_targetAspect / t_sourceAspect);
        float t_height = Mathf.Min(1f, t_sourceAspect / t_targetAspect);
        this.image.uvRect = new Rect((1f - t_width) * 0.5f, (1f - t_height) * 0.5f, t_width, t_height);
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
