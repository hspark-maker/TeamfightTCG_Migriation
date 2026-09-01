using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 한계돌파 판정을 서버에 묻는 단일 창구.
// 단계·간식 차감의 진실원은 서버 limitBreakCard 다 — 매니저에 남은 소유·다음 단계·간식 검사는
// 왕복을 아끼는 낙관 검사일 뿐이고, 둘이 엇갈렸을 때 이기는 쪽은 언제나 서버다.
// EnhanceCommand에 얹지 않는다 — 저쪽이 카드·키워드를 합친 근거는 둘의 계약이 같다는 것인데
// 한계돌파는 무는 것도(간식) 요청·응답 계약도 다르다.
internal static class LimitBreakCommand
{
    const string LIMIT_BREAK_COMMAND = "limitBreakCard";

    // 거절 사유의 계약 코드. 서버 rejectDomain 이 message 앞머리에 실어 보내고
    // ServerCommandRejectedException.Reason 이 그것을 떼어 준다.
    const string REASON_NOT_ENOUGH_SNACK = "NotEnoughSnack";
    const string REASON_MAX_STAGE        = "MaxStage";

    /// <summary>한계돌파 1회를 서버에 요청한다. 오른 단계·남은 간식은 응답 채택이 슬롯째 갈아끼우므로
    /// 화면이 읽을 것은 성패와 사유뿐이다.</summary>
    internal static async UniTask<ELimitBreakOutcome> LimitBreakAsync(int _cardId)
    {
        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<LimitBreakCardResult>(
                LIMIT_BREAK_COMMAND,
                new { env = ContentProfileConfig.Active.CloudEnvId, cardId = _cardId });

            LogSettled(_cardId, t_result);

            return ELimitBreakOutcome.Success;
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            return Blocked(t_rejected);
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다 — 여기서 표면을 두 번 칠하지 않는다.
            Debug.LogWarning($"[LimitBreakCommand] 응답 채택이 세션을 접었다 — {t_adoption.Message}");
            return ELimitBreakOutcome.NotReady;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[LimitBreakCommand] {LIMIT_BREAK_COMMAND} 실패 — {t_exception.GetBaseException().Message}");
            return ELimitBreakOutcome.NotReady;
        }
    }

    // 서버 확정값과 클라가 표에서 읽은 값을 나란히 남긴다 — 화면은 슬롯에서 다시 읽으므로
    // 양쪽이 같은 표를 다르게 읽었는지 실기에서 알아낼 창구가 이 로그뿐이다.
    static void LogSettled(int _cardId, LimitBreakCardResult _result)
    {
        if (_result == null) return;

        // 서버가 도달했다고 말한 **그 단계**의 클라 값이다 — 왕복 뒤라 다음 단계를 물으면 한 칸 어긋난다.
        bool t_known = GrowthRules.TryGetLimitBreakStep(_result.Stage, out LimitBreakStep t_step);

        Debug.Log($"[LimitBreakCommand] 한계돌파 성공(card={_cardId}) — 단계 {_result.Stage} · " +
                  $"체력 +{_result.HpGain}(클라 {(t_known ? t_step.HpGain.ToString() : "-")}) · " +
                  $"간식 -{_result.SnackCost}(클라 {(t_known ? t_step.SnackCost.ToString() : "-")}) · 잔량 {_result.SnackLeft}");
    }

    // 거절을 화면이 읽을 결말로 접는다. 못 가리는 사유(CardNotOwned · RuleUnavailable · 미지)는 NotReady 다 —
    // 원인이 화면 밖에 있어 조용히 묻으면 안 되므로 로그는 남긴다.
    static ELimitBreakOutcome Blocked(ServerCommandRejectedException _rejected)
    {
        switch (_rejected.Reason)
        {
            case REASON_NOT_ENOUGH_SNACK: return ELimitBreakOutcome.NotEnoughSnack;
            case REASON_MAX_STAGE:        return ELimitBreakOutcome.MaxStage;
        }

        Debug.LogWarning($"[LimitBreakCommand] {LIMIT_BREAK_COMMAND} 를 서버가 거절했다 — {_rejected.Message}");
        return ELimitBreakOutcome.NotReady;
    }
}
