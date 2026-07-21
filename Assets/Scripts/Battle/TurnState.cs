/// <summary>
/// 턴 진행 중 입력 게이팅 상태 (게임 규칙).
/// 이전엔 CardView(뷰)의 static 필드에 흩어져 있던 것을 분리한 단일 권위체.
/// 쓰기: 턴 로직(PlayerTurn / MultiplayerPlayerTurn 등). 조회: CardView 입력 판정, 뷰 표시.
/// 씬 종료 시 Reset() (CardView.Cleanup에서 호출).
/// </summary>
public static class TurnState
{
    /// <summary>로컬 플레이어 입력 허용 여부. false면 카드 조작 차단.</summary>
    public static bool InputAllowed { get; set; }

    /// <summary>연속 공격 강제 대상. null 아니면 이 카드만 조작 가능.</summary>
    public static CardInstance ForcedAttacker { get; set; }

    /// <summary>로컬 플레이어 ownerIndex. 싱글=0, 멀티P2=1.</summary>
    public static int LocalOwnerIndex { get; set; }

    public static void Reset()
    {
        InputAllowed    = false;
        ForcedAttacker  = null;
        LocalOwnerIndex = 0;
    }
}
