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
exports.ping = void 0;
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const firebaseApp_1 = require("../firebaseApp");
const saveDocument_1 = require("../save/saveDocument");
/**
 * 왕복 진단. 인증이 없어도 던지지 않는다 — 인증이 원인일 때
 * 그 사실 자체를 알려주지 못하면 진단 도구가 아니다.
 */
exports.ping = (0, https_1.onCall)(async (request) => {
    const uid = request.auth?.uid ?? null;
    const env = String(request.data?.env ?? "test");
    let exists = false;
    let revision = 0;
    let documentSchemaVersion = null;
    let readError = null;
    const envKnown = (0, saveDocument_1.isKnownEnv)(env);
    if (uid && envKnown) {
        try {
            const snapshot = await (0, saveDocument_1.saveDocument)(env, uid).get();
            exists = snapshot.exists;
            revision = Number(snapshot.data()?.revision ?? 0);
            documentSchemaVersion = snapshot.data()?.schemaVersion ?? null;
        }
        catch (error) {
            readError = error instanceof Error ? error.message : String(error);
        }
    }
    logger.info("ping", {
        uid,
        env,
        envKnown,
        exists,
        revision,
        serverSchemaVersion: saveDocument_1.SCHEMA_VERSION,
        documentSchemaVersion,
    });
    // 진단 도구가 "정상"이라 답하면 안 되는 경우까지 ok 에 담는다.
    return {
        ok: uid !== null && envKnown && readError === null,
        envKnown,
        uid,
        env,
        database: firebaseApp_1.DATABASE_ID,
        schemaVersion: saveDocument_1.SCHEMA_VERSION,
        // 서버 기대값 옆에 문서의 실제 값을 나란히 둔다 — 쓰기 전에 드리프트를 본다.
        documentSchemaVersion,
        exists,
        revision,
        readError,
    };
});
//# sourceMappingURL=ping.js.map