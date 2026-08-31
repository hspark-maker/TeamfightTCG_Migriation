import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {
  isKnownEnv,
  mutateSave,
  requireUid,
  SaveMutation,
} from "../save/saveDocument";
import {rejectDomain} from "../save/domainReject";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {readSpecRows} from "../packs/packSpecReader";
import {
  judgeNodeUnlock,
  MAX_NODE_ID_LENGTH,
  parseChapterNodeRows,
  readNodeIdList,
} from "../tournamentTable";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 * AlreadyPending·AlreadyCleared 는 클라가 성공과 같은 자리로 받는다(재시도가 도착한 것이다).
 */
type ReportReject =
  | "NodeNotFound"
  | "ChainBlocked"
  | "RankLocked"
  | "ChainUnreadable"
  | "AlreadyCleared"
  | "AlreadyPending"
  | "SpecUnreadable";

/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {ReportReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(reason: ReportReject, message: string, context: Record<string, unknown>): never {
  rejectDomain(reason, message, context);
}

/**
 * 토너먼트 정점 격파 신고 — 선행 사슬과 랭크 잠금을 서버가 재고 통과하면 미수령 낙인을 세운다.
 * 지급과 클리어 확정은 claimReward 몫이라 여기서는 지갑도 clearedNodeIds 도 건드리지 않는다.
 *
 * won 을 받지 않는다 — 서버가 전투를 검증할 방법이 없어 "항상 true 인 인자"가 되고,
 * 그런 인자는 읽는 사람에게 검증되는 것처럼 보인다. 패배는 아예 호출하지 않는 것이 계약이다.
 */
export const reportTournamentWin = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const nodeId = String(request.data?.nodeId ?? "").trim();

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (nodeId.length === 0 || nodeId.length > MAX_NODE_ID_LENGTH) {
    throw new HttpsError("invalid-argument", "nodeId must be a non-empty string.");
  }

  const context = {uid, env, nodeId};

  // 스펙 읽기는 트랜잭션 밖이다 — 유저 문서와 무관하고, 재실행마다 다시 읽으면 비용만 는다.
  const specRows = await readSpecRows(env, "TournamentChapter");
  const chapterRows = parseChapterNodeRows(specRows);
  if (chapterRows.length === 0) {
    // 표를 통째로 못 읽은 것은 미저작과 다르다 — 배포/업로드 사고이고 유저 잘못이 아니다.
    logger.error("TournamentChapter spec is empty or unreadable",
      {...context, specRowCount: specRows.length});
    reject("SpecUnreadable", "Tournament chapter spec is unreadable.",
      {...context, specRowCount: specRows.length});
  }

  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  let replayed = true;
  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  const result = await mutateSave(env, uid, "reportTournamentWin", {kind: "client", txId},
    (current): SaveMutation => {
      const tournament = current.tournament as Record<string, unknown> | undefined;
      const cleared = readNodeIdList(tournament?.clearedNodeIds);
      const pending = typeof tournament?.pendingRewardNodeId === "string" ?
        tournament.pendingRewardNodeId : "";

      if (cleared.includes(nodeId)) {
        reject("AlreadyCleared", `Tournament node '${nodeId}' is already cleared.`, {...context, pending});
      }
      // 같은 정점을 다시 신고한 것은 재시도다. 여기서 슬롯을 되쓰면 재시도마다 revision 이 올라
      // 클라가 달라진 것 없는 상태를 반복 채택한다.
      if (pending === nodeId) {
        reject("AlreadyPending", `Tournament node '${nodeId}' is already pending.`, {...context});
      }
      // 다른 정점이 미수령으로 남아 있으면 덮지 않는다 — 덮는 순간 그 보상이 소리 없이 사라진다.
      // 수령이 낙인을 비우므로, 유저는 앞의 선물을 받고 다시 신고하면 된다.
      if (pending.length > 0) {
        reject("ChainBlocked", `Tournament node '${pending}' still has an unclaimed reward.`,
          {...context, pending});
      }

      const points = Number((current.rank as Record<string, unknown> | undefined)?.points ?? 0);
      const verdict = judgeNodeUnlock(chapterRows, nodeId,
        new Set(cleared), Number.isSafeInteger(points) && points > 0 ? points : 0);
      if (!verdict.ok) {
        reject(verdict.reason, `Tournament node '${nodeId}' is not unlocked (${verdict.reason}).`,
          {...context, points, clearedCount: cleared.length});
      }

      // 슬롯 **전체 값**을 쓴다 — clearedNodeIds·claimedChapterIds 를 그대로 실어야 지워지지 않는다.
      return {
        slots: {
          tournament: {
            clearedNodeIds: cleared,
            claimedChapterIds: readNodeIdList(tournament?.claimedChapterIds),
            pendingRewardNodeId: nodeId,
          },
        },
      };
    },
    (adopted) => {
      replayed = false;
      return {...adopted, nodeId};
    });

  if (replayed) {
    logger.info("receipt replay",
      {uid, env, source: "reportTournamentWin", txId, revision: result.revision});
  } else {
    logger.info("reportTournamentWin", {
      uid, env, nodeId,
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
});
