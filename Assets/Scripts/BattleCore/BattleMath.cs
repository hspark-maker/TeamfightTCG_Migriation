using System;

namespace TeamfightTCG.BattleCore
{
    /// <summary>Unity Mathf 없이 기존 float→int 규칙을 보존하는 전투 산술.</summary>
    public static class BattleMath
    {
        public static int FloorToInt(float _value) => (int)Math.Floor(_value);
        public static int Min(int _a, int _b) => _a < _b ? _a : _b;
        public static int Max(int _a, int _b) => _a > _b ? _a : _b;
    }
}
