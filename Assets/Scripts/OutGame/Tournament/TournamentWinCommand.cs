using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 정점 격파 신고의 단일 창구. 해금 사슬과 랭크 잠금의 진실원은 서버 reportTournamentWin 이다.
// 낙인 반영은 여기서 하지 않는다: 응답의 updatedSlots 를 ServerSaveCommands 가 채택하면
// ServerSlotRehydrator 가 토너먼트 화면 갱신까지 통지한다.
//
// 승패를 보내지 않는다 — 서버가 전투를 검증할 방법이 없어 "항상 true 인 인자"가 되고,
// 그런 인자는 읽는 사람에게 검증되는 것처럼 보인다. 패배는 아예 부르지 않는 것이 계약이다.
internal static class TournamentWinCommand
{
    const string REPORT_COMMAND = "reportTournamentWin";

    // 전투 씬과 로비가 같은 정점을 두 번 신고하는 것이 정상 경로다(양쪽 다 실패에 대비한다).
    // 왕복이 겹치면 뒤엣것은 서버를 부르지 않고 앞엣것의 결과를 기다린다.
    static readonly Dictionary<string, UniTask<bool>> s_inFlight = new Dictionary<string, UniTask<bool>>();

    /// <summary>정점 격파를 서버에 신고한다. 낙인이 섰으면 true — 이미 서 있던 경우도 포함한다.</summary>
    internal static UniTask<bool> ReportWinAsync(string _nodeId)
    {
        if (string.IsNullOrEmpty(_nodeId))
        {
            Debug.LogWarning("[TournamentWinCommand] nodeId 가 비어 신고하지 않는다(정점 저작의 nodeId 확인).");
            return UniTask.FromResult(false);
        }

        if (s_inFlight.TryGetValue(_nodeId, out UniTask<bool> t_pending)) return t_pending;

        UniTask<bool> t_task = SendAsync(_nodeId);
        s_inFlight[_nodeId] = t_task;
        return t_task;
    }

    static async UniTask<bool> SendAsync(string _nodeId)
    {
        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<ReportTournamentWinResult>(
                REPORT_COMMAND,
                new { env = ContentProfileConfig.Active.CloudEnvId, nodeId = _nodeId });

            Debug.Log($"[TournamentWinCommand] 격파 신고 완료(node={_nodeId}, rev={t_result.Revision}).");
            return true;
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            // 재시도가 성공과 같은 자리에 도착한 것이다 — 낙인은 이미 서 있거나 수령까지 끝났다.
            if (t_rejected.Reason == "AlreadyPending" || t_rejected.Reason == "AlreadyCleared")
            {
                Debug.Log($"[TournamentWinCommand] 이미 반영된 신고다(node={_nodeId}, reason={t_rejected.Reason}).");
                return true;
            }

            Debug.LogWarning($"[TournamentWinCommand] 서버가 신고를 거절했다(node={_nodeId}, reason={t_rejected.Reason}) — {t_rejected.Message}");
            return false;
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다 — 여기서 표면을 두 번 칠하지 않는다.
            Debug.LogWarning($"[TournamentWinCommand] 응답 채택이 세션을 접었다 — {t_adoption.Message}");
            return false;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[TournamentWinCommand] {REPORT_COMMAND} 실패(node={_nodeId}) — {t_exception.GetBaseException().Message}");
            return false;
        }
        finally
        {
            // 실패는 남겨 두지 않는다 — 로비 복귀가 같은 정점을 다시 신고할 수 있어야 한다.
            s_inFlight.Remove(_nodeId);
        }
    }
}
