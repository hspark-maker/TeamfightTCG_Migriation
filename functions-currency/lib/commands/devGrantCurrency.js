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
exports.devGrantCurrency = void 0;
const node_crypto_1 = require("node:crypto");
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const currencyKeys_1 = require("../generated/currency/currencyKeys");
const wallet_1 = require("../generated/currency/wallet");
const walletStore_1 = require("../generated/currency/walletStore");
const walletTransaction_1 = require("../currency/walletTransaction");
const receiptId_1 = require("../generated/save/receiptId");
/**
 * 디버그 재화 지급. 클라 디버그 오버레이가 부르는 test env 전용 통로다.
 * 지갑 문서만 쓴다 — 세이브 진행도와는 무관하다.
 *
 * 여기서는 invalid-argument 를 던진다 — 도메인 명령이었다면 클라 CloudFailureClassifier 가
 * 세션을 끊어 문제였겠지만, 이 함수는 라이브에서 아예 닿지 않는 디버그 경로라
 * 잘못된 인자로 세션이 막히는 것이 오히려 옳은 신호다.
 *
 * 이 codebase 로 옮겨 온 첫 **지갑 쓰기** 명령이다(C6.6). 응답 모양은 default 에 있던 때와 같다
 * — 클라 계약이라 codebase 이사로 바뀌면 안 된다.
 */
exports.devGrantCurrency = (0, https_1.onCall)(async (request) => {
    var _a, _b, _c, _d, _e, _f, _g, _h, _j;
    // requireUid(save/saveDocument)는 firebase-admin·세이브 문서를 물고 있어 이 codebase 로 넘어오지 않는다.
    // 인증 관문은 currencyPing 과 같은 3줄짜리 지역 관용구로 둔다 — 코드·메시지는 옛 requireUid 그대로다.
    const uid = (_a = request.auth) === null || _a === void 0 ? void 0 : _a.uid;
    if (!uid) {
        throw new https_1.HttpsError("unauthenticated", "Sign-in is required.");
    }
    const env = String((_c = (_b = request.data) === null || _b === void 0 ? void 0 : _b.env) !== null && _c !== void 0 ? _c : "");
    // 라이브 문서는 어떤 경우에도 이 함수가 건드리지 않는다.
    if (env !== "test") {
        throw new https_1.HttpsError("permission-denied", "devGrantCurrency is available on the test env only.");
    }
    const requestedCurrency = String((_e = (_d = request.data) === null || _d === void 0 ? void 0 : _d.currency) !== null && _e !== void 0 ? _e : "");
    const currency = currencyKeys_1.CURRENCY_KEYS.find((key) => key === requestedCurrency);
    if (currency === undefined) {
        throw new https_1.HttpsError("invalid-argument", `Unknown currency: ${requestedCurrency}`);
    }
    const amount = Number((_g = (_f = request.data) === null || _f === void 0 ? void 0 : _f.amount) !== null && _g !== void 0 ? _g : 0);
    if (!Number.isSafeInteger(amount) || amount <= 0) {
        throw new https_1.HttpsError("invalid-argument", "amount must be a positive safe integer.");
    }
    // 콜백이 돌았는가 — 영수증 히트로 첫 응답을 되돌려준 호출은 집행 로그를 찍으면 거짓말이 된다.
    // finalize 안에서 뒤집는다 — 트랜잭션 재실행마다 다시 돌아도 결과가 같다.
    let replayed = true;
    // txId 가 없거나 형식을 벗어나면 서버가 발급한다 — 구 클라를 거절하면 세션이 끊긴다.
    const txId = (0, receiptId_1.clientReceiptId)((_h = request.data) === null || _h === void 0 ? void 0 : _h.txId, (0, node_crypto_1.randomUUID)());
    const result = await (0, walletTransaction_1.mutateWallet)(env, uid, "devGrantCurrency", { kind: "client", txId }, (current) => (0, walletStore_1.nextWallet)(current, (0, wallet_1.grant)(current.balances, [{ currency, amount }]), "devGrantCurrency"), (wallet) => {
        replayed = false;
        return { wallet };
    });
    if (replayed) {
        logger.info("receipt replay", { uid, env, source: "devGrantCurrency", txId, rev: result.wallet.rev });
    }
    else {
        logger.info("devGrantCurrency", {
            uid, env, currency, amount, rev: result.wallet.rev,
            txIdSource: (0, receiptId_1.isClientReceiptId)((_j = request.data) === null || _j === void 0 ? void 0 : _j.txId) ? "client" : "server",
        });
    }
    return result;
});
//# sourceMappingURL=devGrantCurrency.js.map