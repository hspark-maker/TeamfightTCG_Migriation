using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>로비 BGM의 주인. SoundManager는 DontDestroyOnLoad라 어떤 곡을 켤지는 씬 쪽이 정한다.</summary>
public class LobbyAudioDirector : MonoBehaviour
{
    [Tooltip("BGM이 무음에서 환경설정 볼륨까지 올라오는 시간(초). 0이면 하드컷으로 시작한다.")]
    [SerializeField, Range(0f, 5f)] float fadeInSeconds = 1.2f;

    // 에디터에서 로비만 실행한 경우에도 다운로드·설정 주입을 기다린다. 재시도 동안 대기를 유지한다.
    async UniTaskVoid Start()
    {
        bool t_canceled = await UniTask.WaitUntil(
            () => SoundManager.Instance != null && SoundManager.Instance.IsConfigured,
            cancellationToken: this.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
        if (t_canceled) return;
        SoundManager.Instance.PlayLobbyBGM(this.fadeInSeconds);
    }
}
