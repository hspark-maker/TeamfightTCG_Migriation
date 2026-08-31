using System;

namespace TeamfightTCG.BattleCore
{
    /// <summary>플랫폼과 런타임 구현에 영향받지 않는 splitmix64 스트림.</summary>
    public struct DeterministicRandom
    {
        const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

        ulong state;

        public bool IsSeeded { get; private set; }
        public int DrawCount { get; private set; }

        public void Seed(ulong _seed)
        {
            this.state = NormalizeSeed(_seed);
            this.IsSeeded = true;
            this.DrawCount = 0;
        }

        public void Reset() => this = default;

        public int Range(int _maxExclusive)
        {
            if (_maxExclusive <= 1) return 0;
            return (int)(NextU64() % (ulong)_maxExclusive);
        }

        public int Range(int _minInclusive, int _maxExclusive)
            => _minInclusive + Range(_maxExclusive - _minInclusive);

        public ulong NextU64()
        {
            this.DrawCount++;
            this.state += GoldenGamma;
            return Mix(this.state);
        }

        public static ulong DeriveDeckSeed(ulong _initialSeed, int _ownerIndex)
        {
            ulong t_seed = _initialSeed ^ (0xD1B54A32D192ED03UL * (ulong)(_ownerIndex + 1));
            return Mix(t_seed + GoldenGamma);
        }

        static ulong NormalizeSeed(ulong _seed) => _seed == 0 ? GoldenGamma : _seed;

        static ulong Mix(ulong _value)
        {
            ulong t_z = _value;
            t_z = (t_z ^ (t_z >> 30)) * 0xBF58476D1CE4E5B9UL;
            t_z = (t_z ^ (t_z >> 27)) * 0x94D049BB133111EBUL;
            return t_z ^ (t_z >> 31);
        }
    }
}
