using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 정적 보상 수령을 서버에 묻는 단일 창구.
// 자격 판정·지급·낙인의 진실원은 서버 claimReward 다 — 각 도메인의 CanClaim 은 왕복을 아끼는 낙관 검사일 뿐이고,
// 둘이 엇갈렸을 때 이기는 쪽은 언제나 서버다. 랭크 티어와 모험 정점이 같은 계약을 쓰므로 여기 하나로 모은다.
internal static class RewardClaimCommand
{
    // 서버 ClaimOwnerType 과 같은 문자열이어야 한다(스펙시트 Reward.ownerType 열 값).
    internal const string OwnerRank = "Rank";
    internal const string OwnerTournament = "Tournament";
    internal const string OwnerAlbum = "Album";

    const string COMMAND_NAME = "claimReward";

    // 날아가 있는 수령의 키(ownerType:ownerId). 팝업은 응답을 기다리지 않고 1초 안에 닫히는데,
    // 도메인의 수령 낙인은 응답 채택 뒤에야 서므로 그 사이 행이 "받을 수 있음"으로 남는다 —
    // 도메인의 상태 판정이 이 집합을 함께 읽어 그 틈을 메우고, 같은 보상의 재클릭도 그 자리에서 막는다.
    static readonly HashSet<string> s_inFlight = new HashSet<string>();

    /// <summary>서버가 수령을 명시적으로 거절했을 때 한 번 울린다. 표면(팝업)은 UI 쪽 구독자가 그린다.</summary>
    internal static event Action OnRejected;

    /// <summary>왕복 중인 수령이 하나라도 있는지. 도메인이 키 문자열을 짓기 전에 거르는 빠른 관문이다.</summary>
    internal static bool HasAnyInFlight => s_inFlight.Count > 0;

    /// <summary>이 보상이 왕복 중인지. 도메인의 수령 낙인이 아직 서지 않은 구간을 이 값이 메운다.</summary>
    internal static bool IsInFlight(string _ownerType, string _ownerId)
        => !string.IsNullOrEmpty(_ownerId) && s_inFlight.Contains(_ownerType + ":" + _ownerId);

    // 도메인 리로드를 끈 플레이에서 옛 키가 남으면 그 보상이 영영 수령 완료로 굳는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_inFlight.Clear();

    /// <summary>보상 수령을 요청한다. 성공하면 응답 채택으로 재화·해당 도메인 슬롯이 갈아끼워진 뒤
    /// <b>서버가 실제로 지급한 목록</b>과 함께 돌아온다. <paramref name="_onInFlightChanged"/> 는
    /// 왕복이 시작될 때와 끝날 때 한 번씩 울려, 도메인이 낙관 상태를 그리고 되돌릴 자리를 준다.</summary>
    // 거절(자격 미달·이미 수령·보상 미저작)은 세션이 아니라 이 호출의 결과다 — 표면은 부른 도메인이 진다.
    internal static async UniTask<RewardClaimOutcome> ClaimAsync(string _ownerType, string _ownerId,
                                                                CurrencyPendingTicket _pending = null,
                                                                Action _onInFlightChanged = null)
    {
        // InvokeAsync 에 닿지 못하는 갈래엔 걷어 줄 finally 가 없다 — 낙관분을 여기서 직접 되돌린다.
        if (string.IsNullOrEmpty(_ownerId))
        {
            _pending?.Settle();
            return default;
        }

        string t_key = _ownerType + ":" + _ownerId;

        // 같은 보상이 아직 왕복 중이다. 여기서 즉시 돌려줘야 부른 쪽이 같은 프레임에 거절을 알고 연출을 접는다.
        if (!s_inFlight.Add(t_key))
        {
            _pending?.Settle();
            return default;
        }

        // 등록이 끝난 이 자리에서 울려야 도메인이 첫 await 이전에 낙관 상태를 그린다.
        _onInFlightChanged?.Invoke();

        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<ClaimRewardResult>(
                COMMAND_NAME,
                new { env = ContentProfileConfig.Active.CloudEnvId, ownerType = _ownerType, ownerId = _ownerId },
                _pending);

            Debug.Log($"[RewardClaimCommand] {_ownerType}/{_ownerId} 수령 — {Describe(t_result)}");
            return new RewardClaimOutcome(ToGains(t_result, _ownerType, _ownerId));
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            // 사전검사를 통과했는데 여기 왔다면 클라 스펙 캐시와 서버 표가 갈렸거나, 다른 기기가 먼저 받은 것이다.
            Debug.LogWarning($"[RewardClaimCommand] {_ownerType}/{_ownerId} 를 서버가 거절했다 — {t_rejected.Message}");
            OnRejected?.Invoke();
            return default;
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다 — 여기서 표면을 두 번 칠하지 않는다.
            Debug.LogWarning($"[RewardClaimCommand] 응답 채택이 세션을 접었다 — {t_adoption.Message}");
            return default;
        }
        catch (System.Exception t_exception)
        {
            Debug.LogError($"[RewardClaimCommand] {COMMAND_NAME} 실패 — {t_exception.GetBaseException().Message}");
            return default;
        }
        finally
        {
            // 거절·예외에도 반드시 온다 — 빠지면 낙관 상태가 수령 완료인 채 고착된다.
            s_inFlight.Remove(t_key);
            _onInFlightChanged?.Invoke();
        }
    }

    // 응답의 재화 표기를 클라 열거형으로 옮긴다. 못 읽는 표기·0 이하는 버린다(스펙 로더와 같은 규약).
    static List<CurrencyGain> ToGains(ClaimRewardResult _result, string _ownerType, string _ownerId)
    {
        var t_gains = new List<CurrencyGain>();
        var t_granted = _result?.Granted;
        if (t_granted == null) return t_gains;

        for (int t_i = 0; t_i < t_granted.Count; t_i++)
        {
            var t_line = t_granted[t_i];
            if (t_line == null || t_line.Amount <= 0) continue;

            if (!CurrencyCode.TryParse(t_line.Currency, out ECurrencyType t_type))
            {
                Debug.LogWarning($"[RewardClaimCommand] {_ownerType}/{_ownerId}: 알 수 없는 재화 '{t_line.Currency}' 를 건너뛴다.");
                continue;
            }

            t_gains.Add(new CurrencyGain(t_type, t_line.Amount));
        }

        return t_gains;
    }

    static string Describe(ClaimRewardResult _result)
    {
        var t_granted = _result.Granted;
        if (t_granted == null || t_granted.Count == 0) return "지급 없음";

        var t_text = new StringBuilder();
        for (int t_i = 0; t_i < t_granted.Count; t_i++)
        {
            if (t_granted[t_i] == null) continue;
            if (t_text.Length > 0) t_text.Append(", ");
            t_text.Append(t_granted[t_i].Currency).Append(' ').Append(t_granted[t_i].Amount);
        }
        return t_text.ToString();
    }
}
