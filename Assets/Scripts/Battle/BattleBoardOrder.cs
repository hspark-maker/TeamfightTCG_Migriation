using System.Collections.Generic;

/// <summary>셔플이 끝난 뒤의 초기 보드 순서(슬롯 0..2 → 대기열)를 소유자별로 붙잡아 둔다.
///
/// 서버 재시뮬레이션이 모든 클라 셔플을 시드로 산출할 수 없기 때문에 필요하다 — 초기화 경로 중
/// <c>GameInitializer</c>의 싱글·튜토리얼 갈래는 <see cref="ShufflePolicy.Match"/>(클라가 만든 로컬 시드)로 섞어
/// 매치 시드와 무관한 배치가 나온다. PvP는 양쪽 제출 대조로 신뢰를 세우고, 새 AI전은 서버가
/// 미리 봉인한 순서를 클라가 그대로 배치한다.
///
/// 전투 중에는 변하지 않는 초기 배치 1회 스냅샷이다.</summary>
public static class BattleBoardOrder
{
    static readonly Dictionary<int, int[]> s_orders = new Dictionary<int, int[]>();

    /// <summary>초기 배치 확정 직후 1회. 호출 지점은 BattleField.Initialize 하나다.</summary>
    public static void Capture(int _ownerIndex, IReadOnlyList<int> _shuffled)
    {
        if (_shuffled == null) return;
        var t_order = new int[_shuffled.Count];
        for (int i = 0; i < _shuffled.Count; i++) t_order[i] = _shuffled[i];
        s_orders[_ownerIndex] = t_order;
    }

    /// <summary>없으면 빈 배열. 제출 쪽에서 길이 0을 "미기록"으로 읽는다.</summary>
    public static int[] For(int _ownerIndex)
        => s_orders.TryGetValue(_ownerIndex, out int[] t_order) ? t_order : System.Array.Empty<int>();

    public static void Reset() => s_orders.Clear();
}
