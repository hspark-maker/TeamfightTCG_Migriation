using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>승부가 갈리는 순간의 전역 연출 레버(배속·배경 블러·BGM 피치)를 <b>혼자 소유하는</b> 클래스.
/// 두 박자를 낸다 — 결정타를 강조하는 <see cref="PlayFinish"/>(BattleFinisher가 부름)와,
/// 결과 팝업 직전의 여운 <see cref="Play"/>(TurnRunner가 부름).
///
/// <para><b>여기서만 Time.timeScale을 건드린다.</b> 프로젝트의 다른 연출(타격 멈칫·화면 흔들림)은 전역 배속을
/// 의도적으로 피한다 — 전투 중에 배속을 흔들면 다른 카드의 트윈·대기까지 같이 늘어나고, 그게 두 클라의
/// 연출 길이 차이가 되기 때문이다. 두 박자만 그 제약 밖이다: 판이 이미 끝난 타격(전멸이 확정된 한 방)과
/// 승패 확정 이후에만 돌고, 규칙도 RNG도 소비하지 않으며, 길이가 <b>승패와 무관하게 같고</b>,
/// 팝업이 뜨기 전에 반드시 1로 돌아온다.</para>
///
/// <para>만지는 전역 상태는 셋뿐이다 — <c>Time.timeScale</c>, <see cref="ScreenBlurFeature.Strength"/>,
/// BGM 피치. timeScale과 피치는 각 박자의 finally에서 즉시 되돌리고(시뮬레이션·소리는 남으면 안 된다),
/// 블러와 카메라 줌은 팝업 뒤 배경으로 <b>일부러 남긴다</b> — 되돌리는 곳은
/// <see cref="Reset"/> 하나이고, 씬을 벗어나는 유일한 경로인 BattleCleanup.Run이 그걸 부른다.</para></summary>
public static class BattleResultBeat
{
    // 도메인 리로드를 끈 채 플레이를 반복하면 정적 값이 남는다. 시작은 항상 "여운 없음"이어야 한다 —
    // Time.timeScale은 에디터가 알아서 되돌려 주지 않으므로, 여운 중에 플레이를 멈추면 다음 플레이가 느리게 뜬다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_playing      = false;
        s_finishActive = false;
        s_finishPlayed = false;
        Time.timeScale = 1f;
    }

    static bool s_playing;
    static bool s_finishActive;   // BeginFinish ~ EndFinish 사이. 배속이 낮게 깔려 있는 구간이다.
    static bool s_finishPlayed;   // 이번 판에서 결정타 강조가 돌았는가. 여운이 슬로우를 두 번 먹이지 않게 한다.

    /// <summary>결정타 강조가 이미 돌았는가. 여운(<see cref="Play"/>)이 자기 길이를 정할 때 본다.</summary>
    public static bool FinishPlayed => s_finishPlayed;

    /// <summary>지금 배속이 낮게 깔린 결정타 구간인가(BeginFinish ~ EndFinish 사이).</summary>
    public static bool FinishActive => s_finishActive;

    // 이 구간은 배경 블러를 건드리지 않는다(_keepBlur 고정) — 결정타는 선명해야 읽힌다.
    // 흐림은 "보드에서 팝업으로 넘어가는" 장치라 Play가 소유한다.
    const float NoBlur = 0f;

    /// <summary>결정타 강조의 <b>앞부분</b> — 얼어붙기(timeScale 0) → 슬로우 진입. 여기서 반환하면
    /// 화면은 느려진 채로 남는다. 호출부는 그 상태에서 사망 연출을 재생하고(스케일드 트윈이라 같이 느려진다)
    /// <see cref="EndFinish"/>로 닫는다 — <b>슬로우 안에서 죽는 그림</b>이 이 연출의 핵심이라,
    /// 비트를 다 돌린 뒤에 사망을 재생하면 강조가 빈 화면에 걸린다.
    ///
    /// <para>반드시 짝을 맞출 것. EndFinish를 빠뜨리면 배속이 낮은 채로 전투가 계속된다
    /// (호출부 AttackSequence는 finally로 보장한다).</para>
    ///
    /// <paramref name="_won"/>은 <b>길이를 바꾸지 않는다</b> — 멀티에서 양쪽 길이가 갈리면 다음 동기 시점이
    /// 어긋난다. 깊이·색을 가르는 자리를 남겨두기 위한 인자다(사운드·색감 확장 지점).</summary>
    public static async UniTask BeginFinish(bool _won, CancellationToken _ct = default)
    {
        if (s_playing || s_finishActive) return;
        s_finishActive = true;
        s_finishPlayed = true;

        try
        {
            BattleTimingConfig t_cfg = GameTiming.Battle;
            float t_pitch = t_cfg.FinishBgmPitch;

            // 얼어붙기가 먼저. 타격이 터진 프레임을 통째로 붙잡아야 "결정타"로 읽힌다 —
            // 여기서부터 배속을 서서히 내리면 그냥 늘어진 타격이 된다.
            if (t_cfg.FinishHitStop > 0f)
            {
                Apply(1f, 0f, NoBlur, t_pitch, _keepBlur: true);   // timeScale 0 = 완전 정지
                await UniTask.Delay((int)(t_cfg.FinishHitStop * 1000f), ignoreTimeScale: true, cancellationToken: _ct);
            }

            await Ramp(t_cfg.FinishIn, 0f, 1f, SlowOf(t_cfg), NoBlur, t_pitch, _ct, _keepBlur: true);
        }
        catch
        {
            // 진입에 실패하면 짝이 될 EndFinish가 오지 않을 수 있다 — 그 자리에서 배속을 되돌린다.
            RestoreTime();
            s_finishActive = false;
            throw;
        }
    }

    /// <summary>결정타 강조의 <b>뒷부분</b> — 사망 연출이 끝난 뒤의 짧은 여운과 정상 배속 복귀.
    /// <see cref="BeginFinish"/>가 돌지 않았으면 무동작이라, 호출부가 분기 없이 finally에서 부르면 된다.</summary>
    public static async UniTask EndFinish(CancellationToken _ct = default)
    {
        if (!s_finishActive) return;

        try
        {
            BattleTimingConfig t_cfg = GameTiming.Battle;
            await UniTask.Delay((int)(t_cfg.FinishHold * 1000f), ignoreTimeScale: true, cancellationToken: _ct);
            await Ramp(t_cfg.FinishOut, 1f, 0f, SlowOf(t_cfg), NoBlur, t_cfg.FinishBgmPitch, _ct, _keepBlur: true);
        }
        finally
        {
            RestoreTime();
            s_finishActive = false;
        }
    }

    static float SlowOf(BattleTimingConfig _cfg) => Mathf.Clamp(_cfg.FinishSlow, 0.02f, 1f);

    // 시뮬레이션 속도와 소리는 어떤 경로로 빠져나가도 남으면 안 된다.
    static void RestoreTime()
    {
        Time.timeScale = 1f;
        SoundManager.Instance?.SetBGMPitch(1f);
    }

    /// <summary>여운 재생. 끝나면 배속·피치는 정상으로 돌아와 있고, 호출자는 곧장 결과 팝업을 띄우면 된다.
    /// 이미 재생 중이면 아무것도 하지 않고 즉시 반환한다(결과는 한 번만 확정되므로 정상 경로에선 발생하지 않는다).
    ///
    /// <paramref name="_ct"/>는 반드시 넘긴다 — 여운 도중 씬이 내려가면 이 루프가 살아남아
    /// <b>다음 씬의</b> Time.timeScale을 낮춘 채 방치할 수 있다. 취소되면 finally가 그 자리에서 되돌린다.</summary>
    public static async UniTask Play(bool _won, CancellationToken _ct = default)
    {
        if (s_playing) return;
        s_playing = true;

        try
        {
            BattleTimingConfig t_cfg = GameTiming.Battle;

            // 결정타 강조가 이미 돌았으면 슬로우는 다시 걸지 않는다 — 두 번 먹이면 "결정타"가 흐려지고
            // 결과까지 늘어진다. 남은 일은 팝업으로 넘어가는 준비뿐이라 배경만 흐려 놓고 넘긴다
            // (카메라 클로즈업은 피니시가 잡아둔 그대로 배경이 된다).
            if (s_finishPlayed)
            {
                await Ramp(t_cfg.ResultBeatAfterFinish, 0f, 1f,
                           _slow: 1f, _blur: Mathf.Clamp01(t_cfg.ResultBeatBlur), _pitch: 1f, _ct);
                return;
            }

            // 패배는 승리보다 얕고 짧게. 깊이(슬로우·블러·줌)와 머무는 시간에 같은 비율을 곱한다.
            float t_depth = _won ? 1f : Mathf.Clamp01(t_cfg.ResultBeatLoseRatio);

            float t_slow  = Mathf.Clamp(Mathf.Lerp(1f, t_cfg.ResultBeatSlow, t_depth), 0.05f, 1f);
            float t_blur  = Mathf.Clamp01(t_cfg.ResultBeatBlur * t_depth);
            float t_pitch = Mathf.Lerp(1f, t_cfg.ResultBeatBgmPitch, t_depth);

            float t_in   = t_cfg.ResultBeatIn;
            float t_hold = t_cfg.ResultBeatHold * t_depth;
            float t_out  = t_cfg.ResultBeatOut;

            // 카메라는 여운 전체 길이에 걸쳐 천천히 다가간다. 되돌리지 않는다 — 팝업 뒤 배경으로 남는다.
            BattleCamera.ResultPush(t_depth, t_in + t_hold + t_out);

            await Ramp(t_in, 0f, 1f, t_slow, t_blur, t_pitch, _ct);
            await UniTask.Delay((int)(t_hold * 1000f), ignoreTimeScale: true, cancellationToken: _ct);
            // 복귀에서 블러만은 제자리(1)에 둔다 — 배경이 흐린 채로 팝업이 올라오는 게 이 연출의 도착점이다.
            await Ramp(t_out, 1f, 0f, t_slow, t_blur, t_pitch, _ct, _keepBlur: true);
        }
        finally
        {
            RestoreTime();
            s_playing = false;
        }
    }

    /// <summary>결정타 강조가 돌았는데 <b>판이 끝나지 않은</b> 경우의 복구. 전멸 판정(BattleFinisher.Arm)과
    /// 실제 승패 판정(TurnRunner.CheckGameOver)은 같은 <c>IsEmpty</c>를 보므로 정상적으로는 어긋나지 않지만,
    /// 어긋나면 흐리고 당겨진 화면에서 전투가 그대로 계속된다 — 그건 버그가 아니라 먹통으로 보인다.
    /// 턴 루프가 다음 턴으로 넘어갈 때마다 부른다(안 돌았으면 무동작).</summary>
    public static void AbortFinish()
    {
        if (!s_finishPlayed) return;
        s_finishPlayed = false;
        s_finishActive = false;

        RestoreTime();
        ScreenBlurFeature.Strength = 0f;
        BattleCamera.RestoreFromFinish(0.25f);
    }

    /// <summary>여운이 남긴 전역 상태를 전부 되돌린다. 전투 씬을 벗어나는 단일 경로(BattleCleanup.Run)가 부른다.</summary>
    public static void Reset()
    {
        RestoreTime();
        ScreenBlurFeature.Strength = 0f;
        BattleCamera.RestoreFromFinish(0.01f);   // 씬이 살아 있다면 줌도 푼다(없으면 무동작)
        s_playing      = false;
        s_finishActive = false;
        s_finishPlayed = false;
    }

    // _t01 = 0(정상) → 1(가장 깊은 여운). 대기는 전부 unscaled — 배속을 낮추는 당사자가 배속에 끌려가면
    // 남은 시간이 스스로 늘어나 여운 길이를 통제할 수 없게 된다.
    static async UniTask Ramp(float _duration, float _from, float _to,
                              float _slow, float _blur, float _pitch,
                              CancellationToken _ct, bool _keepBlur = false)
    {
        if (_duration <= 0f) { Apply(_to, _slow, _blur, _pitch, _keepBlur); return; }

        float t_elapsed = 0f;
        while (t_elapsed < _duration)
        {
            t_elapsed += Time.unscaledDeltaTime;
            Apply(Mathf.Lerp(_from, _to, Mathf.Clamp01(t_elapsed / _duration)), _slow, _blur, _pitch, _keepBlur);
            await UniTask.Yield(PlayerLoopTiming.Update, _ct);
        }

        Apply(_to, _slow, _blur, _pitch, _keepBlur);
    }

    static void Apply(float _t01, float _slow, float _blur, float _pitch, bool _keepBlur)
    {
        Time.timeScale = Mathf.Lerp(1f, _slow, _t01);
        SoundManager.Instance?.SetBGMPitch(Mathf.Lerp(1f, _pitch, _t01));
        if (!_keepBlur) ScreenBlurFeature.Strength = Mathf.Lerp(0f, _blur, _t01);
    }
}
