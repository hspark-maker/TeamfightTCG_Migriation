import {FieldValue} from "firebase-admin/firestore";
import {randomUUID} from "node:crypto";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {EVENTS} from "../analytics/eventNames";
import {recordEvent} from "../observability/analyticsEvent";
import {
  isKnownEnv,
  mutateSave,
  requireUid,
  SaveMutation,
} from "../save/saveDocument";
import {rejectDomain} from "../save/domainReject";
import {clientReceiptId, isClientReceiptId} from "../save/receiptId";
import {db} from "../firebaseApp";
import {CurrencyGain, grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {GrantedItems, grantRewardItems, loadItemGrantContext} from "../rewards/itemGrant";
import {applyGuideProgress} from "../missions/guideMutation";
import {applySnackGrowthProgress} from "../missions/snackGrowthProgress";
import {findMission, MAX_MISSION_ID_LENGTH} from "../missions/catalog";
import {readMissionCatalog} from "../missions/missionSpec";
import {rankRef} from "../rank/rankStore";
import {evaluateGuideProgress} from "../missions/guideProgress";
import {judgeMissionClaim, MissionClaimReject} from "../missions/judgeMissionClaim";
import {
  beginMissionBump,
  commitMissionClaim,
  missionResponse,
  MissionResponse,
} from "../missions/missionStore";
import {missionPeriod} from "../missions/period";
import {readSpecRows} from "../packs/packSpecReader";
import {currentPassSeason, parsePassSeasons, PassSeasonDef} from "../pass/passSpec";
import {
  beginPassMutation,
  commitPassExp,
  passProgressResponse,
  PassProgressResponse,
} from "../pass/passStore";
import {
  judgeRewardClaim as judgeSpecRewardClaim,
  parseRewardRows,
} from "../rewardTable";

// 거절 사유(MissionClaimReject)는 순수 판정 모듈이 소유한다 — 판정과 사유가 갈리면
// 한쪽만 늘어난다. 전부 permission-denied 로 나간다(save/domainReject): 수령 실패로 세션을 끊지 않는다.

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
 * 거절 사유별 로그 문구. 사유 코드만으로는 어느 값에 막혔는지 안 보인다.
 * @param {MissionClaimReject} reason 사유 코드
 * @param {string} missionId 미션 id
 * @param {number} progress 현재 진행도
 * @param {number} target 목표치
 * @return {string} 설명
 */
function rejectMessage(
  reason: MissionClaimReject, missionId: string, progress: number, target: number,
): string {
  switch (reason) {
  case "MissionNotFound":
    return `Mission '${missionId}' is not authored.`;
  case "MissionDisabled":
    return `Mission '${missionId}' is disabled by the catalog.`;
  case "RewardNotFound":
    return `Mission '${missionId}' has no authored reward.`;
  case "AlreadyClaimed":
    return `Mission '${missionId}' is already claimed.`;
  default:
    return `Mission '${missionId}' needs ${target}, has ${progress}.`;
  }
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

  // 스펙 읽기는 트랜잭션 밖에서 끝낸다. 재화 보상의 진실원은 Reward 표이고,
  // MissionDef 는 조건·표시·passExp 만 소유한다.
  const [rawRewardRows, catalog, passSeasonRows] = await Promise.all([
    readSpecRows(env, "Reward"),
    readMissionCatalog(env),
    // 표가 아직 발행되지 않은 상태(패스 출시 전)는 정상 경로다 — 미션 수령마다 error 를 찍으면
    // 진짜 장애가 그 소음에 묻힌다. 저작이 깨진 경우(PASS_SPEC_INVALID)만 error 로 남긴다.
    readSpecRows(env, "PassSeason").catch((error) => {
      logger.warn("mission pass accrual skipped", {
        uid, env, code: "PASS_SPEC_UNAVAILABLE", error: String(error),
      });
      return [];
    }),
  ]);
  const rewardRows = parseRewardRows(rawRewardRows);
  const definition = findMission(missionId, catalog);
  const guideCards = definition?.period === "guide" ? await readSpecRows(env, "Card") : [];
  const rewardOwner = definition?.period === "guide" ? "Guide" : "Mission";
  const authored = judgeSpecRewardClaim(rewardRows, rewardOwner, missionId);
  const itemContext = authored.items.length ? await loadItemGrantContext(env, authored.items) : null;
  let itemGrant: GrantedItems = {slots: {}, cards: [], currencies: []};
  let activePassSeason: PassSeasonDef | null = null;
  try {
    activePassSeason = currentPassSeason(parsePassSeasons(passSeasonRows), Date.now());
  } catch (error) {
    // A broken or not-yet-published pass must not block the mission's currency reward.
    logger.error("mission pass accrual skipped", {
      uid, env, code: "PASS_SPEC_INVALID", error: String(error),
    });
  }

  // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
  const txId = clientReceiptId(request.data?.txId, randomUUID());

  // 기간은 여기서 한 번만 잰다 — 콜백은 재실행되므로 그 안에서 재면 경계에 걸린 호출이 흔들린다.
  const period = missionPeriod(Date.now());

  // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
  let replayed = true;
  let progress = 0;
  let missionState: MissionResponse | undefined;
  // 콜백이 확정한 값. 객체 참조로 들고 있으면 TS 가 콜백 밖에서 null 로 좁혀 버리므로
  // openPack 의 drawn·goldBefore 와 같은 관용구로 원시값만 뺀다.
  let missionPeriodKind = "";
  let missionEvent = "";
  let missionTarget = 0;
  let grantedCurrencies: CurrencyGain[] = [];
  let grantedPassExp = 0;
  let passProgress: PassProgressResponse | undefined;

  const result = await mutateSave(env, uid, "claimMission", {kind: "client", txId},
    async (current, transaction, wallet): Promise<SaveMutation> => {
      // 읽기가 콜백의 첫 줄이고, 아래 쓰기보다 앞이다(Firestore 트랜잭션 규칙).
      // beginMissionBump 이 기간 리셋까지 반영하므로, 어제 진행도로 오늘 보상을 타는 경로가 없다.
      const missions = await beginMissionBump(transaction, db, env, uid, period);
      if (definition?.period === "guide") {
        missions.state.progress = evaluateGuideProgress(current, guideCards, catalog, missions.state.progress);
      }
      const rankSnapshot = itemContext === null ? null : await transaction.get(rankRef(db, env, uid));
      // Keep this read before commitMissionClaim and all other transaction writes.
      const pass = activePassSeason === null ? undefined :
        await beginPassMutation(transaction, db, env, uid, activePassSeason.seasonId);

      // 판정은 순수 모듈이 한다 — 여기서 다시 재면 테스트가 보는 규칙과 집행되는 규칙이 갈린다.
      const verdict = judgeMissionClaim(missionId, missions.state, catalog);
      progress = verdict.progress;
      if (!verdict.allow) {
        reject(verdict.reason, rejectMessage(verdict.reason, missionId, verdict.progress,
          verdict.mission?.target ?? 0),
        {uid, env, missionId, progress: verdict.progress, target: verdict.mission?.target ?? 0});
      }

      const mission = verdict.mission;
      const rewardJudgement = judgeSpecRewardClaim(rewardRows, rewardOwner, mission.id);
      if (rewardJudgement.dropped.length > 0) {
        logger.warn("mission reward rows dropped", {
          uid, env, missionId, dropped: rewardJudgement.dropped,
        });
      }
      if (!rewardJudgement.allow) {
        const passExpOnly = !rewardJudgement.specEmpty && mission.passExp > 0 &&
          rewardJudgement.gains.length === 0 && rewardJudgement.items.length === 0 &&
          rewardJudgement.dropped.length === 0;
        if (!passExpOnly) {
          // 사유를 뭉개지 않는다(claimReward 와 같은 정책) — 표를 통째로 못 읽은 것(NotEligible)과
          // 그 미션에만 보상이 없는 것(RewardNotFound)은 운영이 할 일이 다르다.
          // 전자는 배포/업로드 사고이고 후자는 저작 누락이다.
          reject(rewardJudgement.reason,
            rewardJudgement.specEmpty ?
              "Mission reward spec is unreadable." :
              `No reward is authored for Mission/${missionId}.`,
            {uid, env, missionId, specEmpty: rewardJudgement.specEmpty});
        }
        if (pass === undefined) {
          reject("NotEligible", "An active battle pass is required to claim this XP-only mission. Try again when the pass is available.",
            {uid, env, missionId});
        }
      }

      missionPeriodKind = mission.period;
      missionEvent = mission.event;
      missionTarget = mission.target;
      itemGrant = itemContext === null ? {slots: {}, cards: [], currencies: []} :
        grantRewardItems(current, rewardJudgement.items, itemContext, rewardRows, "",
          Number(rankSnapshot?.data()?.points ?? (current.rank as {points?: number})?.points ?? 0));
      grantedCurrencies = [...rewardJudgement.gains, ...itemGrant.currencies];
      grantedPassExp = mission.passExp;
      if (itemContext) applyGuideProgress(missions, current, itemGrant.slots, itemContext.cards, catalog);
      applySnackGrowthProgress(missions, itemGrant.cards);

      // 낙인과 패스 경험치를 함께 찍는다 — 카운터는 깎지 않는다.
      // 깎으면 같은 이벤트를 세는 주간 미션이 함께 무너진다.
      commitMissionClaim(
        transaction, missions, mission.id, mission.passExp, FieldValue.serverTimestamp());
      if (pass !== undefined) {
        commitPassExp(transaction, pass, mission.passExp, FieldValue.serverTimestamp());
        passProgress = passProgressResponse(pass.state);
      }
      missionState = missionResponse(missions.state, period, catalog);

      // 세이브 슬롯은 하나도 건드리지 않는다. mutateSave 가 revision 만 올리고,
      // 그 쓰기가 영수증의 근거가 된다.
      //
      // 줄 재화가 없으면 지갑을 아예 쓰지 않는다(claimReward 와 같은 정책) — 패스 경험치만 주는
      // 미션이 빈 지급으로 지갑 rev 만 올리면 클라가 달라진 것 없는 잔액을 채택한다.
      const currencies = grantedCurrencies;
      return {
        slots: itemGrant.slots,
        wallet: currencies.length === 0 ?
          undefined :
          nextWallet(wallet, grant(wallet.balances, currencies), "claimMission"),
      };
    },
    (adopted) => {
      replayed = false;
      return {
        ...adopted,
        missionId,
        granted: grantedCurrencies,
        cards: itemGrant.cards,
        packs: itemGrant.packs ?? [],
        grantedPassExp,
        missions: missionState,
        pass: passProgress,
      };
    });

  if (replayed) {
    logger.info("receipt replay", {uid, env, source: "claimMission", txId, revision: result.revision});
  } else {
    recordEvent(EVENTS.missionClaimed.name, {
      uid, env, eventId: txId, sourceCommand: "claimMission", result: "success",
      missionId, period: missionPeriodKind, missionEvent,
      progress, target: missionTarget,
      granted: grantedCurrencies.map((gain) => `${gain.currency}+${gain.amount}`).join(","),
      passExp: grantedPassExp,
      passSeasonId: activePassSeason?.seasonId ?? null,
      revision: result.revision,
      txIdSource: isClientReceiptId(request.data?.txId) ? "client" : "server",
    });
  }

  return result;
});
