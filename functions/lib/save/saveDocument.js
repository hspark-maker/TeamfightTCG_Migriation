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
exports.SCHEMA_VERSION = exports.isKnownEnv = void 0;
exports.saveDocument = saveDocument;
exports.requireUid = requireUid;
exports.assertWritableSchema = assertWritableSchema;
exports.mutateSave = mutateSave;
exports.ensureSaveDocument = ensureSaveDocument;
const https_1 = require("firebase-functions/v2/https");
const logger = __importStar(require("firebase-functions/logger"));
const firestore_1 = require("firebase-admin/firestore");
const firebaseApp_1 = require("../firebaseApp");
const countedTransaction_1 = require("../observability/countedTransaction");
const environments_1 = require("./environments");
Object.defineProperty(exports, "isKnownEnv", { enumerable: true, get: function () { return environments_1.isKnownEnv; } });
const domainReject_1 = require("./domainReject");
const receiptCache_1 = require("./receiptCache");
const wallet_1 = require("../currency/wallet");
const walletStore_1 = require("../currency/walletStore");
/**
 * 서버가 쓰는 세이브 문서 스키마 버전. 클라 쪽 쌍둥이 상수와 짝이다.
 *   Assets/Scripts/OutGame/Save/2.Domain/UserSaveData.cs
 *   -> UserSaveData.VERSION
 * TS와 C#이 상수를 공유할 방법이 없으니, 이 줄을 고치는 커밋은 반드시 저
 * 파일도 같이 고친다. 정책: 필드·슬롯 추가는 버전을 올리지 않고 흡수하고,
 * 파괴적 변경일 때만 서버·클라를 동시에 올린 뒤 기존 문서를 삭제·재생성한다.
 *
 * v8 = 재화가 currency 슬롯을 떠나 wallet 문서로 옮겨간 판.
 */
exports.SCHEMA_VERSION = 8;
/**
 * 세이브 문서 참조. 클라 PlayerSaveFirestorePaths 와 같은 경로여야 한다.
 * @param {string} env 환경 id (live/test)
 * @param {string} uid 유저 uid
 * @return {FirebaseFirestore.DocumentReference} 문서 참조
 */
function saveDocument(env, uid) {
    if (!environments_1.ENVIRONMENTS.includes(env)) {
        throw new https_1.HttpsError("invalid-argument", `Unknown env: ${env}`);
    }
    return firebaseApp_1.db
        .collection("envs").doc(env)
        .collection("users").doc(uid)
        .collection("save").doc("current");
}
/**
 * 호출자의 uid를 꺼낸다.
 * @param {{uid: string} | undefined} auth callable 인증 정보
 * @return {string} uid
 */
function requireUid(auth) {
    if (!auth?.uid) {
        throw new https_1.HttpsError("unauthenticated", "Sign-in is required.");
    }
    return auth.uid;
}
/**
 * 문서 스키마 버전이 이 서버가 쓸 수 있는 값인지 판정한다. **정확히 SCHEMA_VERSION** 만
 * 통과시키고, 벗어날 때 낮음/높음을 다른 오류 코드로 가른다 — 원인도 조치도 다르기 때문이다.
 * 클라 PlayerSaveCloud 의 초기화 게이트가 remote>client / remote<client 를
 * 가르는 것과 같은 축이다.
 *
 * 낡은 문서에 승급 창을 열어 두지 않는 이유: 지갑을 모르는 클라는 v8 서버와 원리상 공존할 수
 * 없다. 잔액을 바꾸는 명령이 하나라도 성공하면 그 클라는 wallet 응답을 못 읽어 그 자리에서
 * 잔액이 갈리고, 뒤이은 업로드가 낮은 schemaVersion 을 실어 룰에 영구 거부된다.
 * 구 클라는 상태가 갈라지기 **전에** 멈추는 것이 옳다. 승급을 실제로 수행하는
 * commands/ensureWallet 만 자기 판정(assertMigratableSchema)으로 v7 을 받는다.
 *
 * export 인 것은 순수 회귀(scripts/test-schema-window.js)가 이 판정을 못박기 때문이다.
 * @param {unknown} rawVersion 문서에 적힌 schemaVersion 원본 값
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 */
function assertWritableSchema(rawVersion, env, uid) {
    const documentVersion = typeof rawVersion === "number" ? rawVersion : Number.NaN;
    if (documentVersion === exports.SCHEMA_VERSION) {
        return;
    }
    // 로그와 에러 메시지 양쪽에 기대값·실제값을 모두 남긴다 — 드리프트는
    // 전 callable을 한꺼번에 죽이므로 "왜"가 남지 않으면 원인 추적이 막힌다.
    const drift = {
        uid,
        env,
        serverSchemaVersion: exports.SCHEMA_VERSION,
        documentSchemaVersion: rawVersion ?? null,
    };
    const seen = `document v${String(rawVersion)} vs server v${exports.SCHEMA_VERSION}`;
    if (!Number.isFinite(documentVersion)) {
        logger.error("save schema unreadable", drift);
        throw new https_1.HttpsError("failed-precondition", `Save schema is unreadable (${seen}): the document's schemaVersion ` +
            "is missing or is not a number.", drift);
    }
    if (documentVersion > exports.SCHEMA_VERSION) {
        logger.error("save schema drift: server is behind the document", drift);
        throw new https_1.HttpsError("out-of-range", `Save schema drift (${seen}): the document is newer than this ` +
            "server. Deploy functions built from the same commit as the client " +
            "(UserSaveData.VERSION); retrying will not help.", drift);
    }
    logger.error("save schema drift: document is stale", drift);
    throw new https_1.HttpsError("failed-precondition", `Save schema drift (${seen}): the document is older than the schema this ` +
        `server writes (v${exports.SCHEMA_VERSION}). It must be migrated (ensureWallet) ` +
        "or deleted and recreated before it is writable.", drift);
}
/**
 * 세이브 문서를 트랜잭션 1회로 읽고 고친다. revision +1 과 updatedAt 은
 * 여기서만 움직인다 — callable 하나당 세이브 revision 정확히 +1 이라는 계약의 집행 지점.
 * (문서 쓰기 자체는 세이브 1회 + 영수증 1회이고, 지갑이 움직이면 거기에 지갑 1회가 더 붙는다.)
 *
 * 지갑 문서도 **항상 함께 읽어** 콜백에 넘기고 응답에 싣는다(옵션 플래그를 두지 않는다).
 * 바뀌지 않은 지갑을 매번 내보내는 것은 클라 채택이 단조·멱등이라 무해하고,
 * 클라가 어떤 이유로든 드리프트했을 때 다음 명령이 스스로 맞춰 준다.
 * 같은 txId 로 다시 온 요청은 **콜백에 들어가기도 전에** 첫 응답을 되돌려준다(쓰기 0회).
 * 응답 조립을 finalize 콜백으로 받는 것이 그 때문이다 — 트랜잭션 밖에서 조립하면
 * 캐시할 응답이 아직 없어서 영수증에 실을 것이 없다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {string} source 명령 이름. 영수증에 그대로 실리고 재시도 판정의 대조축이다
 * @param {ReceiptKey} receipt 영수증 번호(요청 txId 또는 서버 발급)
 * @param {Function} mutate 현재 문서·트랜잭션·지갑을 받아 갱신할 슬롯 전체 값을 돌려준다
 * @param {Function} finalize 채택 계약에 명령별 필드를 얹어 최종 응답을 만든다. 트랜잭션 안에서 돈다
 * @return {Promise<TResponse>} finalize 가 만든 응답
 */
async function mutateSave(env, uid, source, receipt, mutate, finalize) {
    const reference = saveDocument(env, uid);
    const walletReference = (0, walletStore_1.walletRef)(firebaseApp_1.db, env, uid);
    return (0, countedTransaction_1.withCountedTransaction)(source, async (transaction) => {
        const snapshot = await transaction.get(reference);
        if (!snapshot.exists) {
            throw new https_1.HttpsError("failed-precondition", "Save document does not exist.");
        }
        const current = snapshot.data() ?? {};
        assertWritableSchema(current.schemaVersion, env, uid);
        // 지갑 읽기는 콜백 진입 **전에** 끝낸다 — Firestore 트랜잭션은 모든 읽기가 모든 쓰기보다
        // 앞서야 하는데, openPack 처럼 재실행되는 명령 안에서 읽으면 그 순서가 깨진다.
        const walletSnapshot = await transaction.get(walletReference);
        const wallet = (0, walletStore_1.readWallet)(walletSnapshot);
        // 여기서 승급하지 않는다 — 위 판정을 통과한 문서는 이미 v8 이고, v7 이관은
        // 그것만을 위해 있는 commands/ensureWallet 의 일이다.
        //
        // 다만 지갑 부재는 메운다: v8 문서는 currency 슬롯이 없으므로 지갑이 사라진 계정은
        // 잔액을 주장하는 곳이 어디에도 없다. 잔액 0 으로 세우는 것이 그 상태의 정답이고
        // 잃는 것이 없다. 안 세우면 지갑을 쓰는 명령이 전부 실패해 계정이 굳는다.
        const creatingWallet = !walletSnapshot.exists;
        // 영수증 조회가 **마지막 무조건 읽기**다 — 콜백이 자기 문서를 더 읽을 수 있으므로
        // (enhanceCard 의 grants) 여기보다 뒤로 밀 수 없고, 쓰기는 아직 하나도 없다.
        const lookup = (0, walletStore_1.readReceipt)(await transaction.get((0, walletStore_1.receiptRef)(walletReference, receipt.txId)));
        if (lookup.hit) {
            if (lookup.source !== source) {
                // 같은 txId 를 다른 명령이 재사용했다. 첫 명령의 응답을 다른 명령에 돌려주면
                // 클라가 엉뚱한 결과를 채택하므로, 집행하지 않고 거절한다.
                (0, domainReject_1.rejectDomain)("TxIdReused", `txId '${receipt.txId}' was already used by another command.`, { uid, env, source, receiptSource: lookup.source, txId: receipt.txId });
            }
            try {
                // 문서 revision 을 넘겨 코히런스를 검사한다 — 캐시본은 첫 시도의 revision·지갑을,
                // updatedSlots 는 지금 문서를 실으므로 둘이 어긋나면 섞인 상태가 나간다.
                return (0, receiptCache_1.replayCached)(lookup.result, current, Number(current.revision ?? 0));
            }
            catch (error) {
                // 도메인 거절이 아니라 permission-denied 를 쓰지 않는다 — 이 응답으로 로컬 상태를
                // 맞출 방법이 없으니 클라가 다시 초기화하는 것이 옳다(RemoteAhead 와 같은 축).
                logger.error("receipt replay is unusable", {
                    uid, env, source, txId: receipt.txId, error: String(error),
                });
                throw new https_1.HttpsError("failed-precondition", `Receipt replay is unusable: ${String(error)}`);
            }
        }
        const revision = Number(current.revision ?? 0) + 1;
        const outcome = await mutate(current, transaction, wallet);
        // 응답에 실을 지갑은 **쓰기 전에** 확정한다 — finalize 가 만든 응답 그대로가 영수증에
        // 담겨야 재시도가 같은 답을 받는데, 그 답은 갱신된 잔액을 실어야 하기 때문이다.
        // 개설 갈래의 rev 1 은 createWallet 이 세우는 값과 같은 축이다.
        const credited = {
            rev: creatingWallet ? 1 : outcome.wallet?.next.rev ?? wallet.rev,
            balances: (0, wallet_1.normalizeBalances)(outcome.wallet?.next.balances ?? wallet.balances),
        };
        const response = finalize({
            revision,
            updatedSlots: outcome.slots,
            wallet: credited,
        });
        transaction.update(reference, {
            ...outcome.slots,
            revision,
            updatedAt: firestore_1.FieldValue.serverTimestamp(),
        });
        // 영수증에 실리는 것은 응답 그대로가 아니라 슬롯 값을 뷘 캐시본이다(근거는 receiptCache).
        const cached = (0, receiptCache_1.cacheableResponse)(response, outcome.slots);
        if (creatingWallet) {
            // set 이 아니라 create 다 — 이 트랜잭션 밖에서 ensureWallet 이 먼저 지갑을 세웠으면
            // 재실행되어 그쪽 이관 잔액을 0 으로 덮어쓰는 것을 막는다.
            // 지갑이 없어 개설과 이동이 한 트랜잭션에 겹쳤다 — 영수증은 돈을 움직인 명령을 적는다.
            // 개설 사실은 rev 1 · before 4키 0 으로 읽힌다.
            (0, walletStore_1.createWallet)(transaction, walletReference, outcome.wallet?.next.balances ?? wallet.balances, source, receipt, cached, firestore_1.FieldValue.serverTimestamp());
        }
        else if (outcome.wallet !== undefined) {
            (0, walletStore_1.writeWallet)(transaction, walletReference, outcome.wallet, receipt, cached, firestore_1.FieldValue.serverTimestamp());
        }
        else {
            // 세이브만 쓴 갈래도 영수증을 끊는다 — 그 세이브 쓰기가 재화 이동을 대신하기 때문이다.
            // 재시도 판정이 이 source 를 대조한다 — 명령마다 달라야 한다.
            (0, walletStore_1.writeReceiptOnly)(transaction, walletReference, source, wallet, receipt, cached, firestore_1.FieldValue.serverTimestamp());
        }
        return response;
    });
}
/**
 * 클라 PlayerSaveDocument.TryReadMeta 와 같은 판정이다 — 여기서 통과시킨 문서만 클라가 초기화할 수 있다.
 * @param {FirebaseFirestore.DocumentData | undefined} data 문서 본문
 * @return {boolean} 메타가 온전한가
 */
function hasUsableMeta(data) {
    if (data == null)
        return false;
    const schemaVersion = data.schemaVersion;
    const revision = data.revision;
    return Number.isInteger(schemaVersion) && schemaVersion > 0 &&
        Number.isInteger(revision) && revision >= 0;
}
/**
 * 세이브 문서를 확보한다 — 없으면 만들고, 있으면 그대로 둔 채 현재 revision 만 돌려준다.
 *
 * mutateSave 와 갈라 두는 이유: 저쪽은 "callable 1회 = 문서 쓰기 1회, revision +1" 이 계약이고
 * R5~R8 이 전부 그 위에 선다. 생성 분기를 섞으면 그 불변식이 흐려진다.
 *
 * 이미 있는 문서에는 스키마 검사를 하지 않는다 — 드리프트는 클라 초기화가 다시 읽으며
 * MarkUpdateRequired / Fail 로 훨씬 나은 표면을 만든다. 여기서 던지면 그 갈래를 못 밟는다.
 *
 * 지갑도 **같은 트랜잭션**에서 만든다. 두 문서가 갈라지면 세이브만 있는 계정이 생기고,
 * 그 계정은 초기화의 ensureWallet 이 0 잔액 지갑을 세워 스타터 골드를 영영 잃는다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {string} deviceId 클라 기기 id (32자 hex)
 * @param {string} appVersion 클라 앱 버전
 * @param {Function} buildSlots 새 문서에 실을 슬롯 9개
 * @param {Balances} starterBalances 같은 트랜잭션에서 세울 지갑의 최초 잔액
 * @return {Promise<EnsureAccountOutcome>} 새 revision 과 생성 여부
 */
async function ensureSaveDocument(env, uid, deviceId, appVersion, buildSlots, starterBalances) {
    const reference = saveDocument(env, uid);
    const walletReference = (0, walletStore_1.walletRef)(firebaseApp_1.db, env, uid);
    return (0, countedTransaction_1.withCountedTransaction)("ensureSaveDocument", async (transaction) => {
        const snapshot = await transaction.get(reference);
        const data = snapshot.exists ? snapshot.data() : undefined;
        if (snapshot.exists && hasUsableMeta(data)) {
            return {
                revision: Number(data?.revision ?? 0),
                created: false,
                walletCreated: false,
                repaired: false,
                discardedFields: [],
            };
        }
        // 지갑 존재를 먼저 **묻는다**. 세이브만 지워지고 지갑이 남은 계정에서 createWallet 의 create 가
        // ALREADY_EXISTS 로 터지면 그 계정은 세이브를 영영 다시 만들지 못한다.
        // 읽기는 전부 쓰기보다 앞서야 하므로 이 자리가 마지막 읽기다.
        const walletSnapshot = await transaction.get(walletReference);
        const fresh = {
            ...buildSlots(),
            schemaVersion: exports.SCHEMA_VERSION,
            revision: 1,
            updatedAt: firestore_1.FieldValue.serverTimestamp(),
            deviceId,
            appVersion,
        };
        const walletCreated = !walletSnapshot.exists;
        if (walletCreated) {
            (0, walletStore_1.createWallet)(transaction, walletReference, starterBalances, "walletCreate:freshAccount", { kind: "boot", txId: "walletCreate:freshAccount" }, undefined, firestore_1.FieldValue.serverTimestamp());
        }
        // 메타가 없거나 깨진 문서는 클라가 초기화조차 못 하는데(TryReadMeta 실패 → Fail),
        // 그대로 두면 여기서도 noop 이라 계정이 영구 잠긴다 — 룰에 delete 경로도 없다.
        // 스키마 밖 문서는 유효한 세이브였던 적이 없으므로 버리고 새로 만드는 것이 유일한 복구다.
        if (snapshot.exists) {
            const discardedFields = Object.keys(data ?? {}).sort();
            transaction.set(reference, fresh);
            return { revision: 1, created: true, walletCreated, repaired: true, discardedFields };
        }
        // set 이 아니라 create 다 — 트랜잭션 밖에서 누가 먼저 만들었으면 재실행되어 덮어쓰기가 막힌다.
        transaction.create(reference, fresh);
        return { revision: 1, created: true, walletCreated, repaired: false, discardedFields: [] };
    });
}
//# sourceMappingURL=saveDocument.js.map