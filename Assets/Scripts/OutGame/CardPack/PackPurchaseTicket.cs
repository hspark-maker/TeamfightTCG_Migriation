using Cysharp.Threading.Tasks;
using UnityEngine;

// 카드팩 구매 왕복을 "값"이 아니라 "진행 중인 약속"으로 다루는 티켓.
// 클릭 즉시 개봉 화면을 열고 서버 응답은 그와 나란히 도착시키기 위한 계약이다.
public class PackPurchaseTicket
{
    // 자동 리셋 소스를 쓰면 뷰와 컨트롤러 중 한쪽만 결과를 받는다.
    readonly UniTaskCompletionSource<OpenedPack> m_source = new UniTaskCompletionSource<OpenedPack>();

    // 진행 상태·결과를 밖으로 내보내지 않는다 — 티켓의 진실원은 Await() 하나여야 이중 조회 경로가 생기지 않는다.
    CardPackData m_pack;

    /// <summary>구매 왕복을 즉시 띄우고 그 약속을 돌려준다 (await 하지 않는다).</summary>
    public static PackPurchaseTicket Begin(CardPackData _pack)
    {
        var t_ticket = new PackPurchaseTicket { m_pack = _pack };
        t_ticket.RunAsync().Forget();
        return t_ticket;
    }

    /// <summary>이미 끝난 결과를 같은 계약으로 감싼다 (디버그·단독 테스트 경로).</summary>
    public static PackPurchaseTicket Completed(OpenedPack _opened)
    {
        var t_ticket = new PackPurchaseTicket();
        t_ticket.Complete(_opened);
        return t_ticket;
    }

    /// <summary>결과를 기다린다. 여러 곳에서 각자 불러도 같은 결과를 받는다.</summary>
    public UniTask<OpenedPack> Await() => m_source.Task;

    async UniTaskVoid RunAsync()
    {
        OpenedPack t_opened;
        try
        {
            t_opened = await CardPackOpener.PurchaseAsync(m_pack);
        }
        catch (System.Exception t_exception)
        {
            // 예외가 새면 티켓이 영원히 미완으로 남아 개봉 화면이 그대로 멈춘다.
            Debug.LogError($"[PackPurchaseTicket] 구매 왕복이 예외로 끝났다 — {t_exception.GetBaseException().Message}");
            t_opened = OpenedPack.CreateFailure(EPackOpenResult.SpendFailed);
        }

        Complete(t_opened);
    }

    void Complete(OpenedPack _opened)
    {
        m_source.TrySetResult(_opened);
    }
}
