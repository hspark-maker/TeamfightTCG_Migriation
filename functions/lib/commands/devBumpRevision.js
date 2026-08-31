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
exports.devBumpRevision = void 0;
const node_crypto_1 = require("node:crypto");
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const saveDocument_1 = require("../save/saveDocument");
const receiptId_1 = require("../save/receiptId");
/**
 * R0 채택 계약 실증용. 서버가 실제로 문서를 쓰고 revision 을 올린 뒤
 * {revision, updatedSlots} 를 돌려준다. R9 에서 debugMutate 로 흡수되거나 삭제된다.
 */
exports.devBumpRevision = (0, https_1.onCall)(async (request) => {
    const uid = (0, saveDocument_1.requireUid)(request.auth);
    const env = String(request.data?.env ?? "");
    // 라이브 문서는 어떤 경우에도 이 함수가 건드리지 않는다.
    if (env !== "test") {
        throw new https_1.HttpsError("permission-denied", "devBumpRevision is available on the test env only.");
    }
    const nickname = request.data?.nickname;
    if (nickname !== undefined && typeof nickname !== "string") {
        throw new https_1.HttpsError("invalid-argument", "nickname must be a string.");
    }
    // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
    const txId = (0, receiptId_1.clientReceiptId)(request.data?.txId, (0, node_crypto_1.randomUUID)());
    // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
    // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
    let replayed = true;
    const result = await (0, saveDocument_1.mutateSave)(env, uid, "devBumpRevision", { kind: "client", txId }, (current) => {
        if (nickname === undefined)
            return { slots: {} };
        // 갱신 후 슬롯 **전체**를 돌려준다 — 클라는 슬롯을 통째로 갈아끼운다.
        const profile = (current.profile ?? {});
        return { slots: { profile: { ...profile, nickname } } };
    }, (adopted) => {
        replayed = false;
        return adopted;
    });
    if (replayed) {
        logger.info("receipt replay", { uid, env, source: "devBumpRevision", txId, revision: result.revision });
    }
    else {
        logger.info("devBumpRevision", { uid, env, revision: result.revision,
            txIdSource: (0, receiptId_1.isClientReceiptId)(request.data?.txId) ? "client" : "server" });
    }
    return result;
});
//# sourceMappingURL=devBumpRevision.js.map