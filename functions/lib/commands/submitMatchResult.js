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
const matchResult_1 = require("../matchResult");
const payout_1 = require("../payout");
const rewardTable_1 = require("../rewardTable");
const battleCommand_1 = require("../battleCommand");
const payloadGuards_1 = require("../match/payloadGuards");
const battleSimulation_1 = require("../battleSimulation");
const deckValidation_1 = require("../deckValidation");
const specBlobReader_1 = require("../specs/specBlobReader");
const matchPairing_1 = require("../matchPairing");
const synergyRules_1 = require("../synergyRules");
const battleReplayCloud_1 = require("../battleReplayCloud");
// 서버 재시뮬레이션 권위 스위치. 골든 벡터 검증 전까지 섀도(false)로 둔다.
// 켜기 전 확인할 것: (1) C#/TS finalStateHash 일치 벡터, (2) Card 표에 maxHp·synergies·
// defaultEvolutionStage 업로드 완료, (3) clientDivergence 실측률.
const SERVER_SIMULATION_AUTHORITATIVE = false;
// 턴별 체크포인트는 골든 대조용 진단 데이터다 — 매치 문서에 영속할 이유가 없다.
// 명령 상한이 1024라 수백 엔트리가 붙을 수 있고, 같은 문서에 이미 두 클라의 base64 명령 로그가 들어 있다.
function persistableSimulation(_result) {
    if (_result == null)
        return null;
    const rest = { ..._result };
    delete rest.checkpoints;
    // 재생 실패면 winnerOwner/remaining/finalStateHash/drawCount 가 undefined 다.
    // Firestore 는 undefined 를 거부하므로(문서 쓰기 전체가 실패한다) 아예 키를 뺀다.
    for (const key of Object.keys(rest))
        if (rest[key] === undefined)
            delete rest[key];
    return rest;
}
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
exports.submitMatchResult = (0, https_1.onCall)({ enforceAppCheck: false }, async (request) => {
    const uid = request.auth?.uid;
    if (!uid)
        throw new https_1.HttpsError("unauthenticated", "authentication required");
    const data = parseSubmitData(request.data);
    if (data.seedSource !== "server") {
        throw new https_1.HttpsError("failed-precondition", "legacy match results are not authoritative");
    }
    const matchRef = firebaseApp_1.db.doc(`envs/${data.env}/matches/${data.matchId}`);
    const cardTable = "Card";
    // 표 3개를 블롭으로 읽는다 — 행 문서를 훑으면 제출 1건마다 행 수만큼(Reward 85 · Card 41 …) 과금된다.
    // readSpecRows 가 (env, table) 단위로 5분 캐시를 이미 갖고 있다(specs/specBlobReader.ts).
    // 여기서 다시 캐시하지 마라 — TTL 이 두 벌이 되고 clearSpecCache 로 비워도 이쪽이 옛 값을 계속 준다.
    let rewardRows;
    let rankRows;
    let synergyRules = null;
    const cardSpecs = new Map();
    try {
        const [rewardSpecRows, rankSpecRows, cardSpecRows] = await Promise.all([
            (0, specBlobReader_1.readSpecRows)(data.env, "Reward"),
            (0, specBlobReader_1.readSpecRows)(data.env, "RankGrade"),
            (0, specBlobReader_1.readSpecRows)(data.env, cardTable),
        ]);
        rewardRows = (0, rewardTable_1.parseRewardRows)(rewardSpecRows);
        rankRows = (0, payout_1.parseRankGradeRows)(rankSpecRows);
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
    try {
        const [synergyDefRows, synergyTierRows, synergyEffectRows] = await Promise.all([
            (0, specBlobReader_1.readSpecRows)(data.env, "SynergyDef"),
            (0, specBlobReader_1.readSpecRows)(data.env, "SynergyTierDef"),
            (0, specBlobReader_1.readSpecRows)(data.env, "SynergyEffectDef"),
        ]);
        synergyRules = (0, synergyRules_1.parseSynergyRulesCached)(synergyDefRows, synergyTierRows, synergyEffectRows);
    }
    catch (error) {
        // 구 ruleset 정산은 시너지 재생 데이터와 무관하다. 섀도에서는 실패를 기록만 하고,
        // 권위 ruleset에서는 simulateBattle의 synergy_rules_missing이 정산을 fail-closed 한다.
        logger.error("synergy_spec_invalid", { env: data.env, error });
    }
    const result = await (0, countedTransaction_1.withCountedTransaction)("submitMatchResult", async (tx) => {
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
            return { status, reason: match?.reason ?? null };
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
        // 서버 재시뮬레이션은 ruleset 2부터 **돌지만**, 그 결과로 정산할지는 이 스위치가 정한다.
        // false = 섀도: 결과를 문서에 기록만 하고 승패·지급은 기존 두 클라 합의(decideMatch)가 소유한다.
        // C#/TS 골든 벡터로 finalStateHash 일치가 증명되기 전에는 true 로 올리지 마라 —
        // 리졸버가 한 곳만 틀려도 실제 승자가 패배 정산(골드 flat + 랭크 감점)을 받는다.
        const simulateRules = rulesetVersion >= matchPairing_1.SERVER_AUTHORITATIVE_RULESET_VERSION;
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
            return { status: "pending" };
        }
        if (decision.status === "flagged") {
            tx.set(matchRef, { status: "flagged", reason: decision.reason, submissions,
                settledAt: firestore_1.FieldValue.serverTimestamp(), expiresAt }, { merge: true });
            return { status: "flagged", reason: decision.reason };
        }
        let serverSimulation = null;
        let clientDivergence = null;
        let ownerIndexByUid = null;
        if (simulateRules) {
            const seedHex = match?.seedHex;
            if (!Array.isArray(participantUids) || participantUids.length !== expectedParticipants ||
                typeof seedHex !== "string" || approvals == null) {
                serverSimulation = { ok: false, reason: "server_match_contract_missing" };
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
                if (!Array.isArray(decks[0]) || !Array.isArray(decks[1]) ||
                    (entries[0].commandLogVersion ?? 0) !== 1 || entries[0].commandLogTruncated) {
                    serverSimulation = { ok: false, reason: "server_replay_input_missing" };
                }
                else {
                    // 보드 순서는 클라가 실어 보낸 값이다 — 서버는 시드로 재현할 수 없다.
                    // 두 제출이 같은 값을 냈는지는 authoritativeInputsAgree(board_order_mismatch)가 이미 걸렀다.
                    serverSimulation = (0, battleSimulation_1.simulateBattle)({
                        seedHex,
                        decks: [decks[0], decks[1]],
                        specs: cardSpecs,
                        synergyRules,
                        commandLog,
                        boardOrders: entries[0].boardOrder == null ? undefined :
                            [entries[0].boardOrder.owner0, entries[0].boardOrder.owner1],
                    });
                }
            }
            const simulationReason = serverSimulation.ok ? null : serverSimulation.reason ?? "unknown";
            // 섀도에서는 재생 실패가 정산을 막지 않는다 — 기록만 하고 기존 합의 경로로 계속 간다.
            if (simulationReason != null && authoritativeRules) {
                const reason = `server_simulation_${simulationReason}`;
                tx.set(matchRef, { status: "flagged", reason, submissions,
                    serverSimulation: persistableSimulation(serverSimulation),
                    settledAt: firestore_1.FieldValue.serverTimestamp(), expiresAt }, { merge: true });
                return { status: "flagged", reason };
            }
            const outcomeMismatches = [];
            if (serverSimulation.ok) {
                for (const entry of entries) {
                    const owner = ownerIndexByUid?.[entry.uid] ?? -1;
                    if (owner < 0 || entry.won !== (serverSimulation.winnerOwner === owner) ||
                        entry.myRemaining !== serverSimulation.remaining?.[owner] ||
                        entry.opponentRemaining !== serverSimulation.remaining?.[1 - owner]) {
                        outcomeMismatches.push(entry.uid);
                    }
                }
            }
            // 재생이 실패했으면 "클라와 다르다"가 아니라 "대조를 못 했다"다. 둘을 섞으면 섀도 실측이
            // 실패율과 발산율을 구분하지 못한다. Firestore 는 undefined 를 거부하므로 전부 null 로 접는다.
            if (!serverSimulation.ok) {
                clientDivergence = {
                    compared: false,
                    reason: simulationReason ?? "unknown",
                    submittedStateHash: entries[0].finalStateHash ?? null,
                    serverStateHash: null,
                    outcomeMismatchUids: [],
                };
            }
            else {
                // **finalStateHash 와 비교하면 안 된다** — 그건 마지막으로 두 클라가 합의한 해시라
                // 전투가 끝난 턴의 상태를 담지 못한다(끝 턴은 교환 기회가 없다).
                // endStateHash 가 서버 재시뮬과 같은 시점·같은 계산이다. 없으면(구 클라) 해시 대조를 건너뛴다.
                const clientEnd = entries[0].endStateHash ?? null;
                const hashDiffers = clientEnd != null && serverSimulation.finalStateHash !== clientEnd;
                if (hashDiffers || outcomeMismatches.length > 0) {
                    clientDivergence = {
                        compared: true,
                        reason: clientEnd == null ? "end_state_hash_absent" : null,
                        submittedStateHash: clientEnd,
                        serverStateHash: serverSimulation.finalStateHash ?? null,
                        outcomeMismatchUids: outcomeMismatches,
                    };
                }
            }
        }
        const rankStateRefs = entries.map((entry) => firebaseApp_1.db.doc(`envs/${data.env}/users/${entry.uid}/payoutState/current`));
        const saveRefs = entries.map((entry) => firebaseApp_1.db.doc(`envs/${data.env}/users/${entry.uid}/save/current`));
        const rankStateSnapshots = await tx.getAll(...rankStateRefs);
        const missingRankStateIndexes = [];
        const missingSaveRefs = [];
        for (let i = 0; i < rankStateSnapshots.length; i++) {
            if (rankStateSnapshots[i].exists)
                continue;
            missingRankStateIndexes.push(i);
            missingSaveRefs.push(saveRefs[i]);
        }
        const missingSaveSnapshots = missingSaveRefs.length === 0 ? [] :
            await tx.getAll(...missingSaveRefs);
        // payoutState가 있는 사용자는 save 폴백을 쓰지 않으므로 그 칸은 비워 둔다.
        // rankState 스냅샷으로 메우면 폴백이 실제로 걸릴 때(문서는 있는데 currentPoints가 깨진 경우)
        // save가 아니라 payoutState에서 rank.points를 찾게 되어 멀쩡한 계정이 정산 실패로 튕긴다.
        const saveSnapshots = entries.map(() => undefined);
        for (let i = 0; i < missingRankStateIndexes.length; i++) {
            saveSnapshots[missingRankStateIndexes[i]] = missingSaveSnapshots[i];
        }
        const settledAt = firestore_1.Timestamp.now();
        const payoutExpiresAt = firestore_1.Timestamp.fromMillis(settledAt.toMillis() + 180 * 24 * 60 * 60 * 1000);
        const payoutSummary = {};
        for (let i = 0; i < entries.length; i++) {
            const entry = entries[i];
            const storedPoints = rankStateSnapshots[i].data()?.currentPoints;
            const storedSequence = rankStateSnapshots[i].data()?.sequence;
            const saveRank = saveSnapshots[i]?.data()?.rank;
            const savePoints = saveRank?.points;
            const rankBefore = Number.isSafeInteger(storedPoints) ? storedPoints : savePoints;
            const rankSequence = Number.isSafeInteger(storedSequence) ? storedSequence + 1 : 1;
            if (!Number.isSafeInteger(rankBefore) || rankBefore < 0) {
                throw new https_1.HttpsError("failed-precondition", "rank baseline is unavailable");
            }
            if (!rankStateSnapshots[i].exists && entry.rankPointsBefore !== rankBefore) {
                throw new https_1.HttpsError("failed-precondition", "rank baseline does not match server save");
            }
            const owner = ownerIndexByUid?.[entry.uid] ?? -1;
            const authoritative = authoritativeRules && serverSimulation?.ok === true && owner >= 0;
            // 권위 모드에서는 서버 시뮬의 판정을 쓰고, 섀도에서는 클라 신고를 쓴다.
            const draw = authoritative ? serverSimulation?.draw === true : entry.draw ?? false;
            const won = draw ? false :
                authoritative ? serverSimulation?.winnerOwner === owner : entry.won;
            const survivorCount = authoritative ? serverSimulation?.remaining?.[owner] ?? entry.myRemaining : entry.myRemaining;
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
            const payout = {
                status: "ready",
                env: data.env,
                matchId: data.matchId,
                uid: entry.uid,
                won,
                currency,
                rank,
                rankSequence,
                settledAt,
                expiresAt: payoutExpiresAt,
            };
            tx.set(firebaseApp_1.db.doc(`envs/${data.env}/users/${entry.uid}/payouts/${data.matchId}`), payout);
            tx.set(rankStateRefs[i], {
                currentPoints: rank.after,
                sequence: rankSequence,
                lastMatchId: data.matchId,
                updatedAt: settledAt,
            }, { merge: true });
            payoutSummary[entry.uid] = { currency, rank, won };
        }
        // 정산은 서버가 한다(보상·랭크 계산 + payout 문서 작성). 다만 승패 판정의 진실원은
        // 아직 두 클라 합의다 — 재시뮬 결과는 SERVER_SIMULATION_AUTHORITATIVE 가 켜질 때만 승격된다.
        // 섀도 실측의 유일한 조회 수단이다. 문서에만 쌓으면 발산율을 집계할 방법이 없어
        // 권위 전환(SERVER_SIMULATION_AUTHORITATIVE) 시점을 정할 근거가 생기지 않는다.
        // simulateRules 가 false 여도 찍는다 — 로그가 아예 없으면 "재시뮬이 실패했다"와
        // "재시뮬 대상이 아니었다"를 구분할 수 없고, 그 둘은 원인도 조치도 다르다.
        logger.info("shadow_compare", {
            matchId: data.matchId,
            env: data.env,
            rulesetVersion,
            simulateRules,
            simulated: serverSimulation?.ok === true,
            reason: serverSimulation?.ok === true ? null : serverSimulation?.reason ?? "not_run",
            divergent: clientDivergence != null,
            divergence: clientDivergence,
        });
        logger.info("match_settled", {
            matchId: data.matchId, env: data.env, status: "confirmed",
            uids: entries.map((entry) => entry.uid),
        });
        tx.set(matchRef, { status: "confirmed", submissions, payouts: payoutSummary,
            serverSimulation: persistableSimulation(serverSimulation), clientDivergence,
            settledAt: firestore_1.FieldValue.serverTimestamp(), expiresAt }, { merge: true });
        return { status: "confirmed" };
    });
    if (result.status === "confirmed") {
        // 순수 HTTP 재생은 Firestore 트랜잭션 밖에서만 실행한다. 실패는 내부에서 기록하고 기존 정산을 유지한다.
        await (0, battleReplayCloud_1.runCloudReplayShadow)(data.env, data.matchId);
    }
    return result;
});
//# sourceMappingURL=submitMatchResult.js.map