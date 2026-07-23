using System;
using System.Security.Cryptography;

/// <summary>
/// 멀티플레이어 결정론 RNG. 양 클라이언트가 동일 시드 + 동일 소비 순서로 같은 결과 재현.
/// 게임 로직 랜덤(스플래시 대상, 패시브 등)만 사용. 연출 랜덤(오디오/파티클)은
/// UnityEngine.Random 그대로 둔다 — 전역 시퀀스 오염 방지가 결정론의 핵심.
///
/// 시드는 commit-reveal로 합의(<see cref="MultiplayerTurnRunner"/>): 양쪽 nonce XOR.
/// 어느 클라도 상대 nonce를 보기 전 commit(해시)하므로 시드 조작 불가.
///
/// 알고리즘은 splitmix64 — 플랫폼/런타임 무관하게 동일 결과(System.Random 구현 의존 회피).
/// </summary>
public static class MatchRandom
{
    static ulong s_state;
    static bool  s_seeded;

    public static bool IsSeeded => s_seeded;

    /// <summary>스트림 전진 횟수. Range(n)은 n&lt;=1이면 전진하지 않으므로 이 값이 곧 '실제 소비 횟수'.
    /// 양 클라가 같은 시점에 같은 값이어야 함 — 어긋나면 그 순간부터 영구 divergence.
    /// 테스트 assert용 + 멀티 desync 카나리아용(현재 divergence 탐지 수단이 이것뿐).</summary>
    public static int DrawCount { get; private set; }

    public static void Seed(ulong _seed)
    {
        s_state   = _seed == 0 ? 0x9E3779B97F4A7C15UL : _seed;  // 0-state 회피
        s_seeded  = true;
        DrawCount = 0;
    }

    /// <summary>싱글플레이용 로컬 랜덤 시드.</summary>
    public static void SeedRandomLocal() => Seed(ReadU64(NewNonce()));

    public static void Reset()
    {
        s_state   = 0;
        s_seeded  = false;
        DrawCount = 0;
    }

    // splitmix64
    static ulong NextU64()
    {
        DrawCount++;
        s_state += 0x9E3779B97F4A7C15UL;
        ulong z = s_state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
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

    /// <summary>nonce의 SHA256 (32바이트 commit).</summary>
    public static byte[] Hash(byte[] _nonce)
    {
        using (var t_sha = SHA256.Create())
            return t_sha.ComputeHash(_nonce);
    }

    /// <summary>공개된 nonce가 앞서 받은 commit과 일치하는지 검증.</summary>
    public static bool VerifyCommit(byte[] _nonce, byte[] _commit)
    {
        if (_nonce == null || _commit == null) return false;
        return BytesEqual(Hash(_nonce), _commit);
    }

    /// <summary>8바이트를 big-endian ulong으로. 양 클라 동일 변환 보장.</summary>
    public static ulong ReadU64(byte[] _b)
    {
        ulong t_v = 0;
        for (int i = 0; i < 8; i++)
            t_v = (t_v << 8) | _b[i];
        return t_v;
    }

    static bool BytesEqual(byte[] _a, byte[] _b)
    {
        if (_a == null || _b == null || _a.Length != _b.Length) return false;
        int t_diff = 0;
        for (int i = 0; i < _a.Length; i++)
            t_diff |= _a[i] ^ _b[i];
        return t_diff == 0;
    }
}
