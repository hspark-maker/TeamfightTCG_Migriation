using System.Collections.Generic;

/// <summary>셔플이 끝난 뒤의 초기 보드 순서(슬롯 0..2 → 대기열)를 소유자별로 붙잡아 둔다.
///
/// 서버 재시뮬레이션이 이 순서를 시드로 산출할 수 없기 때문에 필요하다 — 멀티 초기화 경로 중
/// <c>GameInitializer</c>의 레거시 갈래는 <see cref="ShufflePolicy.Local"/>(UnityEngine.Random)로 섞어
/// 매치 시드와 무관한 배치가 나온다. 그래서 클라가 결과 제출에 이 순서를 실어 보내고,
/// 서버는 양쪽 제출이 같은지로만 신뢰를 세운다(한쪽이 거짓말하면 대조에서 걸린다).
///
/// **규칙이 아니다.** 여기 담긴 값은 아무것도 결정하지 않는다 — 이미 벌어진 배치의 기록일 뿐이다.
/// 전투 중에는 변하지 않는다(초기 배치 1회).</summary>
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
