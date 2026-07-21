using UnityEngine;

[CreateAssetMenu(fileName = "SoundConfig", menuName = "Card Battle/Sound Config")]
public class SoundConfig : ScriptableObject
{
    [Header("BGM")]
    public AudioClip bgm;

    [Header("SFX")]
    public AudioClip[] uiClickClips;
    public AudioClip[] hitClips;
    public AudioClip[] deathClips;
    public AudioClip[] dealCardClips;
    public AudioClip[] turnChangeClips;
    public AudioClip[] cinemaEnterClips;
    public AudioClip[] passiveClips;
    [Range(0f, 1f)] public float sfxVolume = 1f;
}
