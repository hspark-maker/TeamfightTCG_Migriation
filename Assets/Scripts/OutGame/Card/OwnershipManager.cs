using System;
using System.Collections.Generic;

// 카드 소유권의 static 단일 창구
public static class OwnershipManager
{
    static readonly HashSet<int> s_owned = new HashSet<int>();

    // 소유 변경 통지 — UI 갱신용
    public static event Action OnOwnershipChanged;

    public static int OwnedCount => s_owned.Count;

    // 외부 변조 차단용 스냅샷(라이브 뷰 아님)
    public static IReadOnlyCollection<int> OwnedIds => new List<int>(s_owned);

    // 메모리 캐시(Init) 없이 세이브의 소유 여부만 조회 — 첫실행 판정용.
    // 이관 전 구 세이브도 "소유 있음"으로 읽어야 한다 — 아니면 기존 유저가 신규로 오인돼 스타터덱을 다시 받는다.
    public static bool HasAnyOwnedSaved()
    {
        var t_data = DataSaveManager.Data.ownership;

        if (t_data.ownedCardIds != null)
        {
            foreach (var t_id in t_data.ownedCardIds)
            {
                if (t_id > 0) return true;
            }
        }
        if (t_data.ownedCardKeys != null)
        {
            foreach (var t_key in t_data.ownedCardKeys)
            {
                if (!string.IsNullOrEmpty(t_key)) return true;
            }
        }
        return false;
    }

    // 부트에서 DataSaveManager.Load()·CardCatalog.SetSource() 이후 1회 호출
    public static void Init()
    {
        s_owned.Clear();
        bool t_dirty = false;

        var t_data = DataSaveManager.Data.ownership;
        if (t_data.ownedCardIds != null)
        {
            foreach (var t_id in t_data.ownedCardIds)
            {
                if (t_id <= 0) continue;
                if (!CardCatalog.IsReady || CardCatalog.Contains(t_id)) s_owned.Add(t_id);
                else t_dirty = true;
            }
        }

        // 구 세이브(이름 키) 이관 — 카탈로그가 준비된 뒤에만 가능하다. 미준비면 다음 부트로 미룬다.
        if (CardCatalog.IsReady && t_data.ownedCardKeys != null && t_data.ownedCardKeys.Count > 0)
        {
            foreach (var t_key in t_data.ownedCardKeys)
            {
                int t_id = CardCatalog.LegacyIdOfName(t_key);
                if (t_id > 0) s_owned.Add(t_id);
            }
            t_data.ownedCardKeys.Clear();   // 한 번만 옮긴다 — 남겨두면 회수한 카드가 부트마다 되살아난다
            t_dirty = true;
        }

        if (t_dirty) Save();
    }

    // 메모리 소유 집합을 세이브 슬롯에 flush 후 영속화
    public static void Save()
    {
        var t_data = DataSaveManager.Data.ownership;
        t_data.ownedCardIds = new List<int>(s_owned);
        DataSaveManager.Save();
    }

    public static bool IsOwned(int _id)
    {
        if (_id <= 0) return false;

        return s_owned.Contains(_id);
    }

    // 카드 1장 지급 — 신규 지급이면 true
    public static bool Grant(int _id)
    {
        if (_id <= 0) return false;
        if (!s_owned.Add(_id)) return false;

        Save();
        OnOwnershipChanged?.Invoke();
        return true;
    }

    // 여러 번호 일괄 지급(Save·이벤트 1회) — 신규 지급 장수 반환
    public static int GrantAll(IEnumerable<int> _ids)
    {
        if (_ids == null) return 0;

        int t_added = 0;
        foreach (var t_id in _ids)
        {
            if (t_id <= 0) continue;
            if (s_owned.Add(t_id)) t_added++;
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
            UnityEngine.Debug.LogWarning("[Ownership] CardCatalog 미초기화 — 부트(InitializationInstaller)를 거치지 않은 씬에서는 전체 해금이 동작하지 않는다.");
            return 0;
        }

        var t_ids = new List<int>(CardCatalog.Count);
        foreach (int t_cardId in CardCatalog.AllIds) t_ids.Add(t_cardId);

        return GrantAll(t_ids);
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
