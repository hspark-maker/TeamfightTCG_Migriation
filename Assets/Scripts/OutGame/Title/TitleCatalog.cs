using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>칭호의 표시 정보와 영구 식별자.</summary>
[Serializable]
public class TitleEntry
{
    [Tooltip("세이브에 저장되는 영구 키. 표시 이름을 바꿔도 이 값은 유지한다.")]
    public string id;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    public Color color = Color.white;
}

/// <summary>칭호 선택창과 프로필이 공유하는 표시 목록.</summary>
[CreateAssetMenu(fileName = "TitleCatalog", menuName = "Card Battle/Title Catalog")]
public class TitleCatalog : ScriptableObject
{
    [Tooltip("선택창 표시 순서. 소지 여부는 유저 세이브에서 별도로 읽는다.")]
    [SerializeField] List<TitleEntry> entries = new List<TitleEntry>();

    public IReadOnlyList<TitleEntry> Entries => entries != null ? (IReadOnlyList<TitleEntry>)entries : Array.Empty<TitleEntry>();

    public bool TryGet(string _id, out TitleEntry _entry)
    {
        _entry = null;
        if (string.IsNullOrEmpty(_id) || entries == null) return false;
        for (int t_i = 0; t_i < entries.Count; t_i++)
        {
            TitleEntry t_entry = entries[t_i];
            if (t_entry == null || t_entry.id != _id) continue;
            _entry = t_entry;
            return true;
        }
        return false;
    }
}
