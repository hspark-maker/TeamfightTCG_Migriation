using Cysharp.Threading.Tasks;
using UnityEngine;

// 세이브 커밋의 단일 진입점.
// 어느 도메인이 커밋을 걸든 캐시를 가진 전 도메인이 함께 flush되므로,
// "재화는 차감 전인데 진행도는 차감 후"인 세이브가 구조적으로 생기지 않는다.
public static class SaveTransaction
{
    // 커밋이 진행 중인가(쓰기 대기 중 같은 커맨드가 두 번 들어오는 것을 막는 판정)
    public static bool IsBusy => DataSaveManager.IsWriting;

    /// <summary>전 도메인을 세이브 슬롯에 반영한 뒤 디스크에 1회 쓴다.</summary>
    public static UniTask<ESaveWriteResult> CommitAsync()
    {
        FlushAll();
        return DataSaveManager.SaveAsync();
    }

    /// <summary>결과를 기다리지 않는 커밋. 동기 커맨드가 쓰는 창구다.
    /// 실패를 받을 호출자가 없으므로 여기서 로그로라도 남긴다.</summary>
    public static void Request() => ReportAsync().Forget();

    static async UniTaskVoid ReportAsync()
    {
        ESaveWriteResult t_result = await CommitAsync();
        if (t_result != ESaveWriteResult.Success)
            Debug.LogError($"[SaveTransaction] 커밋 실패: {t_result}");
    }

    /// <summary>앱 종료 경로 전용 동기 커밋. 종료 콜백은 비동기 완료를 기다려주지 않는다.</summary>
    internal static ESaveWriteResult CommitBlocking()
    {
        FlushAll();
        return DataSaveManager.SaveBlocking();
    }

    static void FlushAll()
    {
        CurrencyManager.FlushToData();
        OwnershipManager.FlushToData();
        ProfileManager.FlushToData();
        DeckSaveManager.FlushToData();
        CardGrowthManager.FlushToData();
        KeywordGrowthManager.FlushToData();
    }
}
