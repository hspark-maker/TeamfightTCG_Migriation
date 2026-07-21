using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] SoundConfig config;
    [SerializeField] int sfxPoolSize = 8;

    [Header("Voice Pity")]
    [SerializeField, Range(0f, 1f)] float voiceBaseChance = 0.4f;
    [SerializeField, Range(0f, 1f)] float voiceChanceIncrement = 0.25f;

    AudioSource bgmSource;
    AudioSource voiceSource;
    AudioSource[] sfxPool;
    int sfxIndex;

    float spawnVoiceChance;
    float attackVoiceChance;
    float killVoiceChance;
    float deathVoiceChance;
    float effectVoiceChance;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildSources();
        ResetVoiceChances();
        if (config?.bgm != null) PlayBGM(config.bgm);
    }

    const string PREFS_BGM = "BGMVolume";
    const string PREFS_SFX = "SFXVolume";

    public float BGMVolume => this.bgmSource != null ? this.bgmSource.volume : 0f;
    public float SFXVolume => this.sfxPool != null && this.sfxPool.Length > 0 ? this.sfxPool[0].volume : 0f;

    public void SetBGMVolume(float _vol)
    {
        if (this.bgmSource != null) this.bgmSource.volume = _vol;
        PlayerPrefs.SetFloat(PREFS_BGM, _vol);
    }

    public void SetSFXVolume(float _vol)
    {
        if (this.voiceSource != null) this.voiceSource.volume = _vol;
        if (this.sfxPool == null) return;
        foreach (AudioSource t_src in this.sfxPool)
            if (t_src != null) t_src.volume = _vol;
        PlayerPrefs.SetFloat(PREFS_SFX, _vol);
    }

    void BuildSources()
    {
        float t_bgmVol = PlayerPrefs.GetFloat(PREFS_BGM, 1f);
        float t_sfxVol = PlayerPrefs.GetFloat(PREFS_SFX, 1f);

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.volume = t_bgmVol;

        voiceSource = gameObject.AddComponent<AudioSource>();
        voiceSource.volume = t_sfxVol;

        sfxPool = new AudioSource[sfxPoolSize];
        for (int i = 0; i < sfxPoolSize; i++)
        {
            sfxPool[i] = gameObject.AddComponent<AudioSource>();
            sfxPool[i].volume = t_sfxVol;
        }
    }

    void ResetVoiceChances()
    {
        spawnVoiceChance = voiceBaseChance;
        attackVoiceChance = voiceBaseChance;
        killVoiceChance = voiceBaseChance;
        deathVoiceChance = voiceBaseChance;
        effectVoiceChance = voiceBaseChance;
    }

    // ── BGM / SFX ─────────────────────────────────────────────────────────

    public void PlayBGM(AudioClip _clip)
    {
        if (_clip == null) return;
        bgmSource.clip = _clip;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        bgmSource.Stop();
    }

    public void PlaySFX(AudioClip _clip)
    {
        if (_clip == null) return;
        AudioSource t_src = sfxPool[sfxIndex % sfxPool.Length];
        sfxIndex++;
        t_src.volume = config?.sfxVolume ?? 1f;
        t_src.PlayOneShot(_clip);
    }

    public void PlayRandom(AudioClip[] _clips)
    {
        if (_clips == null || _clips.Length == 0) return;
        PlaySFX(_clips[Random.Range(0, _clips.Length)]);
    }

    public void PlayUIClick() => PlayRandom(config?.uiClickClips);
    public void PlayHit() => PlayRandom(config?.hitClips);
    public void PlayDeath() => PlayRandom(config?.deathClips);
    public void PlayDealCard() => PlayRandom(config?.dealCardClips);
    public void PlayTurnChange() => PlayRandom(config?.turnChangeClips);
    public void PlayCinemaEnter() => PlayRandom(config?.cinemaEnterClips);
    public void PlayPassive() => PlayRandom(config?.passiveClips);

    // ── Voice (Pity) ──────────────────────────────────────────────────────

    public void PlaySpawnVoice(AudioClip[] _clips) => TryVoice(_clips, ref spawnVoiceChance);
    public void PlayAttackVoice(AudioClip[] _clips) => TryVoice(_clips, ref attackVoiceChance);
    public void PlayKillVoice(AudioClip[] _clips) => TryVoice(_clips, ref killVoiceChance);
    public void PlayDeathVoice(AudioClip[] _clips) => TryVoice(_clips, ref deathVoiceChance);
    public void PlayEffectVoice(AudioClip[] _clips) => TryVoice(_clips, ref effectVoiceChance);

    void TryVoice(AudioClip[] _clips, ref float _chance)
    {
        if (_clips == null || _clips.Length == 0) return;
        if (voiceSource.isPlaying)
        {
            _chance = Mathf.Min(1f, _chance + voiceChanceIncrement);
            return;
        }

        if (Random.value > _chance)
        {
            _chance = Mathf.Min(1f, _chance + voiceChanceIncrement);
            return;
        }
        AudioClip t_clip = _clips[Random.Range(0, _clips.Length)];
        if (t_clip == null) return;
        voiceSource.Stop();
        voiceSource.clip = t_clip;
        voiceSource.Play();
        _chance = voiceBaseChance;
    }
}
