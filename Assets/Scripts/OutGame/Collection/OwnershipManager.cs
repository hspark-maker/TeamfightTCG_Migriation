using System;
using System.Collections.Generic;

// 카드 소유권의 static 단일 창구
public static class OwnershipManager
{
    static readonly HashSet<string> s_owned = new HashSet<string>();

    // 소유 변경 통지 — UI 갱신용
    public static event Action OnOwnershipChanged;

    public static int OwnedCount => s_owned.Count;

    // 외부 변조 차단용 스냅샷(라이브 뷰 아님)
    public static IReadOnlyCollection<string> OwnedKeys => new List<string>(s_owned);

    // 메모리 캐시(Init) 없이 세이브의 소유 여부만 조회 — 첫실행 판정용
    public static bool HasAnyOwnedSaved()
    {
        var t_data = DataSaveManager.Data.ownership;
        if (t_data.ownedCardKeys == null) return false;

        foreach (var t_key in t_data.ownedCardKeys)
        {
            if (!string.IsNullOrEmpty(t_key)) return true;
        }
        return false;
    }

    // 부트에서 DataSaveManager.Load()·CardCatalog.SetSource() 이후 1회 호출
    public static void Init()
    {
        s_owned.Clear();
        bool t_removedUnavailable = false;

        var t_data = DataSaveManager.Data.ownership;
        if (t_data.ownedCardKeys != null)
        {
            foreach (var t_key in t_data.ownedCardKeys)
            {
                if (string.IsNullOrEmpty(t_key)) continue;
                if (!CardCatalog.IsReady || CardCatalog.Get(t_key) != null) s_owned.Add(t_key);
                else t_removedUnavailable = true;
            }
        }

        if (t_removedUnavailable) Save();
    }

    // 메모리 소유 집합을 세이브 슬롯에 flush 후 영속화
    public static void Save()
    {
        var t_data = DataSaveManager.Data.ownership;
        t_data.ownedCardKeys = new List<string>(s_owned);
        DataSaveManager.Save();
    }

    public static bool IsOwned(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return false;

        return s_owned.Contains(_key);
    }

    public static bool IsOwned(CardData _card) => IsOwned(CardCatalog.KeyOf(_card));

    // 카드 1장 지급 — 신규 지급이면 true
    public static bool Grant(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return false;
        if (!s_owned.Add(_key)) return false;

        Save();
        OnOwnershipChanged?.Invoke();
        return true;
    }

    // 카드 1장 회수(디버그용) — 실제 제거 시 true
    public static bool Revoke(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return false;
        if (!s_owned.Remove(_key)) return false;

        Save();
        OnOwnershipChanged?.Invoke();
        return true;
    }

    // 여러 키 일괄 지급(Save·이벤트 1회) — 신규 지급 장수 반환
    public static int GrantAll(IEnumerable<string> _keys)
    {
        if (_keys == null) return 0;

        int t_added = 0;
        foreach (var t_key in _keys)
        {
            if (string.IsNullOrEmpty(t_key)) continue;
            if (s_owned.Add(t_key)) t_added++;
        }
        if (t_added == 0) return 0;

        Save();
        OnOwnershipChanged?.Invoke();
        return t_added;
    }

    // 카탈로그 전량 지급 — 신규 지급 장수 반환("모든 카드" 정의의 단일 창구)
    public static int GrantEntireCatalog()
    {
        if (!CardCatalog.IsReady)
        {
            UnityEngine.Debug.LogWarning("[Ownership] CardCatalog 미초기화 — 부트(BootInstaller)를 거치지 않은 씬에서는 전체 해금이 동작하지 않는다.");
            return 0;
        }

        var t_keys = new List<string>(CardCatalog.Count);
        foreach (var t_card in CardCatalog.All)
        {
            t_keys.Add(CardCatalog.KeyOf(t_card));
        }

        return GrantAll(t_keys);
    }

    // 전체 회수(디버그용) — 제거된 장수 반환
    public static int RevokeAll()
    {
        int t_removed = s_owned.Count;
        if (t_removed == 0) return 0;

        s_owned.Clear();
        Save();
        OnOwnershipChanged?.Invoke();
        return t_removed;
    }
}
