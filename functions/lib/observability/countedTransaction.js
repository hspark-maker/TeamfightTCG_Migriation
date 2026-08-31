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
exports.withCountedTransaction = withCountedTransaction;
const logger = __importStar(require("firebase-functions/logger"));
const firebaseApp_1 = require("../firebaseApp");
function createCountedTransaction(raw, attempt, onRead) {
    const proxy = new Proxy(raw, {
        get(target, property) {
            if (property === "get") {
                return async (...args) => {
                    const method = target.get;
                    const result = await method.apply(target, args);
                    const querySize = result?.size;
                    const count = typeof querySize === "number" ? Math.max(1, querySize) : 1;
                    attempt.reads += count;
                    onRead(count);
                    return result;
                };
            }
            if (property === "getAll") {
                return async (...args) => {
                    const method = target.getAll;
                    const result = await method.apply(target, args);
                    attempt.reads += result.length;
                    onRead(result.length);
                    return result;
                };
            }
            if (property === "create" || property === "set" || property === "update" || property === "delete") {
                return (...args) => {
                    attempt.writes++;
                    const method = Reflect.get(target, property, target);
                    method.apply(target, args);
                    return proxy;
                };
            }
            const value = Reflect.get(target, property, target);
            return typeof value === "function" ? value.bind(target) : value;
        },
    });
    return proxy;
}
/**
 * 도메인 거절인가. permission-denied · already-exists 두 코드만 도메인 축으로 예약돼 있다
 * (failed-precondition 은 스키마 드리프트·문서 없음에 이미 쓰여 세션 문제와 못 가른다).
 * @param {unknown} error 트랜잭션이 던진 값
 * @return {boolean} 정상 거절이면 true
 */
function isDomainRejection(error) {
    const code = error?.code;
    return code === "permission-denied" || code === "already-exists";
}
async function withCountedTransaction(command, run, extra = {}) {
    const startedAtMs = Date.now();
    let attempts = 0;
    let lastAttempt = { reads: 0, writes: 0 };
    let totalObservedReads = 0;
    try {
        const result = await firebaseApp_1.db.runTransaction(async (raw) => {
            attempts++;
            const attempt = { reads: 0, writes: 0 };
            lastAttempt = attempt;
            const transaction = createCountedTransaction(raw, attempt, (count) => {
                totalObservedReads += count;
            });
            const value = await run(transaction);
            return value;
        });
        logger.info("tx_cost", {
            ...extra,
            command,
            status: "committed",
            attempts,
            lastAttemptReads: lastAttempt.reads,
            lastAttemptWrites: lastAttempt.writes,
            totalObservedReads,
            durationMs: Date.now() - startedAtMs,
        });
        return result;
    }
    catch (error) {
        // 도메인 거절은 실패가 아니라 정상 결과다(재화 부족·이미 수령 …). warn 으로 찍으면
        // 평범한 플레이가 경보 대상으로 올라오고, 진짜 인프라 실패가 그 잡음에 묻힌다.
        // 축은 클라 CloudFailureClassifier 가 예약해 둔 두 코드와 같게 맞춘다 —
        // 여기서 다른 기준을 세우면 같은 사건을 서버와 클라가 다르게 부른다.
        const rejected = isDomainRejection(error);
        const payload = {
            ...extra,
            command,
            status: rejected ? "rejected" : "failed",
            attempts,
            lastAttemptReads: lastAttempt.reads,
            lastAttemptWrites: lastAttempt.writes,
            totalObservedReads,
            durationMs: Date.now() - startedAtMs,
            error: error instanceof Error ? error.message : String(error),
        };
        if (rejected)
            logger.info("tx_cost", payload);
        else
            logger.warn("tx_cost", payload);
        throw error;
    }
}
//# sourceMappingURL=countedTransaction.js.map