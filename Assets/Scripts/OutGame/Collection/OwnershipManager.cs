using System;
using System.Collections.Generic;
using UnityEngine;

// 카드 소유권의 static 단일 창구. 소유 키 집합을 메모리 캐싱하고 세이브 슬롯(OwnershipSaveData) 매핑을 여기서만 안다.
// 안정 키는 CardCatalog.KeyOf — 덱·도감 세이브와 정합. CurrencyManager와 동일한 부트/flush 결.
public static class OwnershipManager
{
    // 소유 키 캐시.
    static readonly HashSet<string> s_owned = new HashSet<string>();

    // 소유 변경 통지 — UI 갱신용.
    public static event Action OnOwnershipChanged;

    public static int OwnedCount => s_owned.Count;

    // 외부 변조 차단용 스냅샷(라이브 뷰 아님 — 순회 중 Revoke해도 안전).
    public static IReadOnlyCollection<string> OwnedKeys => new List<string>(s_owned);

    // 부트에서 DataSaveManager.Load()·CardCatalog.SetSource() 이후 1회 호출.
    public static void Init()
    {
        s_owned.Clear();

        var t_data = DataSaveManager.Data.ownership;
        if (t_data.ownedCardKeys != null)
        {
            foreach (var t_key in t_data.ownedCardKeys)
            {
                if (!string.IsNullOrEmpty(t_key)) s_owned.Add(t_key);
            }
        }

        if (!t_data.defaultsGranted) GrantDefaults();
    }

    // 메모리 소유 집합을 세이브 슬롯에 flush 후 영속화.
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

    // 신규 지급이면 true. 이미 소유면 false(Save·이벤트 없음).
    public static bool Grant(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return false;
        if (!s_owned.Add(_key)) return false;

        Save();
        OnOwnershipChanged?.Invoke();
        return true;
    }

    // 실제 제거 시 true. 미소유면 false(Save·이벤트 없음). 디버그용.
    public static bool Revoke(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return false;
        if (!s_owned.Remove(_key)) return false;

        Save();
        OnOwnershipChanged?.Invoke();
        return true;
    }

    // 최초 1회 기본 지급 — 카탈로그 전체를 소유로. SO 설정 없이 fallback으로만 동작.
    static void GrantDefaults()
    {
        // Count==0(빈 카탈로그)이면 defaultsGranted를 굳히지 않고 보류한다.
        // 굳히면 이후 재지급이 막혀 영구 0장으로 고착될 수 있다(진행도 0 덮어쓰기 방지).
        if (!CardCatalog.IsReady || CardCatalog.Count == 0)
        {
            Debug.LogWarning("[OwnershipManager] CardCatalog 미준비/빈 상태 — 기본 지급을 보류한다(부트 순서·allCards 확인).");
            return;
        }

        foreach (var t_card in CardCatalog.All)
        {
            var t_key = CardCatalog.KeyOf(t_card);
            if (!string.IsNullOrEmpty(t_key)) s_owned.Add(t_key);
        }

        DataSaveManager.Data.ownership.defaultsGranted = true;
        Save();
    }
}
