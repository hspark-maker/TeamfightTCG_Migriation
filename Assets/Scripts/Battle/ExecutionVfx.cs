using UnityEngine;

/// <summary>
/// 처형(Execution) 발동 연출. 처치에 성공해 **재공격 권한이 열린 순간**에 한 번 재생한다:
/// 처형자 카드 왼쪽 위·오른쪽 위에 스파크 하나씩 + 카드 자리에 마법진 한 번.
///
/// 프리팹 = BattleVfxLibrary(ExecutionSpark / ExecutionCircle), 형태값도 같은 라이브러리.
/// 발동 판정은 여기 없다 — AttackResult.attackerKeywords에 Execution이 섰는지가 유일한 기준이고,
/// 그 판정은 AttackProcessor 한 곳이 소유한다(연출이 규칙을 다시 계산하면 둘이 갈라진다).
///
/// **순수 연출**이다. 상태를 바꾸지 않고 대기시키지도 않는다 — 재공격 입력 창이 이 연출을 기다리면
/// 두 클라의 턴 길이가 프레임레이트만큼 갈라진다.
/// </summary>
public static class ExecutionVfx
{
    static readonly Vector2 DEFAULT_OFFSET = new Vector2(0.45f, 0.6f);   // 라이브러리 미배선 시 폴백

    /// <summary>_view가 없거나 프리팹이 미배선이면 조용히 무동작(호출부에 널 분기가 늘지 않게).</summary>
    public static void Play(CardView _view)
    {
        if (_view == null) return;

        int t_layer = _view.VfxSortingLayerId;

        BattleVfxLibrary t_lib = BattleVfx.Library;
        Vector2 t_off = t_lib != null ? t_lib.executionSparkOffset : DEFAULT_OFFSET;

        // 스파크 두 점 = 카드 **왼쪽 위 / 오른쪽 위**. 카드 로컬 축으로 잡으므로 연출 중 카드가 기울면
        // 두 점도 같이 기울어 카드에 붙어 있는 것으로 보인다(월드 축이면 기운 카드에서 따로 논다).
        Transform t_tr    = _view.transform;
        Vector3   t_side  = t_tr.right * t_off.x;
        Vector3   t_up    = t_tr.up    * t_off.y;

        BattleVfx.Play(BattleVfxId.ExecutionSpark, t_tr.position - t_side + t_up, t_layer);
        BattleVfx.Play(BattleVfxId.ExecutionSpark, t_tr.position + t_side + t_up, t_layer);

        BattleVfx.Play(BattleVfxId.ExecutionCircle, t_tr.position, t_layer);
    }
}
