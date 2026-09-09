using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 튜토리얼 카드 지급을 서버에 묻는 단일 창구.
// 무엇을 주는지의 진실원은 시트의 CardPack/CardPackDrop 이다 — 클라는 팩 ID만 넘기고 목록은 응답으로 받는다.
// 소유 반영은 여기서 하지 않는다: 응답의 updatedSlots 를 ServerSaveCommands 가 채택하면
// ServerSlotRehydrator 가 OwnershipManager.Init 까지 다시 태운다.
internal static class TutorialGrantCommand
{
    const string GRANT_COMMAND = "grantTutorialCards";

    /// <summary>팩 하나의 카드 전량 지급을 서버에 요청한다. 성공하면 서버가 보장한 카드 목록, 실패하면 null.</summary>
    internal static async UniTask<IReadOnlyList<int>> GrantAsync(string _packId)
    {
        // 빈 팩 ID를 그대로 보내면 서버가 invalid-argument 로 답하고, 그 갈래는 Unusable 로 접혀
        // 세션이 차단된다(CloudFailureClassifier) — 저작 실수 하나로 게임이 멎지 않게 왕복 전에 끊는다.
        if (string.IsNullOrEmpty(_packId))
        {
            Debug.LogWarning("[TutorialGrantCommand] No grant pack wired — no grant request is sent (check the pack field in the step authoring).");
            return null;
        }

        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<GrantTutorialCardsResult>(
                GRANT_COMMAND,
                new { env = ContentProfileConfig.Active.CloudEnvId, packId = _packId });

            int t_grantedCount = t_result.Granted != null ? t_result.Granted.Count : 0;
            var t_cardIds = t_result.CardIds ?? (IReadOnlyList<int>)Array.Empty<int>();

            Debug.Log($"[TutorialGrantCommand] Grant done (pack={_packId}) — {t_grantedCount} new out of the {t_cardIds.Count} guaranteed card(s).");

            return t_cardIds;
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            Debug.LogWarning($"[TutorialGrantCommand] The server rejected the grant (pack={_packId}, reason={t_rejected.Reason}) — {t_rejected.Message}");
            return null;
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다 — 여기서 표면을 두 번 칠하지 않는다.
            Debug.LogWarning($"[TutorialGrantCommand] Adopting the response closed the session — {t_adoption.Message}");
            return null;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[TutorialGrantCommand] {GRANT_COMMAND} failed (pack={_packId}) — {t_exception.GetBaseException().Message}");
            return null;
        }
    }
}
