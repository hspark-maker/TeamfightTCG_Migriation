using System.Collections.Generic;
using Firebase.Firestore;

// 재화 세이브 값 객체 — 잔액은 ECurrencyType 이름을 키로 하는 맵이다(enum 선언 순서에 묶이지 않는다)
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class CurrencySaveData
{
    [FirestoreProperty("balances")] public Dictionary<string, long> Balances { get; set; } = new Dictionary<string, long>();

    /// <summary>알려진 재화 키가 빠져 있으면 0으로 채운다 — 잔액 조회가 키 존재 여부를 따지지 않게 한다.</summary>
    public void Normalize()
    {
        if (Balances == null) Balances = new Dictionary<string, long>();

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
        {
            string t_key = ((ECurrencyType)t_i).ToString();
            if (!Balances.ContainsKey(t_key)) Balances[t_key] = 0;
        }
    }
}
