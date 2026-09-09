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
exports.submitMatchResult = void 0;
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const node_crypto_1 = require("node:crypto");
const firestore_1 = require("firebase-admin/firestore");
const firebaseApp_1 = require("../firebaseApp");
const countedTransaction_1 = require("../observability/countedTransaction");
const eventNames_1 = require("../analytics/eventNames");
const analyticsEvent_1 = require("../observability/analyticsEvent");
const missionStore_1 = require("../missions/missionStore");
const period_1 = require("../missions/period");
const matchResult_1 = require("../matchResult");
const payout_1 = require("../payout");
const rewardTable_1 = require("../rewardTable");
const battleCommand_1 = require("../battleCommand");
const payloadGuards_1 = require("../match/payloadGuards");
const deckValidation_1 = require("../deckValidation");
const specBlobReader_1 = require("../specs/specBlobReader");
const matchPairing_1 = require("../matchPairing");
const battleReplayService_1 = require("../battleReplayService");
const battleReplayConfig_1 = require("../battleReplayConfig");
const battleReplayTelemetry_1 = require("../battleReplayTelemetry");
const rankSeason_1 = require("../rank/rankSeason");
const rankStore_1 = require("../rank/rankStore");
// 서버 재생 권위 스위치. true = 승패·잔존의 진실원이 Cloud Run 재생(C# BattleCore)이다.
// 켠 근거: Tools/BattleCoreGolden 이 functions/testdata/golden 코퍼스로 finalStateHash·체크포인트
// 일치를 강제한다. 끄면 두 클라 합의(decideMatch)로 즉시 되돌아간다 — 롤백 레버는 이 한 줄이다.
const SERVER_SIMULATION_AUTHORITATIVE = true;
function persistableReplay(_replay) {
    if (_replay == null)
        return null;
    if (!_replay.ok)
        return { ok: false, reason: _replay.reason };
    return { ok: true, ..._replay.outcome };
}
/**
 * 재생 요청 지문. 트랜잭션 밖에서 받아 온 재생 결과가 **지금 트랜잭션이 보는 입력**의
 * 것인지 확인하는 유일한 수단이다. 지문이 다르면 그 결과를 쓰지 않고 다시 받아 온다.
 * @param {ReplayRequestPayload} _request 재생 서비스로 보낼 요청 본문
 * @return {string} 요청 직렬화의 sha256
 */
function replayFingerprint(_request) {
    return (0, node_crypto_1.createHash)("sha256").update(JSON.stringify(_request)).digest("hex");
}
// 트랜잭션 → 재생 → 트랜잭션. 두 번째 트랜잭션에서 지문이 또 어긋나면(동시 제출로 입력이
// 바뀌었다) 아무것도 쓰지 않고 unavailable 로 내려 클라 재시도에 맡긴다.
// 늘리지 마라 — 재생 호출은 최악 40초라 onCall 시간 예산이 곱으로 늘어난다.
const SETTLE_ATTEMPTS = 2;
const SUBMISSION_DEADLINE_MS = 120000;
function authoritativeInputsAgree(a, b) {
    if (a.uid === b.uid)
        return "same_uid";
    const seedSource = a.seedSource ?? "commit_reveal";
    if (seedSource !== (b.seedSource ?? "commit_reveal"))
        return "seed_source_mismatch";
    if (seedSource === "commit_reveal" &&
        (a.myNonce !== b.opponentNonce || a.opponentNonce !== b.myNonce))
        return "nonce_mismatch";
    if (a.myDeckHash !== b.opponentDeckHash || a.opponentDeckHash !== b.myDeckHash)
        return "deck_mismatch";
    if (a.contentFingerprint !== b.contentFingerprint)
        return "content_mismatch";
    if ((a.commandLogVersion ?? 0) !== 1 || b.commandLogVersion !== 1)
        return "command_log_required";
    if (a.commandLogTruncated || b.commandLogTruncated)
        return "command_log_truncated";
    if (a.commandCount !== b.commandCount || a.commandLogHash !== b.commandLogHash ||
        a.commandLog !== b.commandLog)
        return "command_log_mismatch";
    if ((a.draw ?? false) !== (b.draw ?? false))
        return "draw_conflict";
    // 보드 순서는 서버가 재현할 수 없다(클라 경로에 시드 무관 셔플이 섞인다). 그래서 재시뮬 입력으로
    // 받아 쓰되, 두 클라가 같은 값을 냈을 때만 신뢰한다 — 이게 이 값의 유일한 검증 수단이다.
    if (!(0, matchResult_1.sameBoardOrder)(a, b))
        return "board_order_mismatch";
    return null;
}
function sameNumbers(left, right) {
    return Array.isArray(left) && left.length === right.length &&
        left.every((value, index) => value === right[index]);
}
function parseSubmitData(raw) {
    if (raw == null || typeof raw !== "object")
        throw new https_1.HttpsError("invalid-argument", "payload required");
    const data = raw;
    const env = data.env;
    const matchId = data.matchId;
    const seedSource = data.seedSource == null ? "commit_reveal" : data.seedSource;
    const myNonce = data.myNonce;
    const opponentNonce = data.opponentNonce;
    const myDeckHash = data.myDeckHash;
    const opponentDeckHash = data.opponentDeckHash;
    const finalStateHash = data.finalStateHash;
    const stateHashChain = data.stateHashChain;
    const stateHashChainPrev = data.stateHashChainPrev;
    const stateHashChainLength = data.stateHashChainLength;
    const contentFingerprint = data.contentFingerprint;
    const won = data.won;
    const myRemaining = data.myRemaining;
    const opponentRemaining = data.opponentRemaining;
    const rankPointsBefore = data.rankPointsBefore;
    const commandLogVersion = data.commandLogVersion == null ? 0 : data.commandLogVersion;
    const commandLog = data.commandLog == null ? "" : data.commandLog;
    const commandLogHash = data.commandLogHash == null ? "" : data.commandLogHash;
    const commandCount = data.commandCount == null ? 0 : data.commandCount;
    const commandLogTruncated = data.commandLogTruncated == null ? false : data.commandLogTruncated;
    if ((env !== "live" && env !== "test") || typeof matchId !== "string" || !payloadGuards_1.HEX_32.test(matchId) ||
        (seedSource !== "server" && seedSource !== "commit_reveal") ||
        typeof myDeckHash !== "string" || !payloadGuards_1.HEX_64.test(myDeckHash) ||
        typeof opponentDeckHash !== "string" || !payloadGuards_1.HEX_64.test(opponentDeckHash) ||
        typeof finalStateHash !== "string" || !payloadGuards_1.HEX_16.test(finalStateHash) ||
        typeof stateHashChain !== "string" || !payloadGuards_1.HEX_16.test(stateHashChain) ||
        typeof stateHashChainPrev !== "string" || !payloadGuards_1.HEX_16.test(stateHashChainPrev) ||
        !Number.isInteger(stateHashChainLength) || stateHashChainLength < 0 ||
        stateHashChainLength > 10000 ||
        typeof contentFingerprint !== "string" || !payloadGuards_1.HEX_64.test(contentFingerprint) ||
        typeof won !== "boolean" || !Number.isInteger(myRemaining) || !Number.isInteger(opponentRemaining) ||
        myRemaining < 0 || myRemaining > 12 ||
        opponentRemaining < 0 || opponentRemaining > 12 ||
        !Number.isSafeInteger(rankPointsBefore) || rankPointsBefore < 0) {
        throw new https_1.HttpsError("invalid-argument", "invalid match result payload");
    }
    // 무승부 플래그. 구 클라는 안 보내므로 없으면 false 다.
    const rawDraw = raw.draw;
    if (rawDraw !== undefined && typeof rawDraw !== "boolean") {
        throw new https_1.HttpsError("invalid-argument", "invalid draw flag");
    }
    const draw = rawDraw === true;
    // 종료 시점 해시. 구 클라는 안 보내므로 없으면 undefined 다(그 경우 대조를 건너뛴다).
    const rawEndStateHash = raw.endStateHash;
    let endStateHash;
    if (rawEndStateHash !== undefined && rawEndStateHash !== "") {
        if (typeof rawEndStateHash !== "string" || !payloadGuards_1.HEX_16.test(rawEndStateHash)) {
            throw new https_1.HttpsError("invalid-argument", "invalid end state hash");
        }
        endStateHash = rawEndStateHash;
    }
    // 무승부인데 이겼다고 주장하면 앞뒤가 안 맞는다 — 형식 단계에서 거른다.
    if (draw && won === true) {
        throw new https_1.HttpsError("invalid-argument", "draw cannot be a win");
    }
    // 보드 순서: 소유자 2개 × 덱 장수. 값 자체는 서버가 검증할 수 없고(시드로 재현 불가) 형식만 본다.
    // 진위는 두 클라 제출 대조(sameBoardOrder)가 가른다. 구 클라는 안 보내므로 없어도 통과시킨다.
    const rawBoardOrder = raw.boardOrder;
    // 와이어는 [owner0[], owner1[]] 로 오지만 저장은 맵으로 접는다 —
    // Firestore 가 중첩 배열을 거절한다("Property submissions contains an invalid nested entity").
    let boardOrder;
    if (rawBoardOrder !== undefined) {
        if (!Array.isArray(rawBoardOrder) || rawBoardOrder.length !== 2) {
            throw new https_1.HttpsError("invalid-argument", "invalid board order");
        }
        const sides = rawBoardOrder.map((side) => {
            if (!Array.isArray(side) || side.length > 12) {
                throw new https_1.HttpsError("invalid-argument", "invalid board order");
            }
            return side.map((value) => {
                if (!Number.isInteger(value) || value <= 0) {
                    throw new https_1.HttpsError("invalid-argument", "invalid board order");
                }
                return value;
            });
        });
        boardOrder = { owner0: sides[0], owner1: sides[1] };
    }
    if (!Number.isInteger(commandLogVersion) || (commandLogVersion !== 0 && commandLogVersion !== 1) ||
        typeof commandLog !== "string" || typeof commandLogHash !== "string" ||
        !Number.isInteger(commandCount) || commandCount < 0 ||
        commandCount > battleCommand_1.MAX_BATTLE_COMMANDS ||
        typeof commandLogTruncated !== "boolean") {
        throw new https_1.HttpsError("invalid-argument", "invalid command log metadata");
    }
    if (commandLogVersion === 1) {
        const rawLog = Buffer.from(commandLog, "base64");
        const canonicalBase64 = rawLog.toString("base64");
        const expectedHash = (0, node_crypto_1.createHash)("sha256").update(rawLog).digest("hex");
        const truncatedShape = commandLogTruncated && commandCount === battleCommand_1.MAX_BATTLE_COMMANDS && commandLog === "";
        const completeShape = !commandLogTruncated && canonicalBase64 === commandLog &&
            rawLog.length === commandCount * battleCommand_1.BATTLE_COMMAND_RECORD_BYTES;
        const commandError = completeShape ? (0, battleCommand_1.validateBattleCommands)(rawLog, commandCount) : null;
        if (!payloadGuards_1.HEX_64.test(commandLogHash) || commandLogHash !== expectedHash ||
            (!truncatedShape && !completeShape) || commandError != null) {
            throw new https_1.HttpsError("invalid-argument", "invalid command log payload");
        }
    }
    else if (commandLog !== "" || commandLogHash !== "" || commandCount !== 0 || commandLogTruncated) {
        throw new https_1.HttpsError("invalid-argument", "legacy command log fields must be empty");
    }
    if (seedSource === "commit_reveal" &&
        (typeof myNonce !== "string" || !payloadGuards_1.HEX_16.test(myNonce) ||
            typeof opponentNonce !== "string" || !payloadGuards_1.HEX_16.test(opponentNonce) ||
            (0, matchResult_1.expectedMatchId)(myNonce, opponentNonce) !== matchId)) {
        throw new https_1.HttpsError("invalid-argument", "matchId does not match nonces");
    }
    return { env, matchId, seedSource,
        myNonce: seedSource === "server" ? "" : myNonce,
        opponentNonce: seedSource === "server" ? "" : opponentNonce,
        myDeckHash, opponentDeckHash,
        finalStateHash, stateHashChain, stateHashChainPrev,
        stateHashChainLength: stateHashChainLength, contentFingerprint, won,
        myRemaining: myRemaining, opponentRemaining: opponentRemaining,
        rankPointsBefore: rankPointsBefore,
        commandLogVersion: commandLogVersion,
        commandLog, commandLogHash, commandCount: commandCount, commandLogTruncated,
        boardOrder, draw, endStateHash };
}
// 기본 60초로는 재생 왕복(최악 40초) + 트랜잭션 2회를 못 견딘다.
exports.submitMatchResult = (0, https_1.onCall)({ enforceAppCheck: false, timeoutSeconds: 120 }, async (request) => {
    const uid = request.auth?.uid;
    if (!uid)
        throw new https_1.HttpsError("unauthenticated", "authentication required");
    const data = parseSubmitData(request.data);
    if (data.seedSource !== "server") {
        throw new https_1.HttpsError("failed-precondition", "legacy match results are not authoritative");
    }
    // 토글은 트랜잭션 밖에서 제출당 한 번만 읽는다. off면 Cloud Run 호출 자체를 생략해
    // 대역폭·인스턴스 사용을 멈추고 기존 두 클라이언트 합의 경로로 되돌아간다.
    const replayEnabled = await (0, battleReplayConfig_1.isBattleReplayEnabled)(data.env);
    const matchRef = firebaseApp_1.db.doc(`envs/${data.env}/matches/${data.matchId}`);
    const cardTable = "Card";
    // 표 3개를 블롭으로 읽는다 — 행 문서를 훑으면 제출 1건마다 행 수만큼(Reward 85 · Card 41 …) 과금된다.
    // readSpecRows 가 (env, table) 단위로 5분 캐시를 이미 갖고 있다(specs/specBlobReader.ts).
    // 여기서 다시 캐시하지 마라 — TTL 이 두 벌이 되고 clearSpecCache 로 비워도 이쪽이 옛 값을 계속 준다.
    let rewardRows;
    let rankRows;
    let rankSeason;
    const cardSpecs = new Map();
    try {
        const [rewardSpecRows, rankSpecRows, cardSpecRows, seasonSpecRows] = await Promise.all([
            (0, specBlobReader_1.readSpecRows)(data.env, "Reward"),
            (0, specBlobReader_1.readSpecRows)(data.env, "RankGrade"),
            (0, specBlobReader_1.readSpecRows)(data.env, cardTable),
            (0, specBlobReader_1.readSpecRows)(data.env, "PassSeason"),
        ]);
        rewardRows = (0, rewardTable_1.parseRewardRows)(rewardSpecRows);
        rankRows = (0, payout_1.parseRankGradeRows)(rankSpecRows);
        const activeSeason = (0, rankSeason_1.currentRankSeason)(seasonSpecRows, Date.now());
        if (activeSeason === null)
            throw new Error("no active rank season");
        rankSeason = activeSeason;
        for (const row of cardSpecRows) {
            const spec = (0, deckValidation_1.parseCardSpecRow)(row);
            if (spec == null)
                throw new Error(`invalid card spec:${cardTable}/${row.id}`);
            cardSpecs.set(spec.id, spec);
        }
    }
    catch (error) {
        logger.error("payout_spec_invalid", { env: data.env, error });
        // 스펙 블롭이 깨졌거나 못 읽은 것이라 **제출 잘못이 아니다** — 표를 고치면 같은 제출이 그대로 통과한다.
        // failed-precondition 으로 내리면 클라가 이걸 영구 거절로 읽고 큐에서 버려 보상이 사라진다.
        throw new https_1.HttpsError("unavailable", "payout specs are unavailable");
    }
    // 시너지 3표는 여기서 읽지 않는다 — 재생기가 매치 문서의 specPins 로 고정본을 직접 읽는다.
    // Functions 가 따로 읽으면 전투 도중 표가 재발행됐을 때 두 벌이 갈린다.
    const period = (0, period_1.missionPeriod)(Date.now());
    const settle = async (tx, replay) => {
        const matchSnapshot = await tx.get(matchRef);
        const match = matchSnapshot.data();
        if (data.seedSource === "server") {
            const participantUids = match?.participantUids;
            if (match?.seedSource !== "server" ||
                !Array.isArray(participantUids) || !participantUids.includes(uid)) {
                throw new https_1.HttpsError("permission-denied", "server match identity is not registered");
            }
        }
        const status = typeof match?.status === "string" ? match.status : "pending";
        if (status !== "pending") {
            return { kind: "done", value: { status, reason: match?.reason ?? null } };
        }
        const submissions = { ...match?.submissions };
        const incoming = { ...data, uid, submittedAt: firestore_1.Timestamp.now() };
        delete incoming.env;
        delete incoming.matchId;
        const prior = submissions[uid];
        if (prior && !(0, matchResult_1.sameSubmission)(prior, incoming)) {
            throw new https_1.HttpsError("already-exists", "submission cannot be changed");
        }
        submissions[uid] = prior ?? incoming;
        const entries = Object.values(submissions);
        const createdAt = match?.createdAt instanceof firestore_1.Timestamp ? match.createdAt : firestore_1.Timestamp.now();
        const expiresAt = firestore_1.Timestamp.fromMillis(createdAt.toMillis() + 7 * 24 * 60 * 60 * 1000);
        const expectedParticipants = (0, payloadGuards_1.safeInteger)(match?.expectedParticipants) ?? 2;
        const solo = match?.mode === "solo" && match?.resultProtocol === 1 && expectedParticipants === 1;
        const participantUids = match?.participantUids;
        const approvals = (0, payloadGuards_1.objectRecord)(match?.approvals);
        const soloApproval = solo ? (0, payloadGuards_1.objectRecord)(approvals?.[uid]) : null;
        const aiDeck = solo ? (0, payloadGuards_1.objectRecord)(match?.aiDeck) : null;
        const aiCardIds = aiDeck?.cardIds;
        const aiCardLevel = (0, payloadGuards_1.safeInteger)(aiDeck?.cardLevel);
        const soloAiSnapshots = solo && Array.isArray(aiCardIds) && aiCardLevel != null ?
            (0, deckValidation_1.buildAiDeckSnapshots)(aiCardIds, aiCardLevel, cardSpecs) : null;
        let soloContractReason = null;
        if (solo) {
            const serverOrders = (0, payloadGuards_1.objectRecord)(match?.serverBoardOrders);
            if (!Array.isArray(participantUids) || participantUids.length !== 1 || participantUids[0] !== uid ||
                match?.phase !== "locked" || match?.lockStatus !== "approved") {
                soloContractReason = "solo_match_contract_missing";
            }
            else if (soloApproval?.ownerIndex !== 0 || !Array.isArray(soloApproval?.cardSnapshots) ||
                incoming.myDeckHash !== soloApproval?.deckHash) {
                soloContractReason = "solo_player_deck_mismatch";
            }
            else if (soloAiSnapshots == null || incoming.opponentDeckHash !== (0, deckValidation_1.computeDeckHash)(soloAiSnapshots)) {
                soloContractReason = "solo_ai_deck_mismatch";
            }
            else if (incoming.contentFingerprint !== match?.cardDataVersion) {
                soloContractReason = "solo_content_mismatch";
            }
            else if ((incoming.commandLogVersion ?? 0) !== 1 || incoming.commandLogTruncated) {
                soloContractReason = "solo_command_log_required";
            }
            else if (incoming.boardOrder == null ||
                !sameNumbers(serverOrders?.owner0, incoming.boardOrder.owner0) ||
                !sameNumbers(serverOrders?.owner1, incoming.boardOrder.owner1)) {
                soloContractReason = "solo_board_order_mismatch";
            }
        }
        const rawRulesetVersion = match?.rulesetVersion;
        const rulesetVersion = Number.isInteger(rawRulesetVersion) ? rawRulesetVersion : 0;
        // 재생을 부를지 여부. 셋 다 참이어야 부른다:
        //   replayEnabled  = 운영 토글(envs/{env}/config/battleReplay). 꺼지면 호출 자체를 생략해
        //                    Cloud Run 대역폭이 0이 되고, 정산은 두 클라 합의(decideMatch)로 후퇴한다.
        //   rulesetVersion = 재생기가 아는 규칙 세대(2 이상)
        //   SERVER_SIMULATION_AUTHORITATIVE = 배포로만 되돌리는 코드 킬스위치
        // **토글이 꺼진 동안은 승패 진실원이 클라 합의다** — 치트 방어가 낮아진 상태이므로
        // 릴리즈 관리 창의 경고를 무시하고 오래 꺼 두지 마라.
        const simulateRules = replayEnabled && rulesetVersion >= matchPairing_1.SERVER_AUTHORITATIVE_RULESET_VERSION;
        const authoritativeRules = SERVER_SIMULATION_AUTHORITATIVE && simulateRules;
        const nowMs = firestore_1.Timestamp.now().toMillis();
        const decision = solo ?
            entries.length > 1 ? { status: "flagged", reason: "too_many_submissions" } :
                soloContractReason == null ? { status: "confirmed" } :
                    { status: "flagged", reason: soloContractReason } :
            authoritativeRules ?
                entries.length < 2 ?
                    (nowMs - createdAt.toMillis() > SUBMISSION_DEADLINE_MS ?
                        { status: "flagged", reason: "single_submission" } : { status: "pending" }) :
                    entries.length > 2 ? { status: "flagged", reason: "too_many_submissions" } :
                        (() => {
                            const reason = authoritativeInputsAgree(entries[0], entries[1]);
                            return reason ? { status: "flagged", reason } : { status: "confirmed" };
                        })() :
                (0, matchResult_1.decideMatch)(entries, createdAt.toMillis(), nowMs, SUBMISSION_DEADLINE_MS);
        if (decision.status === "pending") {
            tx.set(matchRef, { status: "pending", submissions, createdAt, expiresAt,
                deadlineAt: firestore_1.Timestamp.fromMillis(createdAt.toMillis() + SUBMISSION_DEADLINE_MS) }, { merge: true });
            return { kind: "done", value: { status: "pending" } };
        }
        if (decision.status === "flagged") {
            tx.set(matchRef, { status: "flagged", reason: decision.reason, submissions,
                replayUnavailable: firestore_1.FieldValue.delete(),
                settledAt: firestore_1.FieldValue.serverTimestamp(), expiresAt }, { merge: true });
            return { kind: "done", value: { status: "flagged", reason: decision.reason }, telemetry: {
                    settled: 1, replayOk: 0, replayFailed: 0, unavailable: 0,
                    divergent: 0, outcomeMismatch: 0, hashMismatch: 0,
                } };
        }
        let serverReplay = null;
        let clientDivergence = null;
        let ownerIndexByUid = null;
        let replayWasUnavailable = false;
        let outcomeMismatch = false;
        let hashMismatch = false;
        if (simulateRules) {
            const seedHex = match?.seedHex;
            const specPins = (0, battleReplayService_1.parseSpecPins)(data.env, match?.specPins);
            // 지문은 클라 제출이 아니라 매치 문서의 값을 쓴다 — 재생기가 specPins 로 읽는 표와
            // 같은 세대인지 검사하는 값이라, 제출값을 넣으면 클라가 검사를 통과시킬 수 있다.
            const contentFingerprint = typeof match?.cardDataVersion === "string" ? match.cardDataVersion : null;
            if (!Array.isArray(participantUids) || participantUids.length !== expectedParticipants ||
                typeof seedHex !== "string" || approvals == null ||
                specPins == null || contentFingerprint == null) {
                serverReplay = { ok: false, reason: "server_match_contract_missing" };
            }
            else {
                const decks = [null, null];
                ownerIndexByUid = {};
                if (solo) {
                    decks[0] = soloApproval?.cardSnapshots;
                    decks[1] = soloAiSnapshots;
                    ownerIndexByUid[uid] = 0;
                }
                else {
                    for (const participant of participantUids) {
                        const approval = (0, payloadGuards_1.objectRecord)(approvals[participant]);
                        if (approval == null)
                            continue;
                        const ownerIndex = approval.ownerIndex;
                        if ((ownerIndex !== 0 && ownerIndex !== 1) || decks[ownerIndex] != null)
                            continue;
                        decks[ownerIndex] = approval.cardSnapshots;
                        ownerIndexByUid[participant] = ownerIndex;
                    }
                }
                const commandLog = entries[0].commandLog ?? "";
                const boardOrder = entries[0].boardOrder;
                if (!Array.isArray(decks[0]) || !Array.isArray(decks[1]) ||
                    (entries[0].commandLogVersion ?? 0) !== 1 || entries[0].commandLogTruncated ||
                    boardOrder == null) {
                    serverReplay = { ok: false, reason: "server_replay_input_missing" };
                }
                else {
                    // 보드 순서는 클라가 실어 보낸 값이다 — 서버는 시드로 재현할 수 없다.
                    // 두 제출이 같은 값을 냈는지는 authoritativeInputsAgree(board_order_mismatch)가 이미 걸렀다.
                    const replayRequest = {
                        env: data.env,
                        rulesetVersion,
                        contentFingerprint,
                        specPins,
                        seedHex,
                        decks: [
                            { ownerIndex: 0, cards: decks[0], boardOrder: boardOrder.owner0 },
                            { ownerIndex: 1, cards: decks[1], boardOrder: boardOrder.owner1 },
                        ],
                        commandLog,
                    };
                    const fingerprint = replayFingerprint(replayRequest);
                    if (replay == null || replay.fingerprint !== fingerprint) {
                        // 재생은 HTTP 호출이라 트랜잭션 안에서 돌릴 수 없다. **아무것도 쓰지 않고** 물러난 뒤
                        // 바깥에서 재생을 받아 같은 입력으로 다시 들어온다(2단계 흐름).
                        return { kind: "need_replay", fingerprint, request: replayRequest };
                    }
                    const verdict = replay.verdict;
                    replayWasUnavailable = verdict.kind === "unavailable";
                    if (verdict.kind === "unavailable" && authoritativeRules) {
                        // 재생기에 닿지 못했다. 여기서 클라 합의로 되돌아가면 권위 전환의 의미가 없고,
                        // flagged 로 닫으면 서비스 장애가 멀쩡한 플레이어의 보상을 지운다 — 제출만 보존하고 연다.
                        // 되밀어 줄 서버 주체가 없어 클라 재제출까지 pending 으로 남는다(replayUnavailable 로 조회).
                        logger.error("battle_replay_unavailable", {
                            matchId: data.matchId, env: data.env, reason: verdict.reason,
                        });
                        tx.set(matchRef, { status: "pending", submissions, createdAt, expiresAt,
                            deadlineAt: firestore_1.Timestamp.fromMillis(createdAt.toMillis() + SUBMISSION_DEADLINE_MS),
                            replayUnavailable: { reason: verdict.reason, at: firestore_1.FieldValue.serverTimestamp() } }, { merge: true });
                        return { kind: "done", value: { status: "pending" }, telemetry: {
                                settled: 0, replayOk: 0, replayFailed: 0, unavailable: 1,
                                divergent: 0, outcomeMismatch: 0, hashMismatch: 0,
                            } };
                    }
                    // 섀도(권위 스위치 off)에서는 재생 실패가 정산을 막지 않는다 — 기록만 하고
                    // 기존 합의 경로(decideMatch)로 계속 간다. 롤백 레버가 실제로 작동하려면 여기가 필요하다.
                    serverReplay = verdict.kind === "ok" ?
                        { ok: true, outcome: verdict.outcome } :
                        { ok: false, reason: verdict.reason };
                }
            }
            const replayReason = serverReplay.ok ? null : serverReplay.reason;
            if (replayReason != null && authoritativeRules) {
                const reason = `server_simulation_${replayReason}`;
                tx.set(matchRef, { status: "flagged", reason, submissions,
                    serverSimulation: persistableReplay(serverReplay),
                    replayUnavailable: firestore_1.FieldValue.delete(),
                    settledAt: firestore_1.FieldValue.serverTimestamp(), expiresAt }, { merge: true });
                return { kind: "done", value: { status: "flagged", reason }, telemetry: {
                        settled: 1, replayOk: 0, replayFailed: 1, unavailable: 0,
                        divergent: 0, outcomeMismatch: 0, hashMismatch: 0,
                    } };
            }
            const outcomeMismatches = [];
            if (serverReplay.ok) {
                const outcome = serverReplay.outcome;
                for (const entry of entries) {
                    const owner = ownerIndexByUid?.[entry.uid] ?? -1;
                    if (owner < 0 || entry.won !== (outcome.winnerOwner === owner) ||
                        entry.myRemaining !== outcome.remaining[owner] ||
                        entry.opponentRemaining !== outcome.remaining[1 - owner]) {
                        outcomeMismatches.push(entry.uid);
                    }
                }
                outcomeMismatch = outcomeMismatches.length > 0;
            }
            // 재생이 실패했으면 "클라와 다르다"가 아니라 "대조를 못 했다"다. 둘을 섞으면 실측이
            // 실패율과 발산율을 구분하지 못한다. Firestore 는 undefined 를 거부하므로 전부 null 로 접는다.
            if (!serverReplay.ok) {
                clientDivergence = {
                    compared: false,
                    reason: replayReason ?? "unknown",
                    submittedStateHash: entries[0].finalStateHash ?? null,
                    serverStateHash: null,
                    outcomeMismatchUids: [],
                };
            }
            else {
                // **finalStateHash 와 비교하면 안 된다** — 그건 마지막으로 두 클라가 합의한 해시라
                // 전투가 끝난 턴의 상태를 담지 못한다(끝 턴은 교환 기회가 없다).
                // endStateHash 가 서버 재생과 같은 시점·같은 계산이다. 없으면(구 클라) 해시 대조를 건너뛴다.
                const clientEnd = entries[0].endStateHash ?? null;
                const hashDiffers = clientEnd != null &&
                    serverReplay.outcome.finalStateHash.toLowerCase() !== clientEnd.toLowerCase();
                hashMismatch = hashDiffers;
                if (hashDiffers || outcomeMismatches.length > 0) {
                    clientDivergence = {
                        compared: true,
                        reason: clientEnd == null ? "end_state_hash_absent" : null,
                        submittedStateHash: clientEnd,
                        serverStateHash: serverReplay.outcome.finalStateHash,
                        outcomeMismatchUids: outcomeMismatches,
                    };
                }
            }
        }
        const rankRefs = entries.map((entry) => (0, rankStore_1.rankRef)(firebaseApp_1.db, data.env, entry.uid));
        const rankStateRefs = entries.map((entry) => firebaseApp_1.db.doc(`envs/${data.env}/users/${entry.uid}/payoutState/current`));
        const saveRefs = entries.map((entry) => firebaseApp_1.db.doc(`envs/${data.env}/users/${entry.uid}/save/current`));
        const rankSnapshots = await tx.getAll(...rankRefs);
        const rankStateSnapshots = await tx.getAll(...rankStateRefs);
        const saveSnapshots = await tx.getAll(...saveRefs);
        // 미션 문서는 payout 쓰기보다 먼저 전부 읽는다. 정산 트랜잭션의 pending -> confirmed 전이가
        // matchId 멱등 게이트라 같은 제출을 다시 보내도 이 경로에는 재진입하지 않는다.
        const missionBumps = [];
        for (const entry of entries) {
            missionBumps.push(await (0, missionStore_1.beginMissionBump)(tx, firebaseApp_1.db, data.env, entry.uid, period));
        }
        const settledAt = firestore_1.Timestamp.now();
        const payoutExpiresAt = firestore_1.Timestamp.fromMillis(settledAt.toMillis() + 180 * 24 * 60 * 60 * 1000);
        const payoutSummary = {};
        // 통계용 승패 사본. 문서에는 payoutSummary 가 이미 담지만 거기서 되읽으면 타입이 unknown 이라
        // 소비자마다 다시 좁혀야 한다 — 승패의 형태를 여기 한 번만 고정한다.
        const settleOutcomes = [];
        for (let i = 0; i < entries.length; i++) {
            const entry = entries[i];
            const storedSequence = rankStateSnapshots[i].data()?.sequence;
            const fallbackPoints = (0, rankStore_1.legacyRankPoints)(rankStateSnapshots[i].data(), saveSnapshots[i].data());
            const fallbackClaimed = (0, rankStore_1.legacyClaimedTiers)(saveSnapshots[i].data(), (0, payout_1.rankTierCount)(rankRows));
            let rankState = (0, rankStore_1.applyRankSeason)((0, rankStore_1.readRank)(rankSnapshots[i], fallbackPoints, fallbackClaimed, rankRows), rankSeason.seasonId, rankRows);
            rankState = (0, rankStore_1.adoptLegacyEntry)(rankState, fallbackPoints, rankRows);
            const rankBefore = rankState.points;
            const rankSequence = Number.isSafeInteger(storedSequence) ? storedSequence + 1 : 1;
            const owner = ownerIndexByUid?.[entry.uid] ?? -1;
            // 권위 모드에서는 서버 재생의 판정을 쓰고, 구 ruleset(섀도)에서는 클라 신고를 쓴다.
            const replayOutcome = authoritativeRules && serverReplay?.ok === true && owner >= 0 ?
                serverReplay.outcome : null;
            const draw = replayOutcome != null ? replayOutcome.draw : entry.draw ?? false;
            const won = draw ? false :
                replayOutcome != null ? replayOutcome.winnerOwner === owner : entry.won;
            const survivorCount = replayOutcome != null ?
                replayOutcome.remaining[owner] ?? entry.myRemaining : entry.myRemaining;
            let currency;
            let rank;
            try {
                // 무승부: 골드는 패배와 같은 정액분을 주고 랭크는 건드리지 않는다.
                // computeRankPayout 은 승패 인자를 요구해서 어느 쪽으로든 점수를 움직인다 — 아예 부르지 않는다.
                currency = (0, payout_1.computeCurrencyPayout)(won, survivorCount, rewardRows);
                rank = draw ?
                    (0, payout_1.computeDrawRankPayout)(rankBefore, rankRows) :
                    (0, payout_1.computeRankPayout)(rankBefore, won, rankRows);
            }
            catch (error) {
                logger.error("payout_calculation_failed", { matchId: data.matchId, uid: entry.uid, error });
                throw new https_1.HttpsError("failed-precondition", "payout calculation failed");
            }
            rankState.points = rank.after;
            rankState.bestTierIndex = Math.max(rankState.bestTierIndex, rank.afterTierIndex);
            const rankProgress = (0, rankStore_1.rankProgressResponse)(rankState);
            const payout = {
                status: "ready",
                env: data.env,
                matchId: data.matchId,
                uid: entry.uid,
                won,
                currency,
                rank,
                rankProgress,
                rankSequence,
                settledAt,
                expiresAt: payoutExpiresAt,
            };
            tx.set(firebaseApp_1.db.doc(`envs/${data.env}/users/${entry.uid}/payouts/${data.matchId}`), payout);
            (0, rankStore_1.writeRank)(tx, rankRefs[i], rankState, settledAt);
            tx.set(rankStateRefs[i], {
                currentPoints: rank.after,
                sequence: rankSequence,
                lastMatchId: data.matchId,
                updatedAt: settledAt,
            }, { merge: true });
            payoutSummary[entry.uid] = { currency, rank, rankProgress, won };
            settleOutcomes.push({
                uid: entry.uid,
                won,
                draw,
                rankBefore,
                rankAfter: rank.after,
            });
        }
        const replayStats = serverReplay?.ok === true ? serverReplay.outcome.stats : undefined;
        const destroyedByOwner = serverReplay?.ok === true ? serverReplay.outcome.destroyedByOwner : undefined;
        const missionNow = firestore_1.FieldValue.serverTimestamp();
        for (let i = 0; i < entries.length; i++) {
            const outcome = settleOutcomes[i];
            const owner = ownerIndexByUid?.[entries[i].uid] ?? (solo ? 0 : -1);
            const bump = missionBumps[i];
            (0, missionStore_1.commitMissionBump)(tx, bump, eventNames_1.EVENTS.battleCompleted.missionKey, 1, missionNow);
            if (outcome.won && serverReplay?.ok === true) {
                (0, missionStore_1.commitMissionBump)(tx, bump, "WinBattle", 1, missionNow);
            }
            if (!solo && outcome.won && serverReplay?.ok === true) {
                (0, missionStore_1.commitMissionBump)(tx, bump, eventNames_1.EVENTS.rankedBattleWon.missionKey, 1, missionNow);
            }
            if (owner < 0 || owner > 1)
                continue;
            const destroyed = destroyedByOwner?.[owner] ?? 0;
            const attacks = replayStats?.attacksByOwner[owner] ?? 0;
            const synergies = replayStats?.synergyFiredByOwner[owner] ?? 0;
            const keywords = replayStats == null ? 0 :
                Object.values(replayStats.keywordsByOwner[owner] ?? {}).reduce((sum, count) => sum + count, 0);
            if (destroyed > 0) {
                (0, missionStore_1.commitMissionBump)(tx, bump, eventNames_1.EVENTS.battleCardsDestroyed.missionKey, destroyed, missionNow);
            }
            if (attacks > 0) {
                (0, missionStore_1.commitMissionBump)(tx, bump, eventNames_1.EVENTS.battleAttacksPerformed.missionKey, attacks, missionNow);
            }
            if (synergies > 0) {
                (0, missionStore_1.commitMissionBump)(tx, bump, eventNames_1.EVENTS.battleSynergiesTriggered.missionKey, synergies, missionNow);
            }
            if (keywords > 0) {
                (0, missionStore_1.commitMissionBump)(tx, bump, eventNames_1.EVENTS.battleKeywordsTriggered.missionKey, keywords, missionNow);
            }
        }
        // 클라 발산율의 유일한 조회 수단이다. 문서에만 쌓으면 집계할 방법이 없다.
        // simulateRules 가 false 여도 찍는다 — 로그가 아예 없으면 "재생이 실패했다"와
        // "재생 대상이 아니었다"를 구분할 수 없고, 그 둘은 원인도 조치도 다르다.
        const replayDivergent = outcomeMismatch || hashMismatch;
        if (replayDivergent) {
            logger.error("battle_replay_divergence", {
                matchId: data.matchId,
                env: data.env,
                outcomeMismatch,
                hashMismatch,
                divergence: clientDivergence,
            });
        }
        logger.info("replay_compare", {
            matchId: data.matchId,
            env: data.env,
            rulesetVersion,
            simulateRules,
            authoritative: authoritativeRules,
            replayed: serverReplay?.ok === true,
            reason: serverReplay?.ok === true ? null : serverReplay?.reason ?? "not_run",
            divergent: replayDivergent,
            divergence: clientDivergence,
        });
        logger.info("match_settled", {
            matchId: data.matchId, env: data.env, status: "confirmed",
            uids: entries.map((entry) => entry.uid),
        });
        // 장애 흔적을 지운다 — 남겨 두면 정상 정산된 매치가 미정산 조회에 계속 걸린다.
        tx.set(matchRef, { status: "confirmed", submissions, payouts: payoutSummary,
            serverSimulation: persistableReplay(serverReplay), clientDivergence,
            replayUnavailable: firestore_1.FieldValue.delete(),
            settledAt: firestore_1.FieldValue.serverTimestamp(), expiresAt }, { merge: true });
        return { kind: "done", value: { status: "confirmed" }, analytics: {
                outcomes: settleOutcomes,
                stats: replayStats,
                destroyedByOwner,
            }, telemetry: {
                settled: 1,
                replayOk: serverReplay?.ok === true ? 1 : 0,
                replayFailed: serverReplay != null && !serverReplay.ok && !replayWasUnavailable ? 1 : 0,
                unavailable: replayWasUnavailable ? 1 : 0,
                divergent: replayDivergent ? 1 : 0,
                outcomeMismatch: outcomeMismatch ? 1 : 0,
                hashMismatch: hashMismatch ? 1 : 0,
            } };
    };
    // 재생은 HTTP 호출이라 Firestore 트랜잭션 안에서 돌릴 수 없다. 트랜잭션이 "이 입력의 재생이
    // 필요하다"고 지문과 함께 물러나면, 여기서 받아 와 같은 입력으로 다시 들어간다.
    let replay = null;
    for (let attempt = 0; attempt < SETTLE_ATTEMPTS; attempt++) {
        const outcome = await (0, countedTransaction_1.withCountedTransaction)("submitMatchResult", (tx) => settle(tx, replay));
        if (outcome.kind === "done") {
            if (outcome.telemetry != null) {
                await (0, battleReplayTelemetry_1.recordReplayDaily)(data.env, outcome.telemetry);
            }
            // 계측 델타가 있다고 정산된 것이 아니다 — 재생 불가로 pending 에 머문 갈래도 델타를 낸다
            // (settled:0). 그 갈래에 이벤트를 내면 매치 하나가 완료로 잡히고, matchId 중복 제거가
            // 나중에 오는 진짜 정산 행을 덮을 수 있다. 실제로 정산이 커밋된 경우에만 낸다.
            if (outcome.telemetry != null && outcome.telemetry.settled > 0) {
                // 제출자 몫의 승패. flagged 정산에는 payout 이 없어 outcomes 가 비고, 그때는 승패를 싣지 않는다
                // — 모르는 것을 false 로 적으면 패배로 집계된다.
                const mine = outcome.analytics?.outcomes.find((entry) => entry.uid === uid);
                (0, analyticsEvent_1.recordEvent)(eventNames_1.EVENTS.battleCompleted.name, {
                    uid,
                    env: data.env,
                    eventId: data.matchId,
                    sourceCommand: "submitMatchResult",
                    result: outcome.value.status,
                    matchId: data.matchId,
                    commandCount: data.commandCount,
                    won: mine?.won ?? null,
                    draw: mine?.draw ?? null,
                    rankBefore: mine?.rankBefore ?? null,
                    rankAfter: mine?.rankAfter ?? null,
                    // 참가자 전원의 결말. 승률은 이 배열로 매치 1행에서 계산한다(참가자별 행이 따로 없다).
                    outcomes: outcome.analytics?.outcomes ?? [],
                    destroyedByOwner: outcome.analytics?.destroyedByOwner ?? null,
                    attacksByOwner: outcome.analytics?.stats?.attacksByOwner ?? null,
                    damageDealtByOwner: outcome.analytics?.stats?.damageDealtByOwner ?? null,
                    healedByOwner: outcome.analytics?.stats?.healedByOwner ?? null,
                    synergyFiredByOwner: outcome.analytics?.stats?.synergyFiredByOwner ?? null,
                    keywordsByOwner: outcome.analytics?.stats?.keywordsByOwner ?? null,
                    turns: outcome.analytics?.stats?.turns ?? null,
                    replayOk: outcome.telemetry.replayOk,
                    replayFailed: outcome.telemetry.replayFailed,
                    replayUnavailable: outcome.telemetry.unavailable,
                    divergent: outcome.telemetry.divergent,
                });
            }
            return outcome.value;
        }
        // 마지막 시도에서 또 need_replay 면 재생을 한 번 더 부를 이유가 없다 — 쓸 트랜잭션이 없다.
        if (attempt + 1 >= SETTLE_ATTEMPTS)
            break;
        replay = { fingerprint: outcome.fingerprint, verdict: await (0, battleReplayService_1.callBattleReplay)(outcome.request) };
    }
    // 지문이 계속 어긋난다 = 재생 입력이 매번 바뀐다(동시 제출 경합). 아무것도 쓰지 않았으므로
    // 제출은 그대로 다시 보낼 수 있다.
    logger.error("battle_replay_fingerprint_unstable", { matchId: data.matchId, env: data.env });
    throw new https_1.HttpsError("unavailable", "battle replay could not be resolved");
});
//# sourceMappingURL=submitMatchResult.js.map