import * as logger from "firebase-functions/logger";
import {measurePhase, recordMetric} from "./observability/requestMetrics";
import {objectRecord, safeInteger} from "./match/payloadGuards";
import {BATTLE_REPLAY_SPEC_TABLES, SpecPin, SpecPins} from "./specs/specBlobReader";

// Cloud Run 재생 서비스(Services/BattleReplay)의 클라이언트.
// 전투 규칙의 진실원은 Unity 와 같은 C# BattleCore 다 — Functions 안에 전투 리졸버 사본을 두지 않는다.
// 골든 대조는 Tools/BattleCoreGolden 이 functions/testdata/golden 코퍼스로 건다.

/** 재생이 실제로 돌아 결과를 낸 경우의 판정값. */
export type BattleReplayOutcome = {
  firstOwner: number;
  winnerOwner: number;
  draw: boolean;
  remaining: number[];
  destroyedByOwner: number[];
  finalStateHash: string;
  drawCount: number;
  stats?: BattleReplayStats;
};

export type BattleReplayStats = {
  attacksByOwner: number[];
  damageDealtByOwner: number[];
  healedByOwner: number[];
  synergyFiredByOwner: number[];
  keywordsByOwner: Record<string, number>[];
  turns: number;
};

/**
 * 재생 호출의 세 갈래. 이 구분이 정산 정책을 가른다.
 * - ok: 재생이 끝났다. 승패·잔존은 이 값이 권위다.
 * - rejected: 재생기가 이 제출을 거절했다(입력이 규칙과 안 맞는다). 매치를 flagged 로 닫는다.
 * - unavailable: 재생기에 닿지 못했다. **매치를 닫으면 안 된다** — 서비스 장애로 멀쩡한
 *   플레이어의 보상을 날리는 것이라, 호출부는 pending 을 유지하고 클라 재시도에 맡긴다.
 */
export type ReplayVerdict =
  | {kind: "ok"; outcome: BattleReplayOutcome}
  | {kind: "rejected"; reason: string}
  | {kind: "unavailable"; reason: string};

export type ReplayDeckPayload = {
  ownerIndex: number;
  cards: unknown[];
  boardOrder: number[];
};

export type ReplayRequestPayload = {
  env: string;
  rulesetVersion: number;
  contentFingerprint: string;
  specPins: SpecPins;
  seedHex: string;
  decks: ReplayDeckPayload[];
  commandLog: string;
};

// Cloud Run 콜드 스타트 + specPins 4표 Firestore 로드가 이 안에 들어가야 한다.
// 짧게 잡으면 첫 요청이 통째로 타임아웃해 매치가 미정산으로 밀린다.
const REPLAY_TIMEOUT_MS = 20_000;
// 전송 실패만 한 번 더 시도한다. rejected 는 재현 가능한 판정이라 재시도해도 같은 답이 온다.
const REPLAY_TRANSPORT_ATTEMPTS = 2;
// 콜드 스타트 중이면 즉시 재시도해도 같은 자리에서 또 죽는다.
const REPLAY_RETRY_DELAY_MS = 1_000;

let cachedIdentityToken: {audience: string; token: string; expiresAtMs: number} | null = null;

export function parseSpecPins(env: string, raw: unknown): SpecPins | null {
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

export async function callBattleReplay(request: ReplayRequestPayload): Promise<ReplayVerdict> {
  return measurePhase("replayTotal", () => callBattleReplayWithRetries(request));
}

async function callBattleReplayWithRetries(request: ReplayRequestPayload): Promise<ReplayVerdict> {
  const serviceUrl = (process.env.BATTLE_REPLAY_URL ?? "").replace(/\/+$/, "");
  if (serviceUrl === "") return {kind: "unavailable", reason: "replay_url_missing"};

  let lastTransportReason = "replay_transport";
  for (let attempt = 0; attempt < REPLAY_TRANSPORT_ATTEMPTS; attempt++) {
    try {
      recordMetric("replayAttempts");
      return await measurePhase("replayAttempt", () => postReplay(serviceUrl, request));
    } catch (error) {
      recordMetric("replayTransportFailures");
      lastTransportReason = transportReason(error);
      logger.warn("battle_replay_transport_failed", {
        attempt, reason: lastTransportReason, error,
      });
      if (attempt + 1 < REPLAY_TRANSPORT_ATTEMPTS) {
        await new Promise((resolve) => setTimeout(resolve, REPLAY_RETRY_DELAY_MS));
      }
    }
  }
  return {kind: "unavailable", reason: lastTransportReason};
}

async function postReplay(serviceUrl: string, request: ReplayRequestPayload): Promise<ReplayVerdict> {
  const audience = process.env.BATTLE_REPLAY_AUDIENCE ?? serviceUrl;
  const token = await measurePhase("replayIdentity", () => identityToken(audience));
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REPLAY_TIMEOUT_MS);
  let response: Response;
  let data: Record<string, unknown>;
  try {
    recordMetric("replayHttpCalls");
    const received = await measurePhase("replayHttp", async () => {
      const response = await fetch(`${serviceUrl}/v1/battle/replay`, {
        method: "POST",
        headers: {"content-type": "application/json", "authorization": `Bearer ${token}`},
        body: JSON.stringify(request),
        signal: controller.signal,
      });
      const data = (await response.json().catch(() => ({}))) as Record<string, unknown>;
      return {response, data};
    });
    ({response, data} = received);
  } finally {
    clearTimeout(timeout);
  }

  const reason = typeof data.reason === "string" && data.reason !== "" ? data.reason : "unknown";
  if (response.ok && data.ok === true) {
    const outcome = parseOutcome(data);
    // 200 인데 본문이 어긋나면 **기본값으로 접으면 안 된다** — winnerOwner 가 없어 -1 로 떨어지면
    // 권위 모드에서 양쪽 다 패배로 지급된다. 판정을 못 읽은 것은 판정이 아니다.
    if (outcome == null) {
      logger.error("battle_replay_response_malformed", {body: data});
      return {kind: "unavailable", reason: "replay_response_malformed"};
    }
    return {kind: "ok", outcome};
  }
  // 매치를 닫는(flagged) 것은 **재생기가 실제로 판정한 경우뿐**이다:
  //   409 = 클라가 본 콘텐츠 세대가 매치에 고정된 표와 다르다, 422 = 재생이 이 전투를 거절했다.
  // 400 은 "우리가 서비스가 못 받는 요청을 만들었다"는 뜻이라(ruleset 승격 · specPins 오저작 등)
  // 우리 쪽 결함이다. 이걸 flagged 로 접으면 배포 순서 하나가 어긋나는 순간 전 매치의 보상이 사라진다.
  if (response.status === 409 || response.status === 422) {
    return {kind: "rejected", reason};
  }
  if (response.status === 400) {
    logger.error("battle_replay_request_rejected", {reason});
    return {kind: "unavailable", reason: `replay_request_${reason}`};
  }
  return {kind: "unavailable", reason: `replay_http_${response.status}`};
}

/**
 * 200 응답 본문을 판정으로 받아들일 수 있는지 본다.
 * @param {Record<string, unknown>} data 재생 서비스 응답 본문
 * @return {BattleReplayOutcome | null} 필수 필드가 하나라도 어긋나면 null
 */
function parseOutcome(data: Record<string, unknown>): BattleReplayOutcome | null {
  const winnerOwner = safeInteger(data.winnerOwner);
  const firstOwner = safeInteger(data.firstOwner);
  const drawCount = safeInteger(data.drawCount);
  const remaining = numberPair(data.remaining);
  const destroyedByOwner = numberPair(data.destroyedByOwner);
  const finalStateHash = typeof data.finalStateHash === "string" ? data.finalStateHash : "";
  const draw = data.draw === true;
  // 무승부면 승자가 없다(-1). 그 밖에는 반드시 0 또는 1 이다.
  const winnerValid = draw ? winnerOwner != null : winnerOwner === 0 || winnerOwner === 1;
  if (!winnerValid || firstOwner == null || drawCount == null ||
      remaining == null || destroyedByOwner == null ||
      !/^[0-9a-f]{16}$/i.test(finalStateHash)) {
    return null;
  }
  const stats = parseReplayStats(data.stats);
  return {firstOwner, winnerOwner: winnerOwner as number, draw, remaining, destroyedByOwner,
    finalStateHash, drawCount, ...(stats == null ? {} : {stats})};
}

function parseReplayStats(raw: unknown): BattleReplayStats | null {
  const data = objectRecord(raw);
  if (data == null) return null;
  const attacksByOwner = nonNegativePair(data.attacksByOwner);
  const damageDealtByOwner = nonNegativePair(data.damageDealtByOwner);
  const healedByOwner = nonNegativePair(data.healedByOwner);
  const synergyFiredByOwner = nonNegativePair(data.synergyFiredByOwner);
  const keywordsByOwner = keywordPairs(data.keywordsByOwner);
  const turns = safeInteger(data.turns);
  if (attacksByOwner == null || damageDealtByOwner == null || healedByOwner == null ||
      synergyFiredByOwner == null || keywordsByOwner == null || turns == null || turns < 0) return null;
  return {attacksByOwner, damageDealtByOwner, healedByOwner,
    synergyFiredByOwner, keywordsByOwner, turns};
}

function nonNegativePair(raw: unknown): number[] | null {
  const pair = numberPair(raw);
  return pair != null && pair[0] >= 0 && pair[1] >= 0 ? pair : null;
}

function keywordPairs(raw: unknown): Record<string, number>[] | null {
  if (!Array.isArray(raw) || raw.length !== 2) return null;
  const result: Record<string, number>[] = [];
  for (const value of raw) {
    const record = objectRecord(value);
    if (record == null) return null;
    const parsed: Record<string, number> = {};
    for (const [key, count] of Object.entries(record)) {
      const integer = safeInteger(count);
      if (key.length === 0 || key.length > 32 || integer == null || integer < 0) return null;
      parsed[key] = integer;
    }
    result.push(parsed);
  }
  return result;
}

function transportReason(error: unknown): string {
  if (error instanceof Error && error.name === "AbortError") return "replay_timeout";
  if (error instanceof Error && error.message === "identity_token_timeout") return "replay_identity_timeout";
  if (error instanceof Error && error.message.startsWith("identity_token_http_")) return "replay_identity_token";
  return "replay_transport";
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
    const response = await fetch(url, {headers: {"Metadata-Flavor": "Google"}, signal: controller.signal})
      .catch((error) => {
        // 여기서 접지 않으면 metadata 서버 1초 타임아웃이 재생기 지연과 같은 사유로 뭉친다.
        if (error instanceof Error && error.name === "AbortError") throw new Error("identity_token_timeout");
        throw error;
      });
    if (!response.ok) throw new Error(`identity_token_http_${response.status}`);
    const token = await response.text();
    const payload = JSON.parse(Buffer.from(token.split(".")[1] ?? "", "base64url").toString("utf8")) as {exp?: number};
    cachedIdentityToken = {audience, token, expiresAtMs: (payload.exp ?? 0) * 1000};
    return token;
  } finally {
    clearTimeout(timeout);
  }
}

function numberPair(raw: unknown): number[] | null {
  if (!Array.isArray(raw) || raw.length !== 2) return null;
  const first = safeInteger(raw[0]);
  const second = safeInteger(raw[1]);
  if (first == null || second == null) return null;
  return [first, second];
}
