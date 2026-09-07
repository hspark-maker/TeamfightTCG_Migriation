using System;
using System.Security.Cryptography;
using System.Threading;
using TeamfightTCG.BattleCore;

/// <summary>
/// 멀티플레이어 결정론 RNG. 양 클라이언트가 동일 시드 + 동일 소비 순서로 같은 결과 재현.
/// 게임 로직 랜덤(스플래시 대상, 패시브 등)만 사용. 연출 랜덤(오디오/파티클)은
/// UnityEngine.Random 그대로 둔다 — 전역 시퀀스 오염 방지가 결정론의 핵심.
///
/// 시드는 서버가 발급한다(<see cref="MultiplayerTurnRunner"/>) — 양 클라가 같은 값을 받아 확정한다.
///
/// 알고리즘은 splitmix64 — 플랫폼/런타임 무관하게 동일 결과(System.Random 구현 의존 회피).
/// </summary>
public static class MatchRandom
{
    public struct DerivedStream
    {
        DeterministicRandom stream;

        internal DerivedStream(ulong _seed)
        {
            this.stream = default;
            this.stream.Seed(_seed);
        }

        public int Range(int _maxExclusive) => this.stream.Range(_maxExclusive);
    }

    sealed class RandomContext
    {
        public DeterministicRandom Stream;
        /// <summary>AI 의사결정 전용. 공용 Stream 과 분리돼 있다 — 아래 AiRange 주석 참조.</summary>
        public DeterministicRandom AiStream;
        public ulong InitialSeed;
    }

    // 슬롯이 둘인 이유는 SynergyRuleProvider 와 같다.
    //   전역   = Unity 런타임. 시드 시점과 소비 시점이 프레임·씬을 건너뛴다. AsyncLocal 로는 못 버틴다 —
    //            Firebase 콜백이 UnitySynchronizationContext 로 돌아올 때 ExecutionContext 가 복원되면서
    //            그 뒤에 심은 AsyncLocal 값이 지워진다. 지워진 채 소비하면 예외가 아니라 **다른 난수**가
    //            나가므로(아래 Current 가 빈 컨텍스트를 새로 만든다) 조용한 divergence 가 된다.
    //   흐름   = Cloud Run 재생 서비스. 요청마다 시드가 다르고 동시에 돈다. 전역을 쓰면 서로 덮어쓴다.
    // 조회는 흐름이 전역보다 우선한다.
    static RandomContext s_global;
    static readonly AsyncLocal<RandomContext> s_scoped = new AsyncLocal<RandomContext>();

    static RandomContext Active => s_scoped.Value ?? s_global;

    static RandomContext Current
    {
        get
        {
            RandomContext t_context = Active;
            if (t_context != null) return t_context;
            // 시드 없이 소비한 경우다(NextU64 가 이미 오류를 남겼다). 흐름 슬롯이 비어 있으니
            // 전역에 만든다 — 서버는 진입에서 반드시 SeedScoped 하므로 여기로 오지 않는다.
            t_context = new RandomContext();
            s_global = t_context;
            return t_context;
        }
    }

    public static bool IsSeeded => Active != null && Active.Stream.IsSeeded;
    public static ulong InitialSeed => Active?.InitialSeed ?? 0;

    /// <summary>스트림 전진 횟수. Range(n)은 n&lt;=1이면 전진하지 않으므로 이 값이 곧 '실제 소비 횟수'.
    /// 양 클라가 같은 시점에 같은 값이어야 함 — 어긋나면 그 순간부터 영구 divergence.
    /// 테스트 assert용 + 멀티 desync 카나리아용(현재 divergence 탐지 수단이 이것뿐).</summary>
    public static int DrawCount => Active?.Stream.DrawCount ?? 0;

    static RandomContext NewContext(ulong _seed)
    {
        var t_context = new RandomContext { InitialSeed = _seed };
        t_context.Stream.Seed(_seed);
        t_context.AiStream.Seed(DeterministicRandom.DeriveAiSeed(_seed));
        return t_context;
    }

    /// <summary>
    /// AI 의사결정용 난수. **공용 스트림을 소비하지 않는다.**
    ///
    /// <para>서버 재생기(<see cref="BattleReplay"/>)는 명령 로그에서 공격자·대상을 읽을 뿐 AI 선택을
    /// 재현하지 않는다. AI 가 공용 스트림을 한 번이라도 소비하면 그 순간부터 클라와 재생기의 소비
    /// 횟수가 어긋나 처형 대상·무쌍 광역이 전부 다른 값으로 갈린다(실측: 솔로 판에서 클라 6회 대
    /// 재생기 5회 — `derived_target_mismatch`). 덱 셔플이 파생 스트림을 쓰는 이유와 같다.</para>
    /// </summary>
    public static int AiRange(int _maxExclusive)
    {
        if (_maxExclusive <= 1) return 0;
        if (!IsSeeded)
            BattleRuleBridge.LogError?.Invoke(
                "[MatchRandom] AI 스트림을 시드 전에 소비했다 — 시드 지점(GameInitializer)보다 앞선 호출이 있다.");
        return Current.AiStream.Range(_maxExclusive);
    }

    /// <summary>프로세스 전역 시드. 한 번에 한 판만 도는 쪽(Unity 전투)이 쓴다.</summary>
    public static void Seed(ulong _seed)
    {
        s_global = NewContext(_seed);
        s_scoped.Value = null;
    }

    /// <summary>지금 호출 흐름에만 시드. 서로 다른 시드로 동시에 도는 쪽(재생 서비스)이 쓴다.</summary>
    public static void SeedScoped(ulong _seed) => s_scoped.Value = NewContext(_seed);

    /// <summary>싱글플레이용 로컬 랜덤 시드.</summary>
    public static void SeedRandomLocal() => Seed(ReadU64(NewNonce()));

    /// <summary>전역·흐름 슬롯을 모두 비운다.</summary>
    public static void Reset()
    {
        s_global = null;
        s_scoped.Value = null;
    }

    /// <summary>흐름 슬롯만 비운다. 요청 종료 시 서버가 쓴다.</summary>
    public static void ResetScoped() => s_scoped.Value = null;

    /// <summary>공유 전투 RNG의 DrawCount를 소비하지 않는 owner별 독립 셔플 스트림.</summary>
    public static DerivedStream DeriveDeckStream(int _ownerIndex)
    {
        if (!IsSeeded) throw new InvalidOperationException("MatchRandom seed is not initialized.");
        return new DerivedStream(DeterministicRandom.DeriveDeckSeed(Current.InitialSeed, _ownerIndex));
    }

    // splitmix64
    static ulong NextU64()
    {
        // 시드 전 소비 = 0-state 회피값으로 시작하는 고정 시퀀스가 나가고, 뒤늦은 Seed가 스트림을 리셋해
        // 소비 순서가 어긋난다(멀티면 그 순간부터 영구 divergence). 컴파일러가 못 잡으니 런타임 카나리아.
        if (!IsSeeded)
            BattleRuleBridge.LogError?.Invoke(
                "[MatchRandom] 시드 전 소비 — 시드 지점(GameInitializer/SyncInitialDecks)보다 앞선 호출이 있다.");
        RandomContext t_context = Current;
        return t_context.Stream.NextU64();
    }

    /// <summary>[0, _maxExclusive) 균등. UnityEngine.Random.Range(0, n) 대체.</summary>
    public static int Range(int _maxExclusive)
    {
        if (_maxExclusive <= 1) return 0;
        return (int)(NextU64() % (ulong)_maxExclusive);
    }

    /// <summary>[_minInclusive, _maxExclusive) 균등.</summary>
    public static int Range(int _minInclusive, int _maxExclusive)
        => _minInclusive + Range(_maxExclusive - _minInclusive);

    // ── commit-reveal 헬퍼 ────────────────────────────────────────────────

    /// <summary>암호학적 8바이트 nonce (예측 불가).</summary>
    public static byte[] NewNonce()
    {
        byte[] t_b = new byte[8];
        using (var t_rng = RandomNumberGenerator.Create())
            t_rng.GetBytes(t_b);
        return t_b;
    }

    /// <summary>8바이트를 big-endian ulong으로. 양 클라 동일 변환 보장.</summary>
    public static ulong ReadU64(byte[] _b)
    {
        ulong t_v = 0;
        for (int i = 0; i < 8; i++)
            t_v = (t_v << 8) | _b[i];
        return t_v;
    }

}
