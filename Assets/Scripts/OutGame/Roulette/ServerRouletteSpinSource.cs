using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 회전 판정을 서버 spinRoulette 에 묻는 추첨 소스. 멈출 칸도 상품도 서버가 정하고,
// 티켓 차감·재화 지급은 응답의 wallet 채택이 한다 — 클라가 잔액을 쓰는 경로는 없다.
// 예외를 밖으로 내보내지 않는 것이 IRouletteSpinSource 계약이라 거절·망 실패를 전부 결과 코드로 접는다.
public sealed class ServerRouletteSpinSource : IRouletteSpinSource
{
    const string COMMAND_NAME = "spinRoulette";

    // 서버 rejectDomain 이 message 앞머리에 싣는 계약 코드. ERouletteSpinResult 이름과 같아야 한다.
    const string REASON_ROULETTE_NOT_FOUND  = "RouletteNotFound";
    const string REASON_EMPTY_POOL          = "EmptyPool";
    const string REASON_INSUFFICIENT_TICKET = "InsufficientTicket";

    readonly RouletteConfig m_config;

    public ServerRouletteSpinSource(RouletteConfig _config)
    {
        m_config = _config;
    }

    /// <summary>회전 1회를 서버에 요청한다. 실패·거절·취소는 전부 결과값으로 돌아온다.</summary>
    public async UniTask<RouletteSpinOutcome> SpinAsync(CancellationToken _ct)
    {
        if (_ct.IsCancellationRequested) return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.Canceled);

        // 게이트 밖에서 InvokeAsync 가 던지면 Unusable 로 분류돼, 원인은 연결인데 "거절"로 뭉개진다.
        if (!PlayerSaveCloud.CanRunServerCommand) return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.NetworkFailed);

        // 첫 await 이전이어야 유저가 누른 프레임에 티켓이 줄어든다. 걷는 쪽은 InvokeAsync 가 전담한다.
        CurrencyPendingTicket t_pending = this.HoldPrice();

        try
        {
            // _ct 를 흘리지 않는다 — 왕복 중 취소는 "티켓은 빠졌는데 결과를 모르는 상태"다.
            // 패널이 닫히면 연출만 사라지고 지갑 채택은 끝까지 간다.
            var t_result = await ServerSaveCommands.InvokeAsync<SpinRouletteResult>(
                COMMAND_NAME,
                new { env = ContentProfileConfig.Active.CloudEnvId, rouletteId = m_config.RouletteId },
                t_pending);

            return this.ToOutcome(t_result);
        }
        catch (OperationCanceledException)
        {
            // 분류기가 취소를 Transient 로 접기 때문에 반드시 맨 위여야 한다 — 아래로 내리면 취소가 망 실패 안내가 된다.
            return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.Canceled);
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            return RouletteSpinOutcome.CreateFailure(Blocked(t_rejected));
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 이 명령은 세이브를 쓰지 않아 계약상 도달하지 않는다. 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다.
            Debug.LogWarning($"[ServerRouletteSpinSource] Adopting the response closed the session — {t_adoption.Message}");
            return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.Rejected);
        }
        catch (Exception t_exception)
        {
            // 망 문제만 갈라낸다 — 판정은 클라우드 분류기 하나에 맡긴다(여기서 예외 목록을 다시 짜면 기준이 둘이 된다).
            if (CloudFailureClassifier.Classify(t_exception) == ECloudFailureKind.Transient)
            {
                Debug.LogWarning($"[ServerRouletteSpinSource] {COMMAND_NAME} round trip failed ({CloudFailureClassifier.Describe(t_exception)}) — {t_exception.GetBaseException().Message}");
                return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.NetworkFailed);
            }

            Debug.LogError($"[ServerRouletteSpinSource] {COMMAND_NAME} failed ({CloudFailureClassifier.Describe(t_exception)}) — {t_exception.GetBaseException().Message}");
            return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.Rejected);
        }
        finally
        {
            // 요청 인자를 짓다 던지면 InvokeAsync 몸통에 닿지 못해 그쪽 회수가 돌지 않는다. 멱등이라 정상 갈래와 겹쳐도 안전하다.
            t_pending?.Settle();
        }
    }

    // 홀드는 표시 전용이고 채택 직전에 걷힌다 — 저작 가격이 서버와 갈려도 잔액에 흔적이 남지 않는다.
    // 비용 재화는 티켓으로 못 박지 않는다 — 업로더가 다이아 회전을 경고만으로 통과시키고
    // Precheck 도 저작 재화로 CanAfford 를 걸어서, 여기만 티켓을 요구하면 그 판의 롤다운이 사라진다.
    CurrencyPendingTicket HoldPrice()
    {
        if (m_config == null) return null;
        if (m_config.Price <= 0) return null;

        return CurrencyPendingTicket.Hold(m_config.PriceType, -m_config.Price);
    }

    // 잔액은 WalletCloud.Adopt 가 이미 정확히 갈아끼웠고 여기 남는 것은 연출뿐이다 —
    // 못 읽는 상품을 골드로 떨어뜨리면 화면이 거짓말을 한다.
    RouletteSpinOutcome ToOutcome(SpinRouletteResult _result)
    {
        ClaimRewardGain t_gain = _result?.Gain;
        if (t_gain == null || t_gain.Amount <= 0)
        {
            Debug.LogError($"[ServerRouletteSpinSource] The grant lines are empty — there is no prize to draw (slot={_result?.SlotIndex}).");
            return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.RewardUnreadable);
        }

        if (!CurrencyCode.TryParse(t_gain.Currency, out ECurrencyType t_type))
        {
            Debug.LogError($"[ServerRouletteSpinSource] Unknown currency '{t_gain.Currency}' — the server already replaced the balance, so only the presentation is skipped.");
            return RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.RewardUnreadable);
        }

        // 지갑이 이미 움직였으므로 실패로 접으면 그쪽이 오히려 거짓이다 — 흔적만 남긴다.
        if (m_config != null && !string.IsNullOrEmpty(_result.RouletteId) && _result.RouletteId != m_config.RouletteId)
            Debug.LogError($"[ServerRouletteSpinSource] The server spun a different board (requested={m_config.RouletteId}, response={_result.RouletteId}).");

        // SO 를 고치고 구성 표 업로드를 잊은 경우가 여기서 잡힌다. 서버 판정이 옳으므로 실패로 접지 않는다.
        if (m_config != null && m_config.TryGetSlot(_result.SlotIndex, out RouletteSlotDef t_slot) &&
            (t_slot.currency != t_type || t_slot.amount != t_gain.Amount))
        {
            Debug.LogError($"[ServerRouletteSpinSource] Slot {_result.SlotIndex} authoring ({t_slot.currency} {t_slot.amount}) differs from the server grant ({t_type} {t_gain.Amount}) — check the config table upload.");
        }

        return RouletteSpinOutcome.CreateSuccess(_result.SlotIndex, t_type, t_gain.Amount);
    }

    // 서버가 모르는 사유는 Rejected 다. 사전검사를 다시 묻지 않는다 —
    // 재검사 시점의 상태가 요청 시점과 달라 틀린 사유를 만든다.
    static ERouletteSpinResult Blocked(ServerCommandRejectedException _rejected)
    {
        switch (_rejected.Reason)
        {
            case REASON_ROULETTE_NOT_FOUND:  return ERouletteSpinResult.RouletteNotFound;
            case REASON_EMPTY_POOL:          return ERouletteSpinResult.EmptyPool;
            case REASON_INSUFFICIENT_TICKET: return ERouletteSpinResult.InsufficientTicket;
        }

        Debug.LogWarning($"[ServerRouletteSpinSource] Could not read the {COMMAND_NAME} rejection reason — '{_rejected.Reason}' · {_rejected.Message}");
        return ERouletteSpinResult.Rejected;
    }
}
