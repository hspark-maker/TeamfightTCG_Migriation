import {FieldValue} from "firebase-admin/firestore";
import * as logger from "firebase-functions/logger";
import {db} from "./firebaseApp";
import {buildAiDeckSnapshots, CardSpecForValidation, parseCardSpecRow} from "./deckValidation";
import {objectRecord, safeInteger} from "./match/payloadGuards";
import {
  BATTLE_REPLAY_SPEC_TABLES,
  readPinnedSpecRows,
  SpecPin,
  SpecPins,
} from "./specs/specBlobReader";

type CloudReplayResult = {
  ok: boolean;
  reason: string;
  firstOwner: number;
  winnerOwner: number;
  draw: boolean;
  remaining: number[];
  destroyedByOwner: number[];
  finalStateHash: string;
  drawCount: number;
};

let cachedIdentityToken: {audience: string; token: string; expiresAtMs: number} | null = null;
let missingUrlLogged = false;

/** 정산 트랜잭션이 끝난 뒤 실행하는 fail-open 섀도 재생. 어떤 실패도 플레이어 정산을 되돌리지 않는다. */
export async function runCloudReplayShadow(env: "live" | "test", matchId: string): Promise<void> {
  const serviceUrl = (process.env.BATTLE_REPLAY_URL ?? "").replace(/\/+$/, "");
  if (serviceUrl === "") {
    if (!missingUrlLogged) {
      missingUrlLogged = true;
      logger.info("cloud_replay_disabled", {reason: "BATTLE_REPLAY_URL_missing"});
    }
    return;
  }

  const matchRef = db.doc(`envs/${env}/matches/${matchId}`);
  try {
    const snapshot = await matchRef.get();
    const match = snapshot.data() as Record<string, unknown> | undefined;
    if (match == null || match.status !== "confirmed" || match.cloudRunSimulation != null) return;

    const specPins = parseSpecPins(env, match.specPins);
    if (specPins == null) {
      logger.warn("cloud_replay_skipped", {env, matchId, reason: "spec_pins_missing"});
      return;
    }

    const request = await buildReplayRequest(env, match, specPins);
    if (request == null) {
      logger.warn("cloud_replay_skipped", {env, matchId, reason: "replay_input_missing"});
      return;
    }

    const result = await callReplay(serviceUrl, request);
    const submissions = Object.values(objectRecord(match.submissions) ?? {})
      .map((value) => objectRecord(value)).filter((value): value is Record<string, unknown> => value != null);
    const ownerIndexByUid = objectRecord(match.ownerIndexByUid) ?? {};
    const mismatchUids: string[] = [];
    if (result.ok) {
      for (const submission of submissions) {
        const uid = typeof submission.uid === "string" ? submission.uid : "";
        const owner = safeInteger(ownerIndexByUid[uid]) ?? -1;
        if (owner < 0 || submission.won !== (result.winnerOwner === owner) ||
            safeInteger(submission.myRemaining) !== result.remaining[owner] ||
            safeInteger(submission.opponentRemaining) !== result.remaining[1 - owner]) {
          mismatchUids.push(uid || "unknown");
        }
      }
    }
    const submittedHash = submissions.length > 0 && typeof submissions[0].endStateHash === "string" ?
      submissions[0].endStateHash : null;
    const divergent = result.ok &&
      ((submittedHash != null && submittedHash.toLowerCase() !== result.finalStateHash.toLowerCase()) ||
       mismatchUids.length > 0);

    await matchRef.set({
      cloudRunSimulation: persistable(result),
      cloudRunDivergence: {
        compared: result.ok,
        reason: result.ok ? null : result.reason,
        submittedStateHash: submittedHash,
        serverStateHash: result.ok ? result.finalStateHash : null,
        outcomeMismatchUids: mismatchUids,
        divergent,
      },
      cloudRunComparedAt: FieldValue.serverTimestamp(),
    }, {merge: true});
    logger.info("cloud_replay_shadow_compare", {
      env, matchId, replayed: result.ok, reason: result.ok ? null : result.reason, divergent,
    });
  } catch (error) {
    logger.error("cloud_replay_shadow_failed", {env, matchId, error});
  }
}

async function buildReplayRequest(env: string, match: Record<string, unknown>, specPins: SpecPins): Promise<unknown | null> {
  const seedHex = typeof match.seedHex === "string" ? match.seedHex : null;
  const rulesetVersion = safeInteger(match.rulesetVersion);
  const contentFingerprint = typeof match.cardDataVersion === "string" ? match.cardDataVersion : null;
  const participants = Array.isArray(match.participantUids) ? match.participantUids : null;
  const approvals = objectRecord(match.approvals);
  const submissions = Object.values(objectRecord(match.submissions) ?? {})
    .map((value) => objectRecord(value)).filter((value): value is Record<string, unknown> => value != null);
  if (seedHex == null || rulesetVersion == null || contentFingerprint == null ||
      participants == null || approvals == null || submissions.length == 0) return null;

  const source = submissions.find((entry) => typeof entry.commandLog === "string");
  const commandLog = source?.commandLog;
  const boardOrder = objectRecord(source?.boardOrder);
  if (typeof commandLog !== "string" || !Array.isArray(boardOrder?.owner0) || !Array.isArray(boardOrder?.owner1)) {
    return null;
  }

  const decks: unknown[] = [null, null];
  const ownerIndexByUid = objectRecord(match.ownerIndexByUid) ?? {};
  const solo = match.mode === "solo" && safeInteger(match.expectedParticipants) === 1;
  if (solo) {
    const uid = typeof participants[0] === "string" ? participants[0] : null;
    const approval = uid == null ? null : objectRecord(approvals[uid]);
    const aiDeck = objectRecord(match.aiDeck);
    const aiCardIds = aiDeck?.cardIds;
    const aiCardLevel = safeInteger(aiDeck?.cardLevel);
    if (approval == null || !Array.isArray(approval.cardSnapshots) ||
        !Array.isArray(aiCardIds) || aiCardLevel == null) return null;

    const cardRows = await readPinnedSpecRows(env, "Card", specPins.Card);
    const cardSpecs = new Map<number, CardSpecForValidation>();
    for (const row of cardRows) {
      const spec = parseCardSpecRow(row);
      if (spec == null) throw new Error(`invalid pinned card spec:${row.id}`);
      cardSpecs.set(spec.id, spec);
    }
    decks[0] = approval.cardSnapshots;
    decks[1] = buildAiDeckSnapshots(aiCardIds as number[], aiCardLevel, cardSpecs);
  } else {
    for (const participant of participants) {
      if (typeof participant !== "string") continue;
      const owner = safeInteger(ownerIndexByUid[participant]);
      const approval = objectRecord(approvals[participant]);
      if ((owner !== 0 && owner !== 1) || !Array.isArray(approval?.cardSnapshots)) continue;
      decks[owner] = approval.cardSnapshots;
    }
  }
  if (!Array.isArray(decks[0]) || !Array.isArray(decks[1])) return null;

  return {
    env,
    rulesetVersion,
    contentFingerprint,
    specPins,
    seedHex,
    decks: [
      {ownerIndex: 0, cards: decks[0], boardOrder: boardOrder.owner0},
      {ownerIndex: 1, cards: decks[1], boardOrder: boardOrder.owner1},
    ],
    commandLog,
  };
}

function parseSpecPins(env: string, raw: unknown): SpecPins | null {
  const record = objectRecord(raw);
  if (record == null) return null;
  const result: Record<string, SpecPin> = {};
  for (const table of BATTLE_REPLAY_SPEC_TABLES) {
    const pin = objectRecord(record[table]);
    const blobPath = pin?.blobPath;
    const payloadHash = pin?.payloadHash;
    if (typeof blobPath !== "string" || !blobPath.startsWith(`envs/${env}/specs/`) ||
        typeof payloadHash !== "string" || !/^[0-9a-f]{16}$/i.test(payloadHash)) return null;
    result[table] = {blobPath, payloadHash: payloadHash.toLowerCase()};
  }
  return result;
}

async function callReplay(serviceUrl: string, body: unknown): Promise<CloudReplayResult> {
  const audience = process.env.BATTLE_REPLAY_AUDIENCE ?? serviceUrl;
  const token = await identityToken(audience);
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 2500);
  try {
    const response = await fetch(`${serviceUrl}/v1/battle/replay`, {
      method: "POST",
      headers: {"content-type": "application/json", authorization: `Bearer ${token}`},
      body: JSON.stringify(body),
      signal: controller.signal,
    });
    const data = await response.json() as Partial<CloudReplayResult>;
    if (!response.ok && typeof data.reason !== "string") throw new Error(`cloud_replay_http_${response.status}`);
    return {
      ok: data.ok === true,
      reason: typeof data.reason === "string" ? data.reason : "unknown",
      firstOwner: safeInteger(data.firstOwner) ?? -1,
      winnerOwner: safeInteger(data.winnerOwner) ?? -1,
      draw: data.draw === true,
      remaining: numberPair(data.remaining),
      destroyedByOwner: numberPair(data.destroyedByOwner),
      finalStateHash: typeof data.finalStateHash === "string" ? data.finalStateHash : "",
      drawCount: safeInteger(data.drawCount) ?? 0,
    };
  } finally {
    clearTimeout(timeout);
  }
}

async function identityToken(audience: string): Promise<string> {
  // 정적 토큰은 Firebase emulator에서만 허용한다. 배포 환경은 반드시 metadata ID token을 쓴다.
  const isEmulator = process.env.FUNCTIONS_EMULATOR === "true" || process.env.FIRESTORE_EMULATOR_HOST != null;
  const developmentToken = isEmulator ? process.env.BATTLE_REPLAY_BEARER_TOKEN : undefined;
  if (developmentToken) return developmentToken;
  const now = Date.now();
  if (cachedIdentityToken?.audience === audience && cachedIdentityToken.expiresAtMs > now + 60_000) {
    return cachedIdentityToken.token;
  }
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 1000);
  try {
    const url = "http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity" +
      `?audience=${encodeURIComponent(audience)}&format=full`;
    const response = await fetch(url, {headers: {"Metadata-Flavor": "Google"}, signal: controller.signal});
    if (!response.ok) throw new Error(`identity_token_http_${response.status}`);
    const token = await response.text();
    const payload = JSON.parse(Buffer.from(token.split(".")[1] ?? "", "base64url").toString("utf8")) as {exp?: number};
    cachedIdentityToken = {audience, token, expiresAtMs: (payload.exp ?? 0) * 1000};
    return token;
  } finally {
    clearTimeout(timeout);
  }
}

function numberPair(raw: unknown): number[] {
  if (!Array.isArray(raw) || raw.length !== 2) return [0, 0];
  return [safeInteger(raw[0]) ?? 0, safeInteger(raw[1]) ?? 0];
}

function persistable(result: CloudReplayResult): Record<string, unknown> {
  return {...result};
}
