using UnityEngine;

/// <summary>로비 BGM의 주인. SoundManager는 DontDestroyOnLoad라 어떤 곡을 켤지는 씬 쪽이 정한다.</summary>
public class LobbyAudioDirector : MonoBehaviour
{
    [SerializeField] AudioClip bgm;

    [Tooltip("BGM이 무음에서 환경설정 볼륨까지 올라오는 시간(초). 0이면 하드컷으로 시작한다.")]
    [SerializeField, Range(0f, 5f)] float fadeInSeconds = 1.2f;

    // Awake가 아니라 Start다 — 같은 씬에 있는 Initialize 프리팹의 SoundManager보다 늦게 돌아야 Instance가 잡힌다.
    void Start()
    {
        SoundManager.Instance?.PlayBGM(this.bgm, this.fadeInSeconds);
    }
}
