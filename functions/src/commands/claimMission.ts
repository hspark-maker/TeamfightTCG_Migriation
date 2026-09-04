import {FieldValue} from "firebase-admin/firestore";
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
import {db} from "../firebaseApp";
import {grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {findMission, MAX_MISSION_ID_LENGTH, missionCatalog} from "../missions/catalog";
import {
  beginMissionBump,
  commitMissionClaim,
  isClaimed,
  progressOf,
} from "../missions/missionStore";
import {missionPeriod} from "../missions/period";

/**
 * 도메인 거절 사유. **와이어 계약**이다 — 클라가 이 문자열을 그대로 대조한다.
 * 전부 permission-denied 로 나간다(save/domainReject): 수령 실패로 세션을 끊지 않는다.
 */
type MissionClaimReject = "MissionNotFound" | "AlreadyClaimed" | "NotEligible";

/**
 * 도메인 거절. 던지기와 로그는 save/domainReject 한 곳이고, 여기 남은 것은 사유 오타를 막는 타입 관문이다.
 * @param {MissionClaimReject} reason 사유 코드
 * @param {string} message 로그용 설명
 * @param {Record<string, unknown>} context 어느 값에 막혔는지
 */
function reject(
  reason: MissionClaimReject,
  message: string,
  context: Record<string, unknown>,
): never {
  rejectDomain(reason, message, context);
}

/**
 * 미션 보상 수령. 달성 판정·지급·낙인을 서버가 소유한다.
 *
 * 진행도는 이 명령이 만들지 않는다 — openPack·enhanceCard·limitBreakCard·claimReward 가
 * 성공 트랜잭션의 부수효과로 올린 값을 여기서 읽기만 한다.
 *
 * **`ClaimReward` 카운터를 올리지 않는다.** 올리면 미션 수령이 미션을 낳는 자기참조가 되고,
 * 일일 "보상 1회 수령" 미션을 그 수령 자체로 채울 수 있다.
 *
 * 지급은 지갑 문서로 나가고 세이브에는 아무 낙인도 남기지 않는다 — 미션 낙인은 미션 문서 소관이다.
 * 그래도 `mutateSave` 를 타는 이유는 영수증(재시도 중복 지급 차단)과 지갑 쓰기 배관이 거기 있어서다.
 */
export const claimMission = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const missionId = String(request.data?.missionId ?? "").trim();

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  if (missionId.length === 0 || missionId.length > MAX_MISSION_ID_LENGTH) {
    throw new HttpsError("invalid-argument", "missionId must be a non-empty string.");
  }

  const mission = findMission(missionId);
  if (mission === null) {
    // 구 클라가 삭제된 미션을 수령하려는 경우다. 카탈로그가 진실원이므로 조용히 거절한다.
    reject("MissionNotFound", `Mission '${missionId}' is not authored.`,
      {uid, env, missionId, catalogSize: missionCatalog().length});
  }

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  // 기간은 여기서 한 번만 잰다 — 콜백은 재실행되므로 그 안에서 재면 경계에 걸린 호출이 흔들린다.
  const period = missionPeriod(Date.now());

  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  let replayed = true;
  let progress = 0;

  const result = await mutateSave(env, uid, "claimMission", {kind: "client", txId},
    async (_current, transaction, wallet): Promise<SaveMutation> => {
      // 읽기가 콜백의 첫 줄이고, 아래 쓰기보다 앞이다(Firestore 트랜잭션 규칙).
      // beginMissionBump 이 기간 리셋까지 반영하므로, 어제 진행도로 오늘 보상을 타는 경로가 없다.
      const missions = await beginMissionBump(transaction, db, env, uid, period);

      // Claimed 검사가 달성 검사보다 먼저다(claimReward 와 같은 순서) — 목표가 나중에 올라가도
      // 이미 받은 보상이 "미달성"으로 되돌아가 재수령 창구가 열리면 안 된다.
      if (isClaimed(missions.state, mission.id)) {
        reject("AlreadyClaimed", `Mission '${mission.id}' is already claimed.`,
          {uid, env, missionId: mission.id, period: mission.period});
      }

      progress = progressOf(missions.state, mission);
      if (progress < mission.target) {
        reject("NotEligible", `Mission '${mission.id}' needs ${mission.target}, has ${progress}.`,
          {uid, env, missionId: mission.id, progress, target: mission.target});
      }

      // 낙인만 찍는다 — 카운터를 깎지 않는다. 깎으면 같은 이벤트를 세는 주간 미션이 함께 무너진다.
      commitMissionClaim(transaction, missions, mission.id, FieldValue.serverTimestamp());

      // 세이브 슬롯은 하나도 건드리지 않는다. mutateSave 가 revision 만 올리고,
      // 그 쓰기가 영수증의 근거가 된다.
      return {
        slots: {},
        wallet: nextWallet(wallet, grant(wallet.balances, mission.reward), "claimMission"),
      };
    },
    (adopted) => {
      replayed = false;
      return {...adopted, missionId: mission.id, granted: mission.reward};
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "claimMission", txId, revision: result.revision});
  } else {
    logger.info("claimMission", {
      uid, env,
      missionId: mission.id, period: mission.period, event: mission.event,
      progress, target: mission.target,
      granted: mission.reward.map((gain) => `${gain.currency}+${gain.amount}`).join(","),
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
});
