using System;
using System.Security.Cryptography;
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

    static DeterministicRandom s_stream;
    static ulong s_initialSeed;

    public static bool IsSeeded => s_stream.IsSeeded;
    public static ulong InitialSeed => s_initialSeed;

    /// <summary>스트림 전진 횟수. Range(n)은 n&lt;=1이면 전진하지 않으므로 이 값이 곧 '실제 소비 횟수'.
    /// 양 클라가 같은 시점에 같은 값이어야 함 — 어긋나면 그 순간부터 영구 divergence.
    /// 테스트 assert용 + 멀티 desync 카나리아용(현재 divergence 탐지 수단이 이것뿐).</summary>
    public static int DrawCount => s_stream.DrawCount;

    public static void Seed(ulong _seed)
    {
        s_initialSeed = _seed;
        s_stream.Seed(_seed);
    }

    /// <summary>싱글플레이용 로컬 랜덤 시드.</summary>
    public static void SeedRandomLocal() => Seed(ReadU64(NewNonce()));

    public static void Reset()
    {
        s_stream.Reset();
        s_initialSeed = 0;
    }

    /// <summary>공유 전투 RNG의 DrawCount를 소비하지 않는 owner별 독립 셔플 스트림.</summary>
    public static DerivedStream DeriveDeckStream(int _ownerIndex)
    {
        if (!IsSeeded) throw new InvalidOperationException("MatchRandom seed is not initialized.");
        return new DerivedStream(DeterministicRandom.DeriveDeckSeed(s_initialSeed, _ownerIndex));
    }

    // splitmix64
    static ulong NextU64()
    {
        // 시드 전 소비 = 0-state 회피값으로 시작하는 고정 시퀀스가 나가고, 뒤늦은 Seed가 스트림을 리셋해
        // 소비 순서가 어긋난다(멀티면 그 순간부터 영구 divergence). 컴파일러가 못 잡으니 런타임 카나리아.
        if (!IsSeeded)
            UnityEngine.Debug.LogError("[MatchRandom] 시드 전 소비 — 시드 지점(GameInitializer/SyncInitialDecks)보다 앞선 호출이 있다.");
        return s_stream.NextU64();
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
