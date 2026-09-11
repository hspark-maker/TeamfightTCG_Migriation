"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.parseSpecPins = parseSpecPins;
exports.callBattleReplay = callBattleReplay;
const logger = __importStar(require("firebase-functions/logger"));
const requestMetrics_1 = require("./observability/requestMetrics");
const payloadGuards_1 = require("./match/payloadGuards");
const specBlobReader_1 = require("./specs/specBlobReader");
// Cloud Run 콜드 스타트 + specPins 4표 Firestore 로드가 이 안에 들어가야 한다.
// 짧게 잡으면 첫 요청이 통째로 타임아웃해 매치가 미정산으로 밀린다.
const REPLAY_TIMEOUT_MS = 20000;
// 전송 실패만 한 번 더 시도한다. rejected 는 재현 가능한 판정이라 재시도해도 같은 답이 온다.
const REPLAY_TRANSPORT_ATTEMPTS = 2;
// 콜드 스타트 중이면 즉시 재시도해도 같은 자리에서 또 죽는다.
const REPLAY_RETRY_DELAY_MS = 1000;
let cachedIdentityToken = null;
function parseSpecPins(env, raw) {
    const record = (0, payloadGuards_1.objectRecord)(raw);
    if (record == null)
        return null;
    const result = {};
    for (const table of specBlobReader_1.BATTLE_REPLAY_SPEC_TABLES) {
        const pin = (0, payloadGuards_1.objectRecord)(record[table]);
        const blobPath = pin?.blobPath;
        const payloadHash = pin?.payloadHash;
        if (typeof blobPath !== "string" || !blobPath.startsWith(`envs/${env}/specs/`) ||
            typeof payloadHash !== "string" || !/^[0-9a-f]{16}$/i.test(payloadHash))
            return null;
        result[table] = { blobPath, payloadHash: payloadHash.toLowerCase() };
    }
    return result;
}
async function callBattleReplay(request) {
    return (0, requestMetrics_1.measurePhase)("replayTotal", () => callBattleReplayWithRetries(request));
}
async function callBattleReplayWithRetries(request) {
    const serviceUrl = (process.env.BATTLE_REPLAY_URL ?? "").replace(/\/+$/, "");
    if (serviceUrl === "")
        return { kind: "unavailable", reason: "replay_url_missing" };
    let lastTransportReason = "replay_transport";
    for (let attempt = 0; attempt < REPLAY_TRANSPORT_ATTEMPTS; attempt++) {
        try {
            (0, requestMetrics_1.recordMetric)("replayAttempts");
            return await (0, requestMetrics_1.measurePhase)("replayAttempt", () => postReplay(serviceUrl, request));
        }
        catch (error) {
            (0, requestMetrics_1.recordMetric)("replayTransportFailures");
            lastTransportReason = transportReason(error);
            logger.warn("battle_replay_transport_failed", {
                attempt, reason: lastTransportReason, error,
            });
            if (attempt + 1 < REPLAY_TRANSPORT_ATTEMPTS) {
                await new Promise((resolve) => setTimeout(resolve, REPLAY_RETRY_DELAY_MS));
            }
        }
    }
    return { kind: "unavailable", reason: lastTransportReason };
}
async function postReplay(serviceUrl, request) {
    const audience = process.env.BATTLE_REPLAY_AUDIENCE ?? serviceUrl;
    const token = await (0, requestMetrics_1.measurePhase)("replayIdentity", () => identityToken(audience));
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), REPLAY_TIMEOUT_MS);
    let response;
    let data;
    try {
        (0, requestMetrics_1.recordMetric)("replayHttpCalls");
        const received = await (0, requestMetrics_1.measurePhase)("replayHttp", async () => {
            const response = await fetch(`${serviceUrl}/v1/battle/replay`, {
                method: "POST",
                headers: { "content-type": "application/json", "authorization": `Bearer ${token}` },
                body: JSON.stringify(request),
                signal: controller.signal,
            });
            const data = (await response.json().catch(() => ({})));
            return { response, data };
        });
        ({ response, data } = received);
    }
    finally {
        clearTimeout(timeout);
    }
    const reason = typeof data.reason === "string" && data.reason !== "" ? data.reason : "unknown";
    if (response.ok && data.ok === true) {
        const outcome = parseOutcome(data);
        // 200 인데 본문이 어긋나면 **기본값으로 접으면 안 된다** — winnerOwner 가 없어 -1 로 떨어지면
        // 권위 모드에서 양쪽 다 패배로 지급된다. 판정을 못 읽은 것은 판정이 아니다.
        if (outcome == null) {
            logger.error("battle_replay_response_malformed", { body: data });
            return { kind: "unavailable", reason: "replay_response_malformed" };
        }
        return { kind: "ok", outcome };
    }
    // 매치를 닫는(flagged) 것은 **재생기가 실제로 판정한 경우뿐**이다:
    //   409 = 클라가 본 콘텐츠 세대가 매치에 고정된 표와 다르다, 422 = 재생이 이 전투를 거절했다.
    // 400 은 "우리가 서비스가 못 받는 요청을 만들었다"는 뜻이라(ruleset 승격 · specPins 오저작 등)
    // 우리 쪽 결함이다. 이걸 flagged 로 접으면 배포 순서 하나가 어긋나는 순간 전 매치의 보상이 사라진다.
    if (response.status === 409 || response.status === 422) {
        return { kind: "rejected", reason };
    }
    if (response.status === 400) {
        logger.error("battle_replay_request_rejected", { reason });
        return { kind: "unavailable", reason: `replay_request_${reason}` };
    }
    return { kind: "unavailable", reason: `replay_http_${response.status}` };
}
/**
 * 200 응답 본문을 판정으로 받아들일 수 있는지 본다.
 * @param {Record<string, unknown>} data 재생 서비스 응답 본문
 * @return {BattleReplayOutcome | null} 필수 필드가 하나라도 어긋나면 null
 */
function parseOutcome(data) {
    const winnerOwner = (0, payloadGuards_1.safeInteger)(data.winnerOwner);
    const firstOwner = (0, payloadGuards_1.safeInteger)(data.firstOwner);
    const drawCount = (0, payloadGuards_1.safeInteger)(data.drawCount);
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
    return { firstOwner, winnerOwner: winnerOwner, draw, remaining, destroyedByOwner,
        finalStateHash, drawCount, ...(stats == null ? {} : { stats }) };
}
function parseReplayStats(raw) {
    const data = (0, payloadGuards_1.objectRecord)(raw);
    if (data == null)
        return null;
    const attacksByOwner = nonNegativePair(data.attacksByOwner);
    const damageDealtByOwner = nonNegativePair(data.damageDealtByOwner);
    const healedByOwner = nonNegativePair(data.healedByOwner);
    const synergyFiredByOwner = nonNegativePair(data.synergyFiredByOwner);
    const keywordsByOwner = keywordPairs(data.keywordsByOwner);
    const turns = (0, payloadGuards_1.safeInteger)(data.turns);
    if (attacksByOwner == null || damageDealtByOwner == null || healedByOwner == null ||
        synergyFiredByOwner == null || keywordsByOwner == null || turns == null || turns < 0)
        return null;
    return { attacksByOwner, damageDealtByOwner, healedByOwner,
        synergyFiredByOwner, keywordsByOwner, turns };
}
function nonNegativePair(raw) {
    const pair = numberPair(raw);
    return pair != null && pair[0] >= 0 && pair[1] >= 0 ? pair : null;
}
function keywordPairs(raw) {
    if (!Array.isArray(raw) || raw.length !== 2)
        return null;
    const result = [];
    for (const value of raw) {
        const record = (0, payloadGuards_1.objectRecord)(value);
        if (record == null)
            return null;
        const parsed = {};
        for (const [key, count] of Object.entries(record)) {
            const integer = (0, payloadGuards_1.safeInteger)(count);
            if (key.length === 0 || key.length > 32 || integer == null || integer < 0)
                return null;
            parsed[key] = integer;
        }
        result.push(parsed);
    }
    return result;
}
function transportReason(error) {
    if (error instanceof Error && error.name === "AbortError")
        return "replay_timeout";
    if (error instanceof Error && error.message === "identity_token_timeout")
        return "replay_identity_timeout";
    if (error instanceof Error && error.message.startsWith("identity_token_http_"))
        return "replay_identity_token";
    return "replay_transport";
}
async function identityToken(audience) {
    // 정적 토큰은 Firebase emulator에서만 허용한다. 배포 환경은 반드시 metadata ID token을 쓴다.
    const isEmulator = process.env.FUNCTIONS_EMULATOR === "true" || process.env.FIRESTORE_EMULATOR_HOST != null;
    const developmentToken = isEmulator ? process.env.BATTLE_REPLAY_BEARER_TOKEN : undefined;
    if (developmentToken)
        return developmentToken;
    const now = Date.now();
    if (cachedIdentityToken?.audience === audience && cachedIdentityToken.expiresAtMs > now + 60000) {
        return cachedIdentityToken.token;
    }
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 1000);
    try {
        const url = "http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity" +
            `?audience=${encodeURIComponent(audience)}&format=full`;
        const response = await fetch(url, { headers: { "Metadata-Flavor": "Google" }, signal: controller.signal })
            .catch((error) => {
            // 여기서 접지 않으면 metadata 서버 1초 타임아웃이 재생기 지연과 같은 사유로 뭉친다.
            if (error instanceof Error && error.name === "AbortError")
                throw new Error("identity_token_timeout");
            throw error;
        });
        if (!response.ok)
            throw new Error(`identity_token_http_${response.status}`);
        const token = await response.text();
        const payload = JSON.parse(Buffer.from(token.split(".")[1] ?? "", "base64url").toString("utf8"));
        cachedIdentityToken = { audience, token, expiresAtMs: (payload.exp ?? 0) * 1000 };
        return token;
    }
    finally {
        clearTimeout(timeout);
    }
}
function numberPair(raw) {
    if (!Array.isArray(raw) || raw.length !== 2)
        return null;
    const first = (0, payloadGuards_1.safeInteger)(raw[0]);
    const second = (0, payloadGuards_1.safeInteger)(raw[1]);
    if (first == null || second == null)
        return null;
    return [first, second];
}
//# sourceMappingURL=battleReplayService.js.map