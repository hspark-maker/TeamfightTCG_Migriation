using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CardData SO ↔ int ID 매핑. 양쪽 클라이언트가 동일한 allCards 리스트 순서를 가져야 함.
/// </summary>
[CreateAssetMenu(fileName = "CardRegistry", menuName = "Card Battle/Card Registry")]
public class CardRegistry : ScriptableObject
{
    [SerializeField] CardData[] allCards;

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
