using System;
using UnityEngine;

/// <summary>키워드 데모 무대에 **누구를 세울지**. 대본(무엇을 하는지)은 코드가 쥔다 —
/// 키워드마다 분기가 제각각이라 데이터로 뺄 수 있는 축이 아니다.
///
/// 앞자리는 여기 없다. 그 자리는 언제나 **방금 강화한 그 카드**다 —
/// 데모의 목적이 "이 카드가 이렇게 싸운다"라서, 남의 카드를 세우면 배운 것이 내 카드로 이어지지 않는다.
/// (도발만 그 카드가 맞는 쪽이 된다 — 남이 나를 치게 만드는 키워드라 배역이 뒤집힌다.)</summary>
[CreateAssetMenu(fileName = "KeywordDemoConfig", menuName = "Card Battle/Keyword Demo Config")]
public class KeywordDemoConfig : ScriptableObject
{
    /// <summary>키워드 하나의 상대 배역. 비운 칸은 아래 기본값으로 떨어진다 —
    /// 전부 저작하게 만들면 키워드가 늘 때마다 빈 칸이 조용히 생긴다.</summary>
    [Serializable]
    public struct Entry
    {
        public CardKeyword keyword;

        [Tooltip("맞은편. 대개 맞는 쪽이고, 도발에서만 치러 오는 쪽이 된다. 비우면 defaultOpponent.")]
        [CardId] public int opponentId;

        [Tooltip("곁에 서는 쪽(무쌍의 광역 대상, 도발이 대신 맞아주는 아군, 힐러가 살리는 아군). 비우면 defaultNeighbor. " +
                 "이 카드가 설 자리는 코드가 정한다 — 무대에서는 줄이 곧 편이라, 무쌍의 대상은 윗줄(적 자리)에 " +
                 "서고 도발이 지켜주는 아군과 힐러가 살리는 아군은 아랫줄(아군 자리)에 선다. " +
                 "그러니 같은 칸에 어떤 카드를 넣어도 편이 뒤집히지 않는다.")]
        [CardId] public int neighborId;
    }

    [Header("기본 배역")]
    [Tooltip("특별할 것 없는 상대. 튀는 카드(키워드가 많거나 아트가 화려한 것)를 고르면 " +
             "정작 봐야 할 공격자의 연출이 묻힌다.")]
    [SerializeField, CardId] int defaultOpponentId;

    [Tooltip("곁에 서는 카드. 위와 **다른 카드**여야 한다 — 같으면 무쌍의 광역이 어디로 갔는지 안 읽힌다.")]
    [SerializeField, CardId] int defaultNeighborId;

    [Header("키워드별 덮어쓰기 (선택)")]
    [SerializeField] Entry[] entries;

    /// <summary>이 키워드의 배역. 미저작이면 기본값, 그것도 없으면 null —
    /// 호출부는 null을 "그 자리를 비운다"로 읽고 조용히 건너뛴다.</summary>
    public void Roles(CardKeyword _keyword, out int _opponentId, out int _neighborId)
    {
        _opponentId = this.defaultOpponentId;
        _neighborId = this.defaultNeighborId;

        if (this.entries == null) return;

        foreach (Entry t_e in this.entries)
        {
            if (t_e.keyword != _keyword) continue;

            if (t_e.opponentId > 0) _opponentId = t_e.opponentId;
            if (t_e.neighborId > 0) _neighborId = t_e.neighborId;
            return;
        }
    }
}
