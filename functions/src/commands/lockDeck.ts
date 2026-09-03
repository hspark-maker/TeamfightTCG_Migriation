import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue, Timestamp} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {withCountedTransaction} from "../observability/countedTransaction";
import {
  CardSnapshot,
  computeDeckHash,
  LIMIT_BREAK_CURVE_SHRUNK,
  parseCardSpecRow,
  validateDeckShape,
  validateDeckSnapshots,
} from "../deckValidation";
import {authoredMaxLimitBreak, parseCardEnhanceRule} from "../growth/enhanceRules";
import {LimitBreakCurve, parseLimitBreakCurve} from "../growth/limitBreakTable";
import {expectedMatchId} from "../matchResult";
import {HEX_16, HEX_32, HEX_64, objectRecord, safeInteger} from "../match/payloadGuards";
import {readSpecRows} from "../specs/specBlobReader";

const MAX_LOCK_DECK_PAYLOAD_CARDS = 64;
const LOCK_TTL_MS = 60 * 60 * 1000;

type LockDeckData = {
  env: "live" | "test";
  matchId: string;
  seedSource: "server" | "commit_reveal";
  seedHex: string | null;
  rulesetVersion: number | null;
  ownerIndex: number;
  cardDataVersion: string;
  myNonce: string | null;
  opponentNonce: string | null;
  contentFingerprint: string;
  deckHash: string;
  cardSnapshots: CardSnapshot[];
};

function parseLockDeckData(raw: unknown): LockDeckData {
  const data = objectRecord(raw);
  if (data == null) throw new HttpsError("invalid-argument", "payload required");
  const env = data.env;
  const matchId = data.matchId;
  const seedSource = data.seedSource == null ? "commit_reveal" : data.seedSource;
  const seedHex = data.seedHex;
  const rulesetVersion = data.rulesetVersion;
  const ownerIndex = safeInteger(data.ownerIndex);
  const cardDataVersion = data.cardDataVersion ?? data.contentFingerprint;
  const myNonce = data.myNonce;
  const opponentNonce = data.opponentNonce;
  const contentFingerprint = data.contentFingerprint;
  const deckHash = data.deckHash;
  if ((env !== "live" && env !== "test") || typeof matchId !== "string" ||
      (seedSource !== "server" && seedSource !== "commit_reveal") ||
      typeof contentFingerprint !== "string" || typeof deckHash !== "string" ||
      typeof cardDataVersion !== "string" ||
      !HEX_32.test(matchId) || !HEX_64.test(contentFingerprint) ||
      !HEX_64.test(cardDataVersion) || !HEX_64.test(deckHash) ||
      (ownerIndex !== 0 && ownerIndex !== 1)) {
    throw new HttpsError("invalid-argument", "invalid deck lock identity");
  }
  if (seedSource === "server") {
    if (typeof seedHex !== "string" || !HEX_16.test(seedHex) ||
        !Number.isInteger(rulesetVersion) || (rulesetVersion as number) <= 0) {
      throw new HttpsError("invalid-argument", "invalid server seed identity");
    }
  } else {
    if (typeof myNonce !== "string" || typeof opponentNonce !== "string" ||
        !HEX_16.test(myNonce) || !HEX_16.test(opponentNonce) ||
        expectedMatchId(myNonce, opponentNonce) !== matchId) {
      throw new HttpsError("invalid-argument", "matchId does not match nonces");
    }
  }
  if (!Array.isArray(data.cardSnapshots) || data.cardSnapshots.length === 0 ||
      data.cardSnapshots.length > MAX_LOCK_DECK_PAYLOAD_CARDS) {
    throw new HttpsError("invalid-argument", "card snapshots required");
  }

  const cardSnapshots = data.cardSnapshots.map((rawCard): CardSnapshot => {
    const card = objectRecord(rawCard);
    if (card == null) throw new HttpsError("invalid-argument", "invalid card snapshot");
    const cardId = safeInteger(card.cardId);
    const level = safeInteger(card.level);
    const hpBonus = safeInteger(card.hpBonus);
    const evolutionStage = safeInteger(card.evolutionStage);
    const unlockedKeywords = safeInteger(card.unlockedKeywords);
    if (cardId == null || cardId <= 0 || level == null || hpBonus == null ||
        evolutionStage == null || unlockedKeywords == null ||
        typeof card.synergyUnlocked !== "boolean") {
      throw new HttpsError("invalid-argument", "invalid card snapshot fields");
    }
    return {
      cardId,
      level,
      hpBonus,
      evolutionStage,
      unlockedKeywords,
      synergyUnlocked: card.synergyUnlocked,
    };
  });
  return {
    env,
    matchId,
    seedSource,
    seedHex: seedSource === "server" ? seedHex as string : null,
    rulesetVersion: seedSource === "server" ? rulesetVersion as number : null,
    ownerIndex: ownerIndex as number,
    cardDataVersion,
    myNonce: typeof myNonce === "string" ? myNonce : null,
    opponentNonce: typeof opponentNonce === "string" ? opponentNonce : null,
    contentFingerprint,
    deckHash,
    cardSnapshots,
  };
}


export const lockDeck = onCall({enforceAppCheck: false}, async (request) => {
  const uid = request.auth?.uid;
  if (!uid) throw new HttpsError("unauthenticated", "authentication required");
  const data = parseLockDeckData(request.data);
  if (data.seedSource !== "server") {
    throw new HttpsError("failed-precondition", "legacy deck locks are not authoritative");
  }
  const table = "Card";
  const shapeError = validateDeckShape(data.cardSnapshots);
  const cardIds = new Set(data.cardSnapshots.map((card) => card.cardId));
  // 덱에 든 카드만 골라 읽던 자리다. 블롭은 표 전체가 문서 1개라 6장을 개별로 집는 것보다 싸고,
  // 무결성 대조(payloadHash)를 거친 표를 보게 된다 — 예전 경로는 메타 존재 여부만 봤다.
  //
  // 한계돌파 곡선의 진실원도 표다 — 검증기가 순수 모듈이라 여기서 읽어 주입한다.
  // 두 표를 나란히 읽는다: 직렬로 두면 캐시 미스마다 왕복이 하나씩 더 붙는다.
  const [specRows, limitBreakCurve] = await Promise.all([
    (async (): Promise<Record<string, unknown>[]> => {
      if (shapeError != null) return [];
      try {
        const rows = await readSpecRows(data.env, table);
        if (rows.length === 0) throw new HttpsError("unavailable", "card spec table is unavailable");
        return rows;
      } catch (error) {
        if (error instanceof HttpsError) throw error;
        logger.error("lockDeck spec read failed", {env: data.env, table, error});
        throw new HttpsError("unavailable", "card spec read failed");
      }
    })(),
    (async (): Promise<LimitBreakCurve | null> => {
      if (shapeError != null) return null;
      try {
        const [ruleRows, curveRows] = await Promise.all([
          readSpecRows(data.env, "CardEnhanceRule"),
          readSpecRows(data.env, "CardLimitBreak"),
        ]);
        const rule = parseCardEnhanceRule(ruleRows);
        if (rule == null || rule.maxLimitBreak <= 0) {
          logger.error("lockDeck limit break rule is unusable", {
            env: data.env,
            ruleRowCount: ruleRows.length,
            maxLimitBreak: rule == null ? null : rule.maxLimitBreak,
          });
          throw new HttpsError("unavailable", "limit break rule is unavailable");
        }
        // 천장 클램프는 조용하다 — 표가 더 큰 상한을 말했다는 사실은 여기서만 드러난다.
        const authored = authoredMaxLimitBreak(ruleRows);
        if (authored != null && authored > rule.maxLimitBreak) {
          logger.warn("lockDeck limit break max stage was clamped to the code ceiling",
            {env: data.env, authored, clamped: rule.maxLimitBreak});
        }
        const curve = parseLimitBreakCurve(curveRows, rule.maxLimitBreak);
        if (curve == null) {
          logger.error("lockDeck limit break curve is unusable",
            {env: data.env, rowCount: curveRows.length, maxLimitBreak: rule.maxLimitBreak});
          throw new HttpsError("unavailable", "limit break spec table is unavailable");
        }
        return curve;
      } catch (error) {
        if (error instanceof HttpsError) throw error;
        logger.error("lockDeck limit break spec read failed", {env: data.env, error});
        throw new HttpsError("unavailable", "limit break spec read failed");
      }
    })(),
  ]);
  const specs = new Map();
  for (const row of specRows) {
    // 덱에 없는 카드는 파싱하지 않는다 — 표의 다른 행이 깨졌다고 잠금을 막을 이유가 없다.
    if (!cardIds.has(Number(row.id))) continue;
    const spec = parseCardSpecRow(row);
    if (spec == null) {
      logger.error("lockDeck spec row is invalid", {table, id: row.id});
      throw new HttpsError("unavailable", "card spec row is invalid");
    }
    specs.set(spec.id, spec);
  }

  const saveRef = db.doc(`envs/${data.env}/users/${uid}/save/current`);
  // 덱 잠금은 매치 문서 안에 산다 — 별도 matchLocks 컬렉션을 두면 같은 matchId 로 문서가 둘이 되고
  // seedHex·rulesetVersion·cardDataVersion 이 양쪽에 중복된다.
  const matchRef = db.doc(`envs/${data.env}/matches/${data.matchId}`);
  return withCountedTransaction("lockDeck", async (tx) => {
    const matchSnapshot = await tx.get(matchRef);
    const saveSnapshot = await tx.get(saveRef);
    const lock = matchSnapshot.data();
    if (lock?.lockStatus === "rejected") {
      return {status: "rejected", reason: "match_rejected"};
    }
    const approvals = objectRecord(lock?.approvals) ?? {};
    // 승인 정원. 이 필드가 없던 시절의 문서는 전부 대인전이다.
    const expectedParticipants = safeInteger(lock?.expectedParticipants) ?? 2;
    const rejectLock = (reason: string, cardId?: number) => {
      const now = Timestamp.now();
      tx.set(matchRef, {
        matchId: data.matchId,
        env: data.env,
        lockStatus: "rejected",
        lockReason: reason,
        lockRejectedBy: uid,
        lockRejectedAt: now,
        expiresAt: Timestamp.fromMillis(now.toMillis() + LOCK_TTL_MS),
        updatedAt: FieldValue.serverTimestamp(),
      }, {merge: true});
      return cardId == null ?
        {status: "rejected", reason} :
        {status: "rejected", reason, cardId};
    };
    if (data.seedSource === "server") {
      const match = lock;
      const participantUids = match?.participantUids;
      const ownerIndexByUid = objectRecord(match?.ownerIndexByUid);
      // 다섯 조건이 한 메시지로 합쳐져 있으면 거절 원인을 밖에서 알 수 없다.
      // 클라에는 계속 일반화된 메시지를 주되(신원 정보 노출 방지), 어느 항목이 어긋났는지는 로그에 남긴다.
      const identityMismatch: string[] = [];
      if (!Array.isArray(participantUids)) identityMismatch.push("participant_uids_missing");
      else if (!participantUids.includes(uid)) identityMismatch.push("uid_not_participant");
      if (ownerIndexByUid?.[uid] !== data.ownerIndex) identityMismatch.push("owner_index");
      if (match?.seedSource !== "server") identityMismatch.push("seed_source");
      if (match?.seedHex !== data.seedHex) identityMismatch.push("seed_hex");
      if (match?.rulesetVersion !== data.rulesetVersion) identityMismatch.push("ruleset_version");
      if (match?.cardDataVersion !== data.cardDataVersion) identityMismatch.push("card_data_version");
      if (identityMismatch.length > 0) {
        logger.error("lockDeck identity mismatch", {
          matchId: data.matchId, env: data.env, uid, mismatch: identityMismatch,
          expected: {
            ownerIndex: ownerIndexByUid?.[uid] ?? null,
            seedSource: match?.seedSource ?? null,
            seedHex: match?.seedHex ?? null,
            rulesetVersion: match?.rulesetVersion ?? null,
            cardDataVersion: match?.cardDataVersion ?? null,
            participantUids: Array.isArray(participantUids) ? participantUids : null,
          },
          received: {
            ownerIndex: data.ownerIndex,
            seedSource: data.seedSource,
            seedHex: data.seedHex,
            rulesetVersion: data.rulesetVersion,
            cardDataVersion: data.cardDataVersion,
          },
        });
        throw new HttpsError("permission-denied", "server match identity is not registered");
      }

      // 새 AI 매치는 findAiMatch가 플레이어 초기 보드 순서를 먼저 봉인한다. lockDeck이 승인할
      // 카드 스냅샷과 그 순서의 멀티셋이 달라지면, 서버가 승인한 덱과 실제 시작 덱이 갈린다.
      // serverBoardOrders가 없는 구 solo 문서는 종전대로 허용해 혼합 버전 배포를 깨지 않는다.
      const boardOrders = objectRecord(match?.serverBoardOrders);
      if (expectedParticipants === 1 && boardOrders != null) {
        const playerOrder = boardOrders.owner0;
        const lockedIds = data.cardSnapshots.map((card) => card.cardId).sort((a, b) => a - b);
        const parsedOrder = Array.isArray(playerOrder) ?
          playerOrder.map((cardId) => safeInteger(cardId)) : [];
        if (parsedOrder.some((cardId) => cardId == null)) {
          return rejectLock("server_board_order_mismatch");
        }
        const orderedIds = (parsedOrder as number[]).sort((a, b) => a - b);
        if (orderedIds.length !== lockedIds.length ||
            orderedIds.some((cardId, index) => cardId !== lockedIds[index])) {
          return rejectLock("server_board_order_mismatch");
        }
      }
    }
    if (shapeError != null) return rejectLock(shapeError);
    // shapeError 가 없을 때만 곡선을 읽으므로 여기 도달하면 서 있다 — 못 읽었으면 위에서 이미 던졌다.
    if (limitBreakCurve == null) {
      throw new HttpsError("unavailable", "limit break spec table is unavailable");
    }

    if (!saveSnapshot.exists) {
      throw new HttpsError("failed-precondition", "player save is not available");
    }

    const priorApproval = objectRecord(approvals[uid]);
    if (priorApproval != null) {
      if (priorApproval.deckHash === data.deckHash &&
          priorApproval.contentFingerprint === data.contentFingerprint &&
          priorApproval.ownerIndex === data.ownerIndex &&
          (priorApproval.seedSource ?? "commit_reveal") === data.seedSource) {
        const status = Object.keys(approvals).length >= expectedParticipants ? "approved" : "pending";
        return {status, idempotent: true};
      }
      throw new HttpsError("already-exists", "a different deck is already locked");
    }
    if (Object.keys(approvals).length >= expectedParticipants) {
      throw new HttpsError("permission-denied", "match already has all participants");
    }
    for (const [approvedUid, rawApproval] of Object.entries(approvals)) {
      const approval = objectRecord(rawApproval);
      if (approvedUid !== uid && approval?.ownerIndex === data.ownerIndex) {
        return rejectLock("owner_index_conflict");
      }
    }
    if (typeof lock?.cardDataVersion === "string" &&
        lock.cardDataVersion !== data.contentFingerprint) {
      return rejectLock("content_fingerprint_mismatch");
    }
    if (typeof lock?.seedSource === "string" && lock.seedSource !== data.seedSource) {
      return rejectLock("seed_source_mismatch");
    }
    if (data.seedSource === "server" &&
        ((typeof lock?.seedHex === "string" && lock.seedHex !== data.seedHex) ||
         (Number.isInteger(lock?.rulesetVersion) && lock?.rulesetVersion !== data.rulesetVersion) ||
         (typeof lock?.cardDataVersion === "string" && lock.cardDataVersion !== data.cardDataVersion))) {
      return rejectLock("server_match_metadata_mismatch");
    }
    if (computeDeckHash(data.cardSnapshots) !== data.deckHash) {
      return rejectLock("deck_hash_mismatch");
    }

    const validation = validateDeckSnapshots(data.cardSnapshots, specs, saveSnapshot.data(), limitBreakCurve);
    if (!validation.ok) {
      // 표 사고 갈래. 서버가 이미 지급한 단계를 곡선 축소가 부정한 것이라 유저 잘못이 아니다 —
      // rejectLock 으로 접으면 매치 문서에 rejected 가 박혀 아무 잘못 없는 상대 몫까지 탄다.
      if (validation.code === LIMIT_BREAK_CURVE_SHRUNK) {
        logger.error("lockDeck limit break curve is shorter than the saved stage", {
          uid,
          matchId: data.matchId,
          env: data.env,
          cardId: validation.cardId,
          maxStage: limitBreakCurve.maxStage,
        });
        throw new HttpsError("unavailable", "limit break spec table is out of date");
      }
      // 여기부터는 전부 유저 덱이 규칙과 어긋난 갈래다 — 매치를 거절한다.
      logger.warn("lockDeck rejected", {
        uid,
        matchId: data.matchId,
        reason: validation.code,
        cardId: validation.cardId,
      });
      return rejectLock(validation.code, validation.cardId);
    }

    const now = Timestamp.now();
    const revision = safeInteger(saveSnapshot.get("revision")) ?? 0;
    const nextApprovals = {
      ...approvals,
      [uid]: {
        deckHash: data.deckHash,
        // 멱등 판정이 이 필드를 읽는다 — 빠지면 같은 uid의 재호출(폴링)이 전부 already-exists로 떨어진다.
        contentFingerprint: data.contentFingerprint,
        seedSource: data.seedSource,
        seedHex: data.seedHex,
        rulesetVersion: data.rulesetVersion,
        cardDataVersion: data.cardDataVersion,
        ownerIndex: data.ownerIndex,
        myNonce: data.myNonce,
        opponentNonce: data.opponentNonce,
        cardSnapshots: data.cardSnapshots,
        saveRevision: revision,
        approvedAt: now,
      },
    };
    const status = Object.keys(nextApprovals).length >= expectedParticipants ? "approved" : "pending";
    tx.set(matchRef, {
      matchId: data.matchId,
      env: data.env,
      // 페어링 단계로 되돌아가지 못하게 하는 단조 표식. createMatch 가 이 값을 보고 키 재사용을 막는다.
      phase: "locked",
      lockStatus: status,
      seedSource: data.seedSource,
      seedHex: data.seedHex,
      rulesetVersion: data.rulesetVersion,
      cardDataVersion: data.cardDataVersion,
      approvals: nextApprovals,
      expiresAt: Timestamp.fromMillis(now.toMillis() + LOCK_TTL_MS),
      updatedAt: FieldValue.serverTimestamp(),
    }, {merge: true});
    // 2인 매치가 왜 승인 정원을 못 채웠는지는 서버에서만 보인다 — 클라 응답에는 status 밖에 없다.
    // uid 는 앞 6자만 남긴다(로그에 계정 전체를 흘리지 않는다).
    logger.info("lockDeck approval", {
      matchId: data.matchId,
      uid: uid.slice(0, 6),
      ownerIndex: data.ownerIndex,
      status,
      approvals: Object.keys(nextApprovals).length,
      expectedParticipants,
    });
    return {status, idempotent: false};
  });
});
