using System;
using UnityEngine;

/// <summary>키워드 데모 무대에 **누구를 세울지**. 대본(무엇을 하는지)은 코드가 쥔다 —
/// 키워드마다 분기가 제각각이라 데이터로 뺄 수 있는 축이 아니다.
///
/// 공격자는 여기 없다. 그 자리는 언제나 **방금 강화한 그 카드**다 —
/// 데모의 목적이 "이 카드가 이렇게 싸운다"라서, 남의 카드를 세우면 배운 것이 내 카드로 이어지지 않는다.</summary>
[CreateAssetMenu(fileName = "KeywordDemoConfig", menuName = "Card Battle/Keyword Demo Config")]
public class KeywordDemoConfig : ScriptableObject
{
    /// <summary>키워드 하나의 상대 배역. 비운 칸은 아래 기본값으로 떨어진다 —
    /// 전부 저작하게 만들면 키워드가 늘 때마다 빈 칸이 조용히 생긴다.</summary>
    [Serializable]
    public struct Entry
    {
        public CardKeyword keyword;

        [Tooltip("맞는 쪽. 비우면 defaultOpponent.")]
        public CardData opponent;

        [Tooltip("곁에 서는 쪽(무쌍의 광역 대상, 도발이 노리던 카드, 힐러가 살리는 아군). 비우면 defaultNeighbor.")]
        public CardData neighbor;
    }

    [Header("기본 배역")]
    [Tooltip("특별할 것 없는 상대. 튀는 카드(키워드가 많거나 아트가 화려한 것)를 고르면 " +
             "정작 봐야 할 공격자의 연출이 묻힌다.")]
    [SerializeField] CardData defaultOpponent;

    [Tooltip("곁에 서는 카드. 위와 **다른 카드**여야 한다 — 같으면 무쌍의 광역이 어디로 갔는지 안 읽힌다.")]
    [SerializeField] CardData defaultNeighbor;

    [Header("키워드별 덮어쓰기 (선택)")]
    [SerializeField] Entry[] entries;

    /// <summary>이 키워드의 배역. 미저작이면 기본값, 그것도 없으면 null —
    /// 호출부는 null을 "그 자리를 비운다"로 읽고 조용히 건너뛴다.</summary>
    public void Roles(CardKeyword _keyword, out CardData _opponent, out CardData _neighbor)
    {
        _opponent = this.defaultOpponent;
        _neighbor = this.defaultNeighbor;

        if (this.entries == null) return;

        foreach (Entry t_e in this.entries)
        {
            if (t_e.keyword != _keyword) continue;

            if (t_e.opponent != null) _opponent = t_e.opponent;
            if (t_e.neighbor != null) _neighbor = t_e.neighbor;
            return;
        }
    }
}
