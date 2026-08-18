using System;
using System.Collections.Generic;

// 재화 세이브 값 객체 — 잔액은 인덱스 = (int)ECurrencyType 인 배열로 담는다
// (JsonUtility가 enum 키 딕셔너리를 못 써서 리스트가 유일한 선택)
[Serializable]
public class CurrencySaveData
{
    public const int VERSION = 1;   // 0 = 배열 이전(gold/diamond/energy 3필드 시절)

    public int version = 0;         // 옛 JSON엔 이 필드가 없어 0으로 읽힌다 = 흡수 신호
    // 잔액. 칸 순서가 ECurrencyType 선언 순서에 묶여 있다 — enum 중간에 값을 끼워 넣으면
    // version이 못 잡고 잔액이 다른 재화로 옮겨간다. 새 재화는 반드시 Count 앞에 덧붙일 것.
    public List<long> balances = new List<long>();

    // 배열 이전 필드. 지우면 옛 세이브의 잔액을 잃는다.
    public long gold = 100;
    public long diamond = 0;
    public long energy = 0;

    // 세이브 모양 보정 — 길이 맞추기 + 옛 3필드 1회 흡수(신규 유저의 초기 지급도 이 경로로 들어온다)
    public void Normalize()
    {
        if (balances == null) balances = new List<long>();
        while (balances.Count < (int)ECurrencyType.Count) balances.Add(0);

        if (version >= VERSION) return;

        balances[(int)ECurrencyType.Gold]    = gold;
        balances[(int)ECurrencyType.Diamond] = diamond;
        balances[(int)ECurrencyType.Energy]  = energy;

        gold = 0;
        diamond = 0;
        energy = 0;
        version = VERSION;
    }
}
