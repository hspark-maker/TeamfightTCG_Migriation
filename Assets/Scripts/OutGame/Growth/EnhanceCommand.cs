using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 강화 판정을 서버에 묻는 단일 창구.
// 성공률·비용·차감·레벨의 진실원은 서버 enhanceCard / enhanceKeyword 다 — 매니저에 남은 만렙·미초기화 검사는
// 왕복을 아끼는 낙관 검사일 뿐이고, 둘이 엇갈렸을 때 이기는 쪽은 언제나 서버다.
// 카드와 키워드가 같은 계약(요청 freeShot · 응답 outcome/level/cost)을 쓰므로 여기 하나로 모은다.
internal static class EnhanceCommand
{
    const string CARD_COMMAND    = "enhanceCard";
    const string KEYWORD_COMMAND = "enhanceKeyword";

    // 거절 사유의 계약 코드. 서버 rejectDomain 이 message 앞머리에 실어 보내고
    // ServerCommandRejectedException.Reason 이 그것을 떼어 준다.
    const string REASON_NOT_AFFORDABLE = "NotAffordable";
    const string REASON_MAX_LEVEL      = "MaxLevel";

    /// <summary>카드 강화 1회를 서버에 요청한다. 성공·확률실패는 결제가 끝난 것이고, 그 밖의 결말은 재화 소모가 없다.</summary>
    internal static async UniTask<EnhanceCommandResult> EnhanceCardAsync(
        int _cardId, bool _freeShot, CurrencyPendingTicket _pending = null)
    {
        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<EnhanceCardResult>(
                CARD_COMMAND,
                new { env = ContentProfileConfig.Active.CloudEnvId, cardId = _cardId, freeShot = _freeShot },
                _pending);

            return new EnhanceCommandResult(t_result.ResolveOutcome(), t_result.Level, t_result.FreeShotUsed);
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            return Blocked(CARD_COMMAND, t_rejected);
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다 — 여기서 표면을 두 번 칠하지 않는다.
            Debug.LogWarning($"[EnhanceCommand] 응답 채택이 세션을 접었다 — {t_adoption.Message}");
            return EnhanceCommandResult.Blocked(EEnhanceOutcome.NotReady);
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[EnhanceCommand] {CARD_COMMAND} 실패 — {t_exception.GetBaseException().Message}");
            return EnhanceCommandResult.Blocked(EEnhanceOutcome.NotReady);
        }
        finally
        {
            // 요청 인자를 짓다 던지면 InvokeAsync 에 닿지 못해 그쪽 회수가 돌지 않는다. 멱등이라 정상 갈래와 겹쳐도 안전하다.
            _pending?.Settle();
        }
    }

    /// <summary>키워드 강화 1회를 서버에 요청한다. 확률 실패가 없어 성립하면 반드시 오른다.</summary>
    internal static async UniTask<EnhanceCommandResult> EnhanceKeywordAsync(
        CardKeyword _keyword, bool _freeShot, CurrencyPendingTicket _pending = null)
    {
        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<EnhanceKeywordResult>(
                KEYWORD_COMMAND,
                new { env = ContentProfileConfig.Active.CloudEnvId, keyword = (int)_keyword, freeShot = _freeShot },
                _pending);

            return new EnhanceCommandResult(t_result.ResolveOutcome(), t_result.Level, t_result.FreeShotUsed);
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            return Blocked(KEYWORD_COMMAND, t_rejected);
        }
        catch (ServerAdoptionException t_adoption)
        {
            Debug.LogWarning($"[EnhanceCommand] 응답 채택이 세션을 접었다 — {t_adoption.Message}");
            return EnhanceCommandResult.Blocked(EEnhanceOutcome.NotReady);
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[EnhanceCommand] {KEYWORD_COMMAND} 실패 — {t_exception.GetBaseException().Message}");
            return EnhanceCommandResult.Blocked(EEnhanceOutcome.NotReady);
        }
        finally
        {
            // 요청 인자를 짓다 던지면 InvokeAsync 에 닿지 못해 그쪽 회수가 돌지 않는다. 멱등이라 정상 갈래와 겹쳐도 안전하다.
            _pending?.Settle();
        }
    }

    // 거절을 화면이 읽을 결말로 접는다. 못 가리는 사유(RuleUnavailable · KeywordNotSupported · 미지)는 NotReady 다 —
    // "지금은 못 한다"가 곧 재화 소모 없음이라, 잘못 짚어 잔액 부족을 알리는 것보다 안전한 폴백이다.
    static EnhanceCommandResult Blocked(string _commandName, ServerCommandRejectedException _rejected)
    {
        switch (_rejected.Reason)
        {
            case REASON_NOT_AFFORDABLE: return EnhanceCommandResult.Blocked(EEnhanceOutcome.NotAffordable);
            case REASON_MAX_LEVEL:      return EnhanceCommandResult.Blocked(EEnhanceOutcome.MaxLevel);
        }

        Debug.LogWarning($"[EnhanceCommand] {_commandName} 를 서버가 거절했다 — {_rejected.Message}");
        return EnhanceCommandResult.Blocked(EEnhanceOutcome.NotReady);
    }
}

/// <summary>강화 명령 1회의 결말. 레벨은 거래가 성립한 경우(성공·확률실패)에만 뜻이 있다.</summary>
internal readonly struct EnhanceCommandResult
{
    internal readonly EEnhanceOutcome Outcome;

    /// <summary>서버가 확정한 시도 후 레벨.</summary>
    internal readonly int Level;

    /// <summary>안내가 대준 무료 한 방을 서버가 실제로 먹였는가.</summary>
    internal readonly bool FreeShotUsed;

    internal EnhanceCommandResult(EEnhanceOutcome _outcome, int _level, bool _freeShotUsed)
    {
        Outcome      = _outcome;
        Level        = _level;
        FreeShotUsed = _freeShotUsed;
    }

    /// <summary>결제 전에 막힌 결말(레벨 변화도 재화 소모도 없다).</summary>
    internal static EnhanceCommandResult Blocked(EEnhanceOutcome _outcome)
        => new EnhanceCommandResult(_outcome, 0, false);

    /// <summary>거래가 성립해 응답 레벨을 믿어도 되는가.</summary>
    internal bool Settled => Outcome == EEnhanceOutcome.Success || Outcome == EEnhanceOutcome.Failed;
}
