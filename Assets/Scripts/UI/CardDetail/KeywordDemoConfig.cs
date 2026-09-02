using System;
using UnityEngine;

/// <summary>키워드 데모 무대에 세울 상대·이웃 배역표(대본은 코드가 쥔다).</summary>
[CreateAssetMenu(fileName = "KeywordDemoConfig", menuName = "Card Battle/Keyword Demo Config")]
public class KeywordDemoConfig : ScriptableObject
{
    /// <summary>키워드 하나의 상대 배역. 비운 칸은 기본값으로 떨어진다.</summary>
    [Serializable]
    public struct Entry
    {
        public CardKeyword keyword;

        [Tooltip("맞은편. 대개 맞는 쪽이고, 도발에서만 치러 오는 쪽이 된다. 비우면 defaultOpponent.")]
        [CardId] public int opponentId;

        [Tooltip("곁에 서는 쪽(무쌍의 광역 대상, 도발이 대신 맞아주는 아군, 힐러가 살리는 아군). 비우면 defaultNeighbor. " +
                 "설 자리(윗줄=적/아랫줄=아군)는 코드가 정한다.")]
        [CardId] public int neighborId;
    }

    [Header("기본 배역")]
    [Tooltip("특별할 것 없는 상대. 튀는 카드를 고르면 공격자의 연출이 묻힌다.")]
    [SerializeField, CardId] int defaultOpponentId;

    [Tooltip("곁에 서는 카드. 위와 다른 카드여야 한다.")]
    [SerializeField, CardId] int defaultNeighborId;

    [Header("키워드별 덮어쓰기 (선택)")]
    [SerializeField] Entry[] entries;

    /// <summary>이 키워드의 배역을 낸다. 미저작 칸은 기본값으로 떨어진다.</summary>
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
