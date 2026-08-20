using UnityEngine;

// 매칭 화면에 세우는 한 사람의 표시값 스냅샷(나·상대 공용)
public readonly struct MatchProfile
{
    // 닉네임 저장이 아직 없다 — 닉네임 설정 화면이 붙으면 이 상수 자리만 갈아끼운다.
    public const string LOCAL_NICKNAME = "나";

    public readonly string Nickname;
    public readonly int    TierIndex;
    public readonly string RankName;

    // 스프라이트는 null을 허용한다 — 뷰가 프리팹에 저작된 스프라이트를 그대로 유지한다.
    public readonly Sprite RankBadge;
    public readonly Sprite Avatar;

    public MatchProfile(string _nickname, int _tierIndex, string _rankName, Sprite _rankBadge, Sprite _avatar)
    {
        Nickname  = _nickname != null ? _nickname : string.Empty;
        TierIndex = _tierIndex;
        RankName  = _rankName != null ? _rankName : string.Empty;
        RankBadge = _rankBadge;
        Avatar    = _avatar;
    }

    // 내 프로필 1회 스냅샷. 랭크 표시는 로비와 같은 창구(RankManager)를 써서 두 화면이 갈리지 않게 한다.
    public static MatchProfile OfLocalPlayer()
    {
        RankInfo t_rank = RankManager.GetInfo();
        return new MatchProfile(LOCAL_NICKNAME, t_rank.TierIndex, t_rank.DisplayName, t_rank.Badge, null);
    }

    // 페이크 매칭의 상대. 랭크 표시를 내 것에서 그대로 가져오는 이유: 상대의 덱과 카드 레벨이 실제로
    // 내 티어 기준으로 뽑히므로(AIDeckConfig·AiCardLevelOf), 표시만 티어표에서 따로 읽으면
    // 언랭크 유저에게 "나=언랭크 / 상대=브론즈 1"로 짝짝이가 된다.
    public static MatchProfile OfOpponent(string _nickname, Sprite _avatar)
    {
        RankInfo t_rank = RankManager.GetInfo();
        return new MatchProfile(_nickname, t_rank.TierIndex, t_rank.DisplayName, t_rank.Badge, _avatar);
    }

    // 토너먼트 정점의 고정 상대. 랭크를 비우는 이유: 덱도 카드 레벨도 정점 저작값이라 내 티어와 무관하고,
    // 이 전투는 랭크에 반영되지도 않는다 — 배지를 붙이면 없는 랭크전을 있는 것처럼 보이게 한다.
    public static MatchProfile OfTournamentNode(string _name, Sprite _avatar)
        => new MatchProfile(_name, 0, string.Empty, null, _avatar);
}
