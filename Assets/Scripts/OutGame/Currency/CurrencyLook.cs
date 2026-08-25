using System;
using System.Collections.Generic;
using UnityEngine;

// 재화 아이콘·표시명 한 장. 재화 그림이 갈리는 UI는 전부 여기만 본다.
// 아이콘 폴백은 null이라 표를 안 꽂아도 현행 화면이 그대로 산다(호출부가 null이면 프리팹 그림을 유지).
[CreateAssetMenu(fileName = "CurrencyLook", menuName = "Card Battle/Currency Look")]
public class CurrencyLook : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("이 줄이 꾸밀 재화. 같은 재화가 여러 줄이면 위쪽 줄이 이긴다.")]
        public ECurrencyType type;

        [Tooltip("비워두면(None) 아이콘을 바꾸지 않는다 — 프리팹에 저작된 그림이 그대로 남는다.")]
        public Sprite icon;

        [Tooltip("상단바 재화 칸에 들어갈 그림. 상단바 아트는 보상 슬롯과 결이 달라 위 icon과 다른 파일을 쓴다 — " +
                 "그래서 폴백 없이 이 칸만 본다.\n\n" +
                 "비워두면(None) 그 재화는 상단바에 뜰 수 없다. 칸을 빌리지도, 빌려주지도 않는다 " +
                 "— 그림과 숫자가 어긋나느니 그 재화의 획득 연출을 건너뛴다.")]
        public Sprite barIcon;

        [Tooltip("비워두면 코드 기본 이름(골드/다이아/에너지/강화 조각)으로 떨어진다.")]
        public string displayName;
    }

    static CurrencyLook s_active;
    static Entry[] s_baked;

    [SerializeField] List<Entry> entries = new List<Entry>();

    // 부트에서 1회 주입. 미배선(null)이면 전부 폴백으로 동작한다
    public static void SetActive(CurrencyLook _look)
    {
        s_active = _look;
        s_baked = null;
    }

    // 미배선·미저작이면 null (호출부는 프리팹 그림을 그대로 둔다)
    public static Sprite IconOf(ECurrencyType _type)
    {
        Entry t_entry = EntryOf(_type);
        return t_entry != null ? t_entry.icon : null;
    }

    /// <summary>상단바 재화 칸에 쓸 그림. 미저작이면 null — 그 재화는 상단바 칸을 받지 못한다.
    /// icon으로 폴백하지 않는다(보상 슬롯 그림이 상단바에 섞이는 것이 정확히 막으려는 것이다).</summary>
    public static Sprite BarIconOf(ECurrencyType _type)
    {
        Entry t_entry = EntryOf(_type);
        return t_entry != null ? t_entry.barIcon : null;
    }

    // 문장이 깨지지 않게 null 폴백을 쓰지 않는다 — 미배선이면 코드 기본 이름
    public static string NameOf(ECurrencyType _type)
    {
        Entry t_entry = EntryOf(_type);
        if (t_entry != null && !string.IsNullOrEmpty(t_entry.displayName)) return t_entry.displayName;

        return DefaultNameOf(_type);
    }

    // 첫 조회에 Count 길이 배열로 한 번 굽는다(조회마다 목록을 훑지 않게)
    static Entry EntryOf(ECurrencyType _type)
    {
        if (s_active == null) return null;

        int t_index = (int)_type;
        if (t_index < 0 || t_index >= (int)ECurrencyType.Count) return null;

        if (s_baked == null)
        {
            s_baked = new Entry[(int)ECurrencyType.Count];

            List<Entry> t_entries = s_active.entries;
            if (t_entries != null)
            {
                for (int t_i = 0; t_i < t_entries.Count; t_i++)
                {
                    Entry t_entry = t_entries[t_i];
                    if (t_entry == null) continue;

                    int t_slot = (int)t_entry.type;
                    if (t_slot < 0 || t_slot >= s_baked.Length) continue;
                    if (s_baked[t_slot] != null) continue;

                    s_baked[t_slot] = t_entry;
                }
            }
        }

        return s_baked[t_index];
    }

    static string DefaultNameOf(ECurrencyType _type)
    {
        switch (_type)
        {
            case ECurrencyType.Gold:    return "골드";
            case ECurrencyType.Diamond: return "다이아";
            case ECurrencyType.Energy:  return "에너지";
            case ECurrencyType.Shard:   return "강화 조각";
            default:                    return _type.ToString();   // 새 재화를 여기 안 적으면 영문 이름이 그대로 보인다(주어 없는 문장 방지)
        }
    }
}
