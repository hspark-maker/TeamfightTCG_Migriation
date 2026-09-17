using UnityEngine;

[CreateAssetMenu(fileName = "SoundConfig", menuName = "Card Battle/Sound Config")]
public class SoundConfig : ScriptableObject
{
    [Header("BGM")]
    public AudioClip lobbyBgm;
    public AudioClip battleBgm;

    [Header("SFX")]
    public AudioClip[] uiClickClips;
    public AudioClip[] hitClips;
    public AudioClip[] deathClips;
    public AudioClip[] dealCardClips;
    public AudioClip[] turnChangeClips;
    public AudioClip[] cinemaEnterClips;
    [Range(0f, 1f)] public float sfxVolume = 1f;
}
