using System;
using System.Collections.Generic;

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

    // 부트 라우팅용: 세이브 소유 여부를 메모리 캐시(Init)·CardCatalog 없이 조회한다.
    // 소유 세이브 슬롯(ownership.ownedCardKeys) 매핑을 이 창구만 알아야 하므로 첫실행 판정도 여기서 답한다.
    // DataSaveManager.Load() 이후면 유효(BootScene 시점엔 GameManager.Boot가 이미 로드함).
    public static bool HasAnyOwnedSaved()
    {
        var t_data = DataSaveManager.Data.ownership;
        if (t_data.ownedCardKeys == null) return false;

        // Init과 동일한 필터(빈/null 키 제외) — "첫실행=OwnedCount==0" 단일 정의 유지.
        foreach (var t_key in t_data.ownedCardKeys)
        {
            if (!string.IsNullOrEmpty(t_key)) return true;
        }
        return false;
    }

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

        // G-23: 신규 유저는 소유 0으로 시작한다(첫실행 판정 = OwnedCount==0, 스타터팩이 채움).
        // 기존 전체 자동지급(GrantDefaults)은 프로덕션 부트에서 제거. 세이브의 ownedCardKeys는
        // 위에서 그대로 로드하므로 기존 소유 유저는 진행도 유지(0 덮어쓰기 없음).
        // 테스트 씬에서 전체 해금이 필요하면 OwnershipDebugTool의 "전체 해금"을 쓴다.
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
}
