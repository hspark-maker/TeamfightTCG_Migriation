using UnityEngine;

/// <summary>
/// 처형(Execution) 발동 연출. 처치에 성공해 **재공격 권한이 열린 순간**에 한 번 재생한다:
/// 처형자 카드 자리에 마법진 한 번.
///
/// 프리팹 = BattleVfxLibrary(ExecutionCircle).
/// 발동 판정은 여기 없다 — AttackResult.attackerKeywords에 Execution이 섰는지가 유일한 기준이고,
/// 그 판정은 AttackProcessor 한 곳이 소유한다(연출이 규칙을 다시 계산하면 둘이 갈라진다).
///
/// **순수 연출**이다. 상태를 바꾸지 않고 대기시키지도 않는다 — 재공격 입력 창이 이 연출을 기다리면
/// 두 클라의 턴 길이가 프레임레이트만큼 갈라진다.
/// </summary>
public static class ExecutionVfx
{
    /// <summary>_view가 없거나 프리팹이 미배선이면 조용히 무동작(호출부에 널 분기가 늘지 않게).</summary>
    public static void Play(CardView _view)
    {
        if (_view == null) return;

        BattleVfx.Play(BattleVfxId.ExecutionCircle, _view.transform.position, _view.VfxSortingLayerId);
    }
}
