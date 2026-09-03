using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 정점 격파 신고의 단일 창구. 해금 사슬과 랭크 잠금의 진실원은 서버 reportAdventureWin 이다.
// 낙인 반영은 여기서 하지 않는다: 응답의 updatedSlots 를 ServerSaveCommands 가 채택하면
// ServerSlotRehydrator 가 모험 화면 갱신까지 통지한다.
//
// 승패를 보내지 않는다 — 서버가 전투를 검증할 방법이 없어 "항상 true 인 인자"가 되고,
// 그런 인자는 읽는 사람에게 검증되는 것처럼 보인다. 패배는 아예 부르지 않는 것이 계약이다.
internal static class AdventureWinCommand
{
    const string REPORT_COMMAND = "reportAdventureWin";

    // 전투 씬과 로비가 같은 정점을 두 번 신고하는 것이 정상 경로다(양쪽 다 실패에 대비한다).
    // 왕복이 겹치면 뒤엣것은 서버를 부르지 않고 앞엣것의 결과를 기다린다.
    //
    // 값이 UniTask 가 아니라 소스인 이유: UniTask 는 1회 소비 계약이라 같은 것을 둘에게 주면
    // 두 번째가 예외를 던진다. 소스의 Task 는 몇 번이든 기다릴 수 있다.
    static readonly Dictionary<string, UniTaskCompletionSource<bool>> s_inFlight =
        new Dictionary<string, UniTaskCompletionSource<bool>>();

    /// <summary>정점 격파를 서버에 신고한다. 낙인이 섰으면 true — 이미 서 있던 경우도 포함한다.</summary>
    internal static UniTask<bool> ReportWinAsync(string _nodeId)
    {
        if (string.IsNullOrEmpty(_nodeId))
        {
            Debug.LogWarning("[AdventureWinCommand] nodeId 가 비어 신고하지 않는다(정점 저작의 nodeId 확인).");
            return UniTask.FromResult(false);
        }

        if (s_inFlight.TryGetValue(_nodeId, out UniTaskCompletionSource<bool> t_pending))
            return t_pending.Task;

        // 등록이 발사보다 먼저다 — 왕복이 첫 대기 전에 끝나는 갈래(오프라인 즉시 거절)에서
        // 뒤에 등록하면 완료된 실패가 사전에 영영 남아 이후 재신고가 서버를 못 부른다.
        var t_source = new UniTaskCompletionSource<bool>();
        s_inFlight[_nodeId] = t_source;

        SendAsync(_nodeId, t_source).Forget();
        return t_source.Task;
    }

    static async UniTaskVoid SendAsync(string _nodeId, UniTaskCompletionSource<bool> _source)
    {
        bool t_reported = false;

        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<ReportAdventureWinResult>(
                REPORT_COMMAND,
                new { env = ContentProfileConfig.Active.CloudEnvId, nodeId = _nodeId });

            // 서버가 다른 정점을 낙인했다면 우리가 아는 상태가 아니다 — 성공으로 접으면
            // 없는 선물을 그리게 된다(응답에 nodeId 가 없는 구 서버는 그대로 믿는다).
            t_reported = string.IsNullOrEmpty(t_result.NodeId) || t_result.NodeId == _nodeId;
            if (t_reported)
                Debug.Log($"[AdventureWinCommand] 격파 신고 완료(node={_nodeId}, rev={t_result.Revision}).");
            else
                Debug.LogError($"[AdventureWinCommand] 서버가 다른 정점을 낙인했다(신고={_nodeId}, 응답={t_result.NodeId}).");
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            // 재시도가 성공과 같은 자리에 도착한 것이다 — 낙인은 이미 서 있거나 수령까지 끝났다.
            if (t_rejected.Reason == "AlreadyPending" || t_rejected.Reason == "AlreadyCleared")
            {
                Debug.Log($"[AdventureWinCommand] 이미 반영된 신고다(node={_nodeId}, reason={t_rejected.Reason}).");
                t_reported = true;
            }
            else
            {
                Debug.LogWarning($"[AdventureWinCommand] 서버가 신고를 거절했다(node={_nodeId}, reason={t_rejected.Reason}) — {t_rejected.Message}");
            }
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다 — 여기서 표면을 두 번 칠하지 않는다.
            Debug.LogWarning($"[AdventureWinCommand] 응답 채택이 세션을 접었다 — {t_adoption.Message}");
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[AdventureWinCommand] {REPORT_COMMAND} 실패(node={_nodeId}) — {t_exception.GetBaseException().Message}");
        }
        finally
        {
            // 실패를 남겨 두지 않는다 — 로비 복귀가 같은 정점을 다시 신고할 수 있어야 한다.
            s_inFlight.Remove(_nodeId);
            _source.TrySetResult(t_reported);
        }
    }
}
