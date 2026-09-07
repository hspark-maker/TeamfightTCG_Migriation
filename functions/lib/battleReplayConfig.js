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
exports.isBattleReplayEnabled = isBattleReplayEnabled;
const logger = __importStar(require("firebase-functions/logger"));
const firebaseApp_1 = require("./firebaseApp");
const CACHE_TTL_MS = 60000;
const cache = new Map();
/**
 * Cloud Run 전투 재생 호출 여부를 읽는다.
 * 문서가 없으면 안전한 초기 상태인 off다. 읽기 장애 때는 마지막 관측값을 유지하고,
 * 관측값도 없으면 off로 접되 매번 오류를 남겨 조용한 권위 후퇴를 막는다.
 * @param {string} env 환경 id
 * @return {Promise<boolean>} true 면 재생을 호출하고 그 결과로 정산한다
 */
async function isBattleReplayEnabled(env) {
    const now = Date.now();
    const cached = cache.get(env);
    if (cached != null && cached.expiresAtMs > now)
        return cached.enabled;
    try {
        const snapshot = await firebaseApp_1.db.doc(`envs/${env}/config/battleReplay`).get();
        const enabled = snapshot.exists && snapshot.data()?.enabled === true;
        cache.set(env, { enabled, expiresAtMs: now + CACHE_TTL_MS });
        return enabled;
    }
    catch (error) {
        logger.error("battle_replay_config_read_failed", {
            env,
            fallback: cached?.enabled ?? false,
            hasCachedValue: cached != null,
            error,
        });
        return cached?.enabled ?? false;
    }
}
//# sourceMappingURL=battleReplayConfig.js.map