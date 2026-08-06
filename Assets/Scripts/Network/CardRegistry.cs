using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CardData SO ↔ int ID 매핑. 양쪽 클라이언트가 동일한 allCards 리스트 순서를 가져야 함.
///
/// **게임에 존재하는 카드 전체 목록의 단일 진실원이기도 하다.** 덱 편성 컬렉션(DeckBuilderUI),
/// 세이브 복원(DeckSaveManager)이 전부 이 목록을 참조한다 — 씬 컴포넌트가 카드 목록을 따로
/// 들고 있으면 카드 추가 시 한쪽만 갱신되어 조용히 어긋난다(실제로 그랬음).
///
/// **배열 인덱스가 곧 와이어 ID다.** 추가는 항상 맨 뒤에만. 중간 삽입·삭제·재정렬은
/// 뒤쪽 카드 ID를 전부 밀어 양 클라 해석이 갈린다 = 즉시 divergence.
/// </summary>
[CreateAssetMenu(fileName = "CardRegistry", menuName = "Card Battle/Card Registry")]
public class CardRegistry : ScriptableObject
{
    [SerializeField] CardData[] allCards;

    /// <summary>등록된 카드 전체(등록 순서 = ID 순서). null 칸이 섞여 있을 수 있으니 소비측에서 걸러라.</summary>
    public IReadOnlyList<CardData> All => this.allCards ?? System.Array.Empty<CardData>();

    /// <summary>와이어 ID 원본은 유지한 채 현재 실행 프로필에 노출할 카드만 반환한다.</summary>
    public IEnumerable<CardData> Available(bool _includeTestCards)
    {
        foreach (CardData t_card in All)
        {
            if (t_card == null) continue;
            if (_includeTestCards || t_card.channel == ECardChannel.Live)
                yield return t_card;
        }
    }

    readonly Dictionary<CardData, int> dataToId = new Dictionary<CardData, int>();
    readonly Dictionary<int, CardData> idToData  = new Dictionary<int, CardData>();

    public void Initialize()
    {
        this.dataToId.Clear();
        this.idToData.Clear();
        for (int i = 0; i < this.allCards.Length; i++)
        {
            if (this.allCards[i] == null) continue;
            this.dataToId[this.allCards[i]] = i;
            this.idToData[i]                = this.allCards[i];
        }
    }

    public int GetId(CardData _data)
    {
        if (_data != null && this.dataToId.TryGetValue(_data, out int t_id)) return t_id;
        return -1;
    }

    public CardData GetData(int _id)
    {
        this.idToData.TryGetValue(_id, out CardData t_data);
        return t_data;
    }
}
