/// <summary>
/// 입력 제스처 종류. 튜토리얼이 "이번 스텝은 이 조작만" 게이팅할 때 쓰는 단일 어휘.
/// SO는 int 직렬화 → 새 값은 반드시 끝에 추가. Any(0) = 제한 없음(일반 전투 기본).
/// </summary>
public enum InputGesture
{
    Any,            // 제한 없음
    DragUp,         // 위로 드래그 = 적에게 끌어 공격
    DragDown,       // 아래로 드래그 = 좌우 조준 후 공격
    Tap,            // 탭 = 내 카드 무장 → 적 카드 탭
    LongPressOnly,  // 롱프레스(정보 확인)만. 드래그·탭 전부 차단
}

/// <summary>
/// 턴 진행 중 입력 게이팅 상태 (게임 규칙).
/// 이전엔 CardView(뷰)의 static 필드에 흩어져 있던 것을 분리한 단일 권위체.
/// 쓰기: 턴 로직(PlayerTurn / MultiplayerPlayerTurn 등). 조회: CardView 입력 판정, 뷰 표시.
/// 씬 종료 시 Reset() (BattleCleanup.Run에서 호출).
/// </summary>
public static class TurnState
{
    /// <summary>로컬 플레이어 입력 허용 여부. false면 카드 조작 차단.</summary>
    public static bool InputAllowed { get; set; }

    /// <summary>연속 공격 강제 대상. null 아니면 이 카드만 조작 가능.</summary>
    public static CardInstance ForcedAttacker { get; set; }

    /// <summary>튜토리얼: 지정 공격 타깃(적 카드). null 아니면 이 적만 밝게, 나머지 적은 암전(집중 유도).</summary>
    public static CardInstance ForcedTarget { get; set; }

    /// <summary>로컬 플레이어 ownerIndex. 싱글=0, 멀티P2=1.</summary>
    public static int LocalOwnerIndex { get; set; }

    /// <summary>튜토리얼: 이번 스텝에 허용된 제스처. Any면 무제한(일반 전투). 조회는 CardView 입력 판정.</summary>
    public static InputGesture AllowedGesture { get; set; }

    /// <summary>지금 진행 중인 턴(_current = ownerIndex)이 로컬 플레이어 턴인가.
    /// 싱글/멀티 분기가 필요 없다 — 싱글은 LocalOwnerIndex가 0이고 플레이어 필드 ownerIndex도 0이다.
    /// (이전엔 TurnRunner가 DeckConfig.IsMultiplayer로 갈라 같은 규칙을 두 벌로 들고 있었다.)</summary>
    public static bool IsLocalTurn(int _current) => _current == LocalOwnerIndex;

    public static void Reset()
    {
        InputAllowed    = false;
        ForcedAttacker  = null;
        ForcedTarget    = null;
        LocalOwnerIndex = 0;
        AllowedGesture  = InputGesture.Any;
    }
}
