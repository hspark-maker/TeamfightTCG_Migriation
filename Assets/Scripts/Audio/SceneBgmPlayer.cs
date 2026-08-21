using UnityEngine;

/// <summary>씬이 열릴 때 그 씬의 BGM을 켠다.
/// SoundManager는 DontDestroyOnLoad라 BGM의 주인은 매니저가 아니라 씬 쪽이다 —
/// 배틀은 <see cref="GameInitializer"/>가, 그 밖의 씬은 이 컴포넌트가 켠다.</summary>
public class SceneBgmPlayer : MonoBehaviour
{
    [SerializeField] AudioClip bgm;

    // Awake가 아니라 Start다 — 같은 씬에 있는 Boot의 SoundManager보다 늦게 돌아야 Instance가 잡힌다.
    void Start()
    {
        SoundManager.Instance?.PlayBGM(this.bgm);
    }
}
