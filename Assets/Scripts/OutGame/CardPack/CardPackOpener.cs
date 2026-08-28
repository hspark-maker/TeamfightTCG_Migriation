using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 카드팩 구매·개봉의 static 파사드.
// 판정 진실원은 서버 openPack 이다. Precheck는 왕복을 아끼려는 낙관 검사일 뿐이고,
// 둘이 엇갈렸을 때 이기는 쪽은 언제나 서버다 — 서버 거절이 나오는 것이 정상 동작이다.
public static class CardPackOpener
{
    // 중복 1장이 주는 간식 수. 실제 적립은 서버가 하고, 이 값은 표시·검증 기준선이다.
    public const int SnackPerDuplicate = 1;

    /// <summary>구매 가능 여부의 낙관 검사. 차감·지급은 하지 않는다.</summary>
    public static EPackOpenResult Precheck(CardPackData _pack)
    {
        if (_pack == null) return EPackOpenResult.PackNotFound;
        if (!PackUnlockRules.IsUnlocked(_pack)) return EPackOpenResult.RankLocked;
        if (!CardCatalog.IsReady || !CardGrowthManager.IsReady) return EPackOpenResult.NotReady;

        IReadOnlyList<WeightedCard> t_resolvedPool = _pack.ResolvePool(RankManager.CurrentGrade);
        bool t_hasCandidate = false;
        for (int t_i = 0; t_i < t_resolvedPool.Count; t_i++)
        {
            WeightedCard t_entry = t_resolvedPool[t_i];
            if (t_entry.cardId <= 0 || !CardCatalog.Contains(t_entry.cardId)) continue;

            t_hasCandidate = true;
            break;
        }
        if (!t_hasCandidate) return EPackOpenResult.EmptyPool;

        if (!CurrencyManager.CanAfford(_pack.PriceType, _pack.Price)) return EPackOpenResult.InsufficientGold;

        return EPackOpenResult.Success;
    }

    /// <summary>팩 구매·개봉을 서버에 요청한다. 응답 채택으로 재화·소유·성장 슬롯이 갈아끼워진다.</summary>
    public static async UniTask<OpenedPack> PurchaseAsync(CardPackData _pack)
    {
        EPackOpenResult t_precheck = Precheck(_pack);
        if (t_precheck != EPackOpenResult.Success) return OpenedPack.CreateFailure(t_precheck);

        // 클라만 SO 폴백을 볼 수 있다 — 시트에 없는 팩은 서버가 아예 모르는 팩이라 반드시 거절된다.
        if (!PackSpec.TryGetPack(_pack.PackId, out _))
            Debug.LogError($"[CardPackOpener] '{_pack.PackId}' 가 CardPack 시트에 없다 — 클라는 SO 로 폴백하지만 서버는 SO 를 못 본다");

        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<OpenPackResult>(
                "openPack", new { env = ContentProfileConfig.Active.CloudEnvId, packId = _pack.PackId });

            return BuildOpenedPack(t_result);
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            Debug.LogWarning($"[CardPackOpener] 사전검사는 통과했으나 서버가 거절했다 — 시트/SO 폴백 드리프트를 점검할 것: {t_rejected.Message}");

            // 거절 사유는 details 로만 오고 안전하게 꺼낼 경로가 없다. 메시지 파싱 대신 사전검사를 다시 물어 사유를 좁힌다.
            EPackOpenResult t_reason = Precheck(_pack);
            if (t_reason == EPackOpenResult.Success) t_reason = EPackOpenResult.SpendFailed;

            return OpenedPack.CreateFailure(t_reason);
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다 — 여기서 표면을 두 번 칠하지 않는다.
            Debug.LogWarning($"[CardPackOpener] 응답 채택이 세션을 접었다 — {t_adoption.Message}");
            return OpenedPack.CreateFailure(EPackOpenResult.SpendFailed);
        }
        catch (System.Exception t_exception)
        {
            Debug.LogError($"[CardPackOpener] openPack 실패 — {t_exception.GetBaseException().Message}");
            return OpenedPack.CreateFailure(EPackOpenResult.SpendFailed);
        }
    }

    static OpenedPack BuildOpenedPack(OpenPackResult _result)
    {
        List<OpenPackCard> t_cards = _result.Cards;
        var t_drawn = new List<DrawnCard>(t_cards != null ? t_cards.Count : 0);

        if (t_cards != null)
        {
            for (int t_i = 0; t_i < t_cards.Count; t_i++)
            {
                OpenPackCard t_card = t_cards[t_i];
                if (t_card == null || t_card.CardId <= 0) continue;

                t_drawn.Add(new DrawnCard(t_card.CardId, t_card.IsNew, t_card.Snack));
            }
        }

        return OpenedPack.CreateSuccess(t_drawn, _result.ResolveRefundType());
    }
}
