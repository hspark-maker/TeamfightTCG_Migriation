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
exports.mutateWallet = mutateWallet;
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const firestore_1 = require("firebase-admin/firestore");
const firebaseApp_1 = require("../firebaseApp");
const countedTransaction_1 = require("../observability/countedTransaction");
const environments_1 = require("../save/environments");
const walletStore_1 = require("./walletStore");
/**
 * 지갑을 트랜잭션 1회로 읽고 고친다. 반환은 WalletPatch 뿐이다
 * — revision·updatedSlots 는 세이브 문서의 것이고 여기선 아무것도 오르지 않는다.
 * 같은 txId 로 다시 온 요청은 콜백에 들어가기도 전에 첫 응답을 되돌려준다(쓰기 0회).
 * 응답 조립을 finalize 콜백으로 받는 것이 그 때문이다 — 트랜잭션 밖에서 조립하면
 * 캐시할 응답이 아직 없어서 영수증에 실을 것이 없다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {string} source 명령 이름. 재시도 판정의 대조축이다
 * @param {ReceiptKey} receipt 영수증 번호(요청 txId 또는 서버 발급)
 * @param {Function} mutate 현재 지갑을 받아 다음 지갑(nextWallet 산물)을 돌려준다
 * @param {Function} finalize 갱신된 지갑에 명령별 필드를 얹어 최종 응답을 만든다. 트랜잭션 안에서 돌다
 * @param {WalletGuard} guard 지급 자격 문서. 넘기면 같은 트랜잭션에서 검사·낙인한다
 * @return {Promise<TResponse>} finalize 가 만든 응답
 */
async function mutateWallet(env, uid, source, receipt, mutate, finalize, guard) {
    if (!(0, environments_1.isKnownEnv)(env)) {
        throw new https_1.HttpsError("invalid-argument", `Unknown env: ${env}`);
    }
    const reference = (0, walletStore_1.walletRef)(firebaseApp_1.db, env, uid);
    return (0, countedTransaction_1.withCountedTransaction)(source, async (transaction) => {
        const snapshot = await transaction.get(reference);
        if (!snapshot.exists) {
            // 도메인 거절(permission-denied)이 아니라 세션 문제다 — 초기화의 ensureWallet 이
            // 돌지 않았다는 뜻이라 클라가 다시 초기화하는 것이 옳은 조치다. rejectDomain 으로
            // 감싸면 클라가 "잔액이 모자란다" 류의 도메인 사유로 오해하고 초기화를 다시 걸지 않는다.
            throw new https_1.HttpsError("failed-precondition", "Wallet document does not exist. Boot must call ensureWallet first.");
        }
        // 영수증 조회. 히트면 쓰기를 하나도 하지 않고 첫 응답을 그대로 돌려준다(자격 검사도 건너뛴다).
        const lookup = (0, walletStore_1.readReceipt)(await transaction.get((0, walletStore_1.receiptRef)(reference, receipt.txId)));
        if (lookup.hit) {
            if (lookup.source !== source) {
                // 같은 txId 를 다른 명령이 재사용했다. 첫 명령의 응답을 돌려주면 클라가 엉뚱한
                // 결과를 채택하므로 집행하지 않고 거절한다. 도메인 거절이라 permission-denied 다
                // (save/domainReject 와 같은 계약) — 다른 코드로 나가면 클라가 세션을 끊는다.
                logger.warn("domain rejected", {
                    reason: "TxIdReused", uid, env, source,
                    receiptSource: lookup.source, txId: receipt.txId,
                });
                throw new https_1.HttpsError("permission-denied", `TxIdReused: txId '${receipt.txId}' was already used by another command.`, { reason: "TxIdReused" });
            }
            return lookup.result;
        }
        // 자격 검사는 영수증 히트 **뒤**다. 히트한 재시도는 첫 호출이 이미 낙인했으므로
        // 여기서 다시 보면 자기 낙인에 걸려 already-claimed 로 떨어진다.
        // 읽기는 전부 쓰기 앞에 모여 있어야 한다(Firestore 트랜잭션 제약) — 그래서 mutate 앞이다.
        const guardSnapshot = guard === undefined ? null : await transaction.get(guard.ref);
        if (guard !== undefined && guardSnapshot !== null)
            guard.verify(guardSnapshot);
        // 트랜잭션을 콜백에 넘기지 않는다 — 넘기면 walletStore 밖에서 쓰는 콜백이 생겨
        // 브랜드 타입 강제가 뚫린다.
        const update = mutate((0, walletStore_1.readWallet)(snapshot));
        // 응답은 쓰기 전에 짓는다 — 그것 그대로가 영수증에 담겨야 재시도가 같은 답을 받는다.
        const response = finalize({ rev: update.next.rev, balances: update.next.balances });
        (0, walletStore_1.writeWallet)(transaction, reference, update, receipt, response, firestore_1.FieldValue.serverTimestamp());
        // 낙인은 지갑과 같은 커밋이다 — 갈라 놓으면 그 틈이 곧 이중 지급 창이다.
        if (guard !== undefined)
            transaction.set(guard.ref, guard.stamp, { merge: true });
        return response;
    });
}
//# sourceMappingURL=walletTransaction.js.map