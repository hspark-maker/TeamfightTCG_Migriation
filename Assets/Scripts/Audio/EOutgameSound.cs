/// <summary>로비·아웃게임에서 벌어지는 사건 하나하나의 소리 이름.
/// 실제 클립 배정은 코드가 아니라 <see cref="SoundBank"/> 에셋이 갖는다.</summary>
// 값을 손으로 박아 둔다 — SoundBank 에셋이 이 값을 정수로 직렬화하므로 중간에 하나 끼우면 배선이 밀린다.
public enum EOutgameSound
{
    None = 0,

    // 탭·버튼
    TabTurn     = 100,
    ButtonPress = 101,
    PopupOpen   = 102,
    PopupClose  = 103,

    // 카드팩
    PackOpenBegin = 200,
    PackTear      = 201,
    PackCardFlick = 202,
    PackSummary   = 203,

    // 강화·진화
    EnhanceCharge  = 300,
    EnhanceSuccess = 301,
    EnhanceFail    = 302,
    EvolveBurst    = 303,

    // 도감
    AlbumPageTurn = 400,
    AlbumCardSeat = 401,
    AlbumFanfare  = 402,

    // 보상
    RewardClaim  = 500,
    CurrencyGain = 501,

    // 랭크
    RankStarFill = 600,
    RankPromote  = 601,

    // 모험
    AdventureNodeTap = 700,
    AdventureClear   = 701,

    // 매칭
    MatchSearch = 800,
    MatchFound  = 801,
    MatchVersus = 802,
}
