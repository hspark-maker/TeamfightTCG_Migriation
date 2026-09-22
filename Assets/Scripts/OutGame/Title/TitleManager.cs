using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>서버가 지급한 칭호의 소유 조회와 장착.</summary>
public static class TitleManager
{
    static readonly HashSet<string> s_ownedIds = new HashSet<string>();
    static string s_equippedId = string.Empty;

    public static event Action OnChanged;
    public static TitleCatalog Catalog { get; private set; }
    public static IReadOnlyList<string> OwnedIds => Slot.OwnedTitleIds != null
        ? Slot.OwnedTitleIds.AsReadOnly() : Array.Empty<string>();
    public static string EquippedId => CanEquip(Slot.EquippedTitleId) ? Slot.EquippedTitleId : string.Empty;

    static ProfileSaveData Slot => DataSaveManager.Data.Profile;

    public static void SetCatalog(TitleCatalog _catalog)
    {
        Catalog = _catalog;
    }

    public static bool IsOwned(string _id)
    {
        return !string.IsNullOrEmpty(_id) && Slot.OwnedTitleIds != null && Slot.OwnedTitleIds.Contains(_id);
    }

    public static bool CanEquip(string _id)
    {
        return IsOwned(_id) && Catalog != null && Catalog.TryGet(_id, out _);
    }

    public static bool TryEquip(string _id)
    {
        string t_id = _id ?? string.Empty;
        if (t_id.Length > 0 && !CanEquip(t_id)) return false;
        if ((Slot.EquippedTitleId ?? string.Empty) == t_id) return true;
        Slot.EquippedTitleId = t_id;
        DataSaveManager.Save();
        NotifyRehydrated();
        return true;
    }

    /// <summary>서버 채택을 알리며 저장 요청은 발생시키지 않는다.</summary>
    public static void NotifyRehydrated()
    {
        IReadOnlyList<string> t_ids = OwnedIds;
        string t_equipped = EquippedId;
        bool t_changed = !s_ownedIds.SetEquals(t_ids) || s_equippedId != t_equipped;
        s_ownedIds.Clear();
        s_ownedIds.UnionWith(t_ids);
        s_equippedId = t_equipped;
        if (t_changed) OnChanged?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Catalog = null;
        s_ownedIds.Clear();
        s_equippedId = string.Empty;
        OnChanged = null;
    }
}
