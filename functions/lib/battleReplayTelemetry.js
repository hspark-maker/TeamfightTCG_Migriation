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
exports.MAX_REPLAY_DAYS = void 0;
exports.utcDay = utcDay;
exports.replayDayRef = replayDayRef;
exports.recordReplayDaily = recordReplayDaily;
const logger = __importStar(require("firebase-functions/logger"));
const firestore_1 = require("firebase-admin/firestore");
const firebaseApp_1 = require("./firebaseApp");
const DAY_MS = 24 * 60 * 60 * 1000;
/** 조회 가능한 최대 일수. 하루 문서 1개라 이 값이 곧 getAll 의 문서 수다. */
exports.MAX_REPLAY_DAYS = 31;
/**
 * UTC 기준 날짜 키. 집계 문서 ID 이자 문서 안의 day 필드다.
 * @param {number} offset 오늘로부터 며칠 전인지(0 = 오늘)
 * @return {string} yyyy-MM-dd
 */
function utcDay(offset = 0) {
    return new Date(Date.now() - offset * DAY_MS).toISOString().slice(0, 10);
}
/**
 * 일별 집계 문서 참조.
 * @param {string} env 환경 id
 * @param {string} day utcDay 가 만든 yyyy-MM-dd
 * @return {DocumentReference} 해당 일자 카운터 문서
 */
function replayDayRef(env, day) {
    // Firestore는 collection/document 교대가 필요하므로 replayDaily 아래에 days 컬렉션을 둔다.
    return firebaseApp_1.db.doc(`envs/${env}/telemetry/replayDaily/days/${day}`);
}
/**
 * 정산 트랜잭션과 분리된 best-effort 집계다. 실패해도 보상 정산은 되돌리지 않는다.
 * @param {string} env 환경 id
 * @param {ReplayDailyDelta} delta 이번 제출이 더할 카운터
 * @return {Promise<void>} 실패는 로그로만 남는다
 */
async function recordReplayDaily(env, delta) {
    try {
        const increments = {
            day: utcDay(),
            updatedAt: firestore_1.FieldValue.serverTimestamp(),
        };
        for (const [key, value] of Object.entries(delta)) {
            if (value !== 0)
                increments[key] = firestore_1.FieldValue.increment(value);
        }
        await replayDayRef(env, increments.day).set(increments, { merge: true });
    }
    catch (error) {
        logger.error("battle_replay_telemetry_write_failed", { env, delta, error });
    }
}
//# sourceMappingURL=battleReplayTelemetry.js.map