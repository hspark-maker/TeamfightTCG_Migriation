using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] SoundConfig config;
    [SerializeField] SoundBank outgameSoundBank;
    [SerializeField] int sfxPoolSize = 8;

    AudioSource bgmSource;
    AudioSource[] sfxPool;
    int sfxIndex;

    // 환경설정이 정한 상한과 연출용 페이드를 따로 든다 — 곱해서 실제 볼륨이 된다.
    // 한 값으로 합치면 페이드 도중 환경설정 슬라이더가 페이드값을 상한으로 읽어 간다.
    float bgmCeiling;
    float sfxCeiling;
    float bgmFade = 1f;

    CancellationTokenSource bgmFadeCts;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(transform.root.gameObject);   // 초기화 프리팹의 자식이라 루트 기준(단독 배치면 자기 자신)
        BuildSources();
        if (config?.bgm != null) PlayBGM(config.bgm);
    }

    void OnDestroy()
    {
        if (Instance == this) KillBgmFade();
    }

    const string PREFS_BGM = "BGMVolume";
    const string PREFS_SFX = "SFXVolume";

    public float BGMVolume => this.bgmCeiling;
    public float SFXVolume => this.sfxCeiling;

    public void SetBGMVolume(float _vol)
    {
        this.bgmCeiling = _vol;
        ApplyBgmVolume();
        LocalPrefs.SetFloat(PREFS_BGM, _vol);
    }

    public void SetSFXVolume(float _vol)
    {
        this.sfxCeiling = _vol;
        ApplySfxVolume();
        LocalPrefs.SetFloat(PREFS_SFX, _vol);
    }

    void BuildSources()
    {
        this.bgmCeiling = LocalPrefs.GetFloat(PREFS_BGM, 1f);
        this.sfxCeiling = LocalPrefs.GetFloat(PREFS_SFX, 1f);

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;

        sfxPool = new AudioSource[sfxPoolSize];
        for (int i = 0; i < sfxPoolSize; i++)
            sfxPool[i] = gameObject.AddComponent<AudioSource>();

        ApplyBgmVolume();
        ApplySfxVolume();
    }

    void ApplyBgmVolume()
    {
        if (this.bgmSource != null) this.bgmSource.volume = this.bgmCeiling * this.bgmFade;
    }

    void ApplySfxVolume()
    {
        float t_vol = SfxVolumeFor(1f);
        if (this.sfxPool == null) return;
        foreach (AudioSource t_src in this.sfxPool)
            if (t_src != null) t_src.volume = t_vol;
    }

    float SfxVolumeFor(float _scale) => this.sfxCeiling * (this.config != null ? this.config.sfxVolume : 1f) * _scale;

    // ── BGM ───────────────────────────────────────────────────────────────

    public void PlayBGM(AudioClip _clip) => PlayBGM(_clip, 0f);

    /// <summary>_clip을 처음부터 켠다. _fadeIn이 0보다 크면 그 시간만큼 볼륨이 올라온다.</summary>
    public void PlayBGM(AudioClip _clip, float _fadeIn)
    {
        if (_clip == null || this.bgmSource == null) return;

        KillBgmFade();
        this.bgmSource.clip = _clip;
        this.bgmSource.Play();

        if (_fadeIn <= 0f)
        {
            this.bgmFade = 1f;
            ApplyBgmVolume();
            return;
        }

        this.bgmFade = 0f;
        ApplyBgmVolume();
        FadeBgmAsync(1f, _fadeIn, _stopAtEnd: false).Forget();
    }

    /// <summary>_dur만큼 볼륨을 내린 뒤 멈춘다. 씬 전환 창구가 부른다.</summary>
    public void FadeOutBGM(float _dur)
    {
        if (this.bgmSource == null || !this.bgmSource.isPlaying) return;

        KillBgmFade();

        if (_dur <= 0f) { StopBGM(); return; }

        FadeBgmAsync(0f, _dur, _stopAtEnd: true).Forget();
    }

    public void StopBGM()
    {
        KillBgmFade();
        this.bgmFade = 1f;
        if (this.bgmSource != null) this.bgmSource.Stop();
        ApplyBgmVolume();
    }

    /// <summary>BGM 재생 속도(1 = 원속). 승패 여운처럼 화면이 느려질 때 소리도 같이 끌리게 하는 표시용 레버.
    /// 이 매니저는 DontDestroyOnLoad라 <b>바꾼 쪽이 반드시 1로 되돌려야 한다</b> — 안 그러면 로비까지 끌린 채 간다.</summary>
    public void SetBGMPitch(float _pitch)
    {
        if (this.bgmSource != null) this.bgmSource.pitch = Mathf.Clamp(_pitch, 0.1f, 3f);
    }

    async UniTaskVoid FadeBgmAsync(float _to, float _dur, bool _stopAtEnd)
    {
        var t_cts = new CancellationTokenSource();
        this.bgmFadeCts = t_cts;
        CancellationToken t_ct = t_cts.Token;

        float t_from = this.bgmFade;
        float t_elapsed = 0f;

        while (t_elapsed < _dur)
        {
            bool t_canceled = await UniTask.Yield(PlayerLoopTiming.Update, t_ct).SuppressCancellationThrow();
            if (t_canceled) return;

            // 승패 여운이 타임스케일을 늦추므로 unscaled로 센다 — 안 그러면 페이드가 같이 늘어진다.
            t_elapsed += Time.unscaledDeltaTime;
            this.bgmFade = Mathf.Lerp(t_from, _to, Mathf.Clamp01(t_elapsed / _dur));
            ApplyBgmVolume();
        }

        this.bgmFade = _to;
        ApplyBgmVolume();

        if (_stopAtEnd)
        {
            if (this.bgmSource != null) this.bgmSource.Stop();
            this.bgmFade = 1f;   // 다음 PlayBGM이 무음으로 시작하지 않게 되돌린다
            ApplyBgmVolume();
        }

        // 도중에 다른 페이드가 끼어들었으면 그쪽 토큰이 주인이다 — 내 것일 때만 치운다.
        if (ReferenceEquals(this.bgmFadeCts, t_cts)) KillBgmFade();
    }

    void KillBgmFade()
    {
        this.bgmFadeCts?.Cancel();
        this.bgmFadeCts?.Dispose();
        this.bgmFadeCts = null;
    }

    // ── SFX ───────────────────────────────────────────────────────────────

    public void PlaySFX(AudioClip _clip) => PlaySFX(_clip, 1f);

    public void PlaySFX(AudioClip _clip, float _volumeScale)
    {
        if (_clip == null || this.sfxPool == null || this.sfxPool.Length == 0) return;

        AudioSource t_src = sfxPool[sfxIndex % sfxPool.Length];
        sfxIndex++;

        // 크기 보정은 원샷에만 얹는다 — 소스 볼륨을 건드리면 그 소스에서 아직 울리는 이전 소리까지 같이 갈린다.
        t_src.PlayOneShot(_clip, Mathf.Clamp01(_volumeScale));
    }

    public void PlayRandom(AudioClip[] _clips)
    {
        if (_clips == null || _clips.Length == 0) return;
        PlaySFX(_clips[Random.Range(0, _clips.Length)]);
    }

    /// <summary>아웃게임 사건 하나의 소리를 낸다. 소리표에 배정이 없으면 조용히 넘어간다.</summary>
    public void PlayCue(EOutgameSound _cue)
    {
        if (this.outgameSoundBank == null) return;
        if (!this.outgameSoundBank.TryGet(_cue, out SoundCueEntry t_entry)) return;

        PlaySFX(t_entry.clips[Random.Range(0, t_entry.clips.Length)], t_entry.volumeScale);
    }

    public void PlayUIClick() => PlayRandom(config?.uiClickClips);
    public void PlayHit() => PlayRandom(config?.hitClips);
    public void PlayDeath() => PlayRandom(config?.deathClips);
    public void PlayDealCard() => PlayRandom(config?.dealCardClips);
    public void PlayTurnChange() => PlayRandom(config?.turnChangeClips);
    public void PlayCinemaEnter() => PlayRandom(config?.cinemaEnterClips);

    // ── Voice (Pity) ──────────────────────────────────────────────────────

}
