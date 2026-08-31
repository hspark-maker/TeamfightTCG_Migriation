using System;
using System.Collections.Generic;

/// <summary>카드 소유권의 static 단일 창구.
///
/// 지급의 진실원은 서버다 — 튜토리얼 이관 후 남은 쓰기 호출부는 디버그와 되감기(OutgameTutorialRewind)뿐이고,
/// 그 둘은 P0에서 닫는다.</summary>
public static class OwnershipManager
{
    static readonly HashSet<int> s_owned = new HashSet<int>();

    /// <summary>소유 변경 통지 — UI 갱신용.
    ///
    /// 서버 응답 채택 경로(ServerSaveCommands.InvokeAsync → ServerSlotRehydrator → Init)에서도 발화한다.
    /// 그 구간은 업로드가 봉인돼 있고 채택이 업로드 기준선을 세우는 중이므로, <b>구독자는 세이브를 쓰지 마라</b>(화면 갱신만).</summary>
    public static event Action OnOwnershipChanged;

    public static int OwnedCount => s_owned.Count;

    // 외부 변조 차단용 스냅샷(라이브 뷰 아님)
    public static IReadOnlyCollection<int> OwnedIds => new List<int>(s_owned);

    // 초기화에서 클라우드 세이브 채택·CardCatalog.SetSource() 이후 1회 호출
    public static void Init()
    {
        s_owned.Clear();
        bool t_dirty = false;

        var t_data = DataSaveManager.Data.Ownership;
        if (t_data.CardIds != null)
        {
            foreach (var t_id in t_data.CardIds)
            {
                if (t_id <= 0) continue;
                if (!CardCatalog.IsReady || CardCatalog.Contains(t_id)) s_owned.Add(t_id);
                else t_dirty = true;
            }
        }

        if (t_dirty) Save();

        // 서버 채택 경로(ServerSlotRehydrator)도 이 Init을 다시 태운다 — 통지가 없으면
        // 도감·덱편집을 열어 둔 채 소유가 늘었을 때 화면이 옛 집합에 머문다.
        OnOwnershipChanged?.Invoke();
    }

    // 메모리 소유 집합을 세이브 슬롯에 flush 후 영속화
    public static void Save()
    {
        var t_data = DataSaveManager.Data.Ownership;
        t_data.CardIds = new List<int>(s_owned);
        DataSaveManager.SaveCoalesced();
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
            UnityEngine.Debug.LogWarning("[Ownership] CardCatalog 미초기화 — 초기화(InitializationRunner)를 거치지 않은 씬에서는 전체 해금이 동작하지 않는다.");
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
