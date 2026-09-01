"use strict";
/**
 * 지갑 문서(`envs/{env}/users/{uid}/wallet/current`)와 그 **영수증**
 * (`.../wallet/current/receipts/{txId}`)을 아는 유일한 파일.
 *
 * **이 파일은 functions-currency 로 미러된다**(`scripts/shared-files.js`). 그래서 두 가지를 지킨다.
 * 1. `../firebaseApp` 을 import 하지 않는다 — codebase 마다 자기 앱 인스턴스를 세우므로
 *    미러가 성립하지 않는다. `db`·`transaction`·`now` 를 전부 인자로 받는다.
 * 2. `HttpsError` 를 지지 않는다 — 순수 회귀(`scripts/`)가 `lib/` 를 직접 require 하는 관용구가 깨진다.
 *    env 검증과 거절은 호출부 callable 이 한다.
 *
 * 세이브 문서와 달리 지갑에는 룰 검증 블록이 없다(`write: if false` 라 클라 쓰기가 없다).
 * 그래서 "서버가 룰을 어긴 문서를 써서 그 계정이 영구 거부된다"는 세이브 쪽 함정이 여기엔 없고,
 * 클램프의 역할은 음수·NaN 이 화면을 깨는 것을 막는 데까지다.
 *
 * **영수증은 잔액 쓰기의 일부다.** nextWallet 이 브랜드 타입 WalletUpdate 를 내고 writeWallet 이
 * 그것만 받으므로, 잔액만 쓰고 영수증을 빠뜨리는 경로가 타입 수준에서 존재하지 않는다.
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.WALLET_SCHEMA_VERSION = void 0;
exports.nextWallet = nextWallet;
exports.walletRef = walletRef;
exports.receiptRef = receiptRef;
exports.readWallet = readWallet;
exports.readReceipt = readReceipt;
exports.writeWallet = writeWallet;
exports.createWallet = createWallet;
exports.writeReceiptOnly = writeReceiptOnly;
const currencyKeys_1 = require("./currencyKeys");
const wallet_1 = require("./wallet");
const saveValues_1 = require("../save/saveValues");
/** 지갑 문서의 스키마 축. 세이브 SCHEMA_VERSION 과 별개로 승급한다. */
exports.WALLET_SCHEMA_VERSION = 1;
/**
 * 유상분을 잔액 이하로 자른다. **이 클램프가 "무상 먼저 소진" 정책 전부다**
 * — 잔액이 줄면 유상분이 새 잔액까지 따라 깎이므로, 감소분은 무상분에서 먼저 나간 셈이 된다.
 * 0 이하인 키는 아예 뺀다(빈 맵 = 전부 무상).
 * @param {Balances} paid 유상 잔액(부분·오염 가능)
 * @param {Balances} balances 정규화된 전체 잔액
 * @return {Balances} 잘린 유상 잔액
 */
function clampPaid(paid, balances) {
    const source = paid !== null && paid !== void 0 ? paid : {};
    const next = {};
    for (const key of currencyKeys_1.CURRENCY_KEYS) {
        const value = Math.min((0, saveValues_1.intOf)(source[key]), (0, saveValues_1.intOf)(balances[key]));
        if (value > 0)
            next[key] = value;
    }
    return next;
}
/**
 * 재화별 증감. **호출부가 넘기지 않고 여기서 차분한다** — 손으로 넘기게 두면 그것이
 * 영수증이 거짓말할 수 있는 유일한 축이 된다(spend/grant 는 상·하한에서 자르므로
 * "의도한 증감"과 "실제 증감"이 다를 수 있고, 영수증은 실제를 적어야 한다).
 * @param {Balances} before 이동 전 잔액
 * @param {Balances} after 이동 후 잔액
 * @return {Balances} 4키 증감(무변화는 0)
 */
function diffBalances(before, after) {
    const changes = {};
    for (const key of currencyKeys_1.CURRENCY_KEYS) {
        changes[key] = (0, saveValues_1.intOf)(after[key]) - (0, saveValues_1.intOf)(before[key]);
    }
    return changes;
}
/**
 * 다음 지갑 상태를 만드는 **유일한 출구**. 명령이 {rev, balances, paidBalances} 를 손으로
 * 조립하기 시작하면 유상분 불변식이 명령마다 갈린다.
 *
 * rev 도 여기서 올린다 — writeWallet 은 받은 값을 그대로 싣는 직렬화기일 뿐이라,
 * 호출부가 rev+1 을 손으로 얹게 두면 빠뜨린 명령의 쓰기가 앞선 쓰기를 덮는다.
 * 세이브 revision 과 달리 "정확히 +1" 은 계약이 아니다(결제 웹훅처럼 클라가 모르는
 * 정당한 쓰기가 생긴다) — 여기서 보장하는 것은 단조 증가뿐이다.
 * @param {WalletState} current 현재 상태
 * @param {Balances} balances 반영 후 잔액
 * @param {string} source 영수증에 적을 명령 이름
 * @return {WalletUpdate} 다음 상태와 그것을 설명하는 영수증 재료
 */
function nextWallet(current, balances, source) {
    const before = (0, wallet_1.normalizeBalances)(current.balances);
    const nextBalances = (0, wallet_1.normalizeBalances)(balances);
    return {
        next: {
            rev: current.rev + 1,
            balances: nextBalances,
            paidBalances: clampPaid(current.paidBalances, nextBalances),
        },
        source,
        before,
        changes: diffBalances(before, nextBalances),
    };
}
/**
 * 지갑 문서 참조. 경로는 클라 FirebaseRootPath.User + /wallet/current 와 같아야 한다.
 * @param {Firestore} db 명명 DB 핸들
 * @param {string} env 환경 id
 * @param {string} uid 사용자 id
 * @return {DocumentReference} 지갑 문서 참조
 */
function walletRef(db, env, uid) {
    return db.doc(`envs/${env}/users/${uid}/wallet/current`);
}
/**
 * 영수증 문서 참조. 지갑 아래에 두므로 호출부는 지갑 ref 만 알면 된다.
 * @param {DocumentReference} wallet 지갑 문서 참조
 * @param {string} txId 영수증 번호
 * @return {DocumentReference} 영수증 문서 참조
 */
function receiptRef(wallet, txId) {
    return wallet.collection("receipts").doc(txId);
}
/**
 * 스냅샷에서 지갑을 읽는다. 문서가 없거나 필드가 깨져도 4키 0 · rev 0 으로 선다
 * — 판정은 호출부가 하고, 여기서 던지면 미러가 순수 계약을 잃는다.
 * @param {DocumentSnapshot} snapshot 지갑 문서 스냅샷
 * @return {WalletState} 지갑 상태
 */
function readWallet(snapshot) {
    var _a, _b;
    const data = snapshot.exists ? snapshot.data() : undefined;
    const rev = Number(data === null || data === void 0 ? void 0 : data.rev);
    const balances = (0, wallet_1.normalizeBalances)(((_a = data === null || data === void 0 ? void 0 : data.balances) !== null && _a !== void 0 ? _a : {}));
    return {
        rev: Number.isFinite(rev) && rev > 0 ? Math.trunc(rev) : 0,
        balances,
        // paidBalances 가 없는 문서는 유상 지급 이전의 지갑이라 전부 무상이다.
        paidBalances: clampPaid(((_b = data === null || data === void 0 ? void 0 : data.paidBalances) !== null && _b !== void 0 ? _b : {}), balances),
    };
}
/**
 * 영수증을 읽는다. 있으면 그 요청은 이미 처리됐고, 기록된 result 를 그대로 돌려주면 된다.
 *
 * **깨진 result 는 던진다.** 미스로 강등하면 재집행이 열려 이 장치의 목적이 통째로 무너진다
 * — 되풀이되는 실패가 조용한 이중 과금보다 낫다.
 * @param {DocumentSnapshot} snapshot 영수증 문서 스냅샷
 * @return {ReceiptLookup} 조회 결과
 */
function readReceipt(snapshot) {
    var _a, _b;
    if (!snapshot.exists)
        return { hit: false };
    const data = (_a = snapshot.data()) !== null && _a !== void 0 ? _a : {};
    const raw = data.result;
    return {
        hit: true,
        source: String((_b = data.source) !== null && _b !== void 0 ? _b : ""),
        result: raw === null || raw === undefined ? null : JSON.parse(String(raw)),
    };
}
/**
 * 지갑과 영수증을 같은 트랜잭션에 싣는다. **받은 값을 그대로 싣는 직렬화기다**
 * — rev 를 올리는 것은 nextWallet 이다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {WalletUpdate} update nextWallet 산물
 * @param {ReceiptKey} receipt 영수증 번호
 * @param {unknown} result 재시도가 그대로 돌려받을 응답(JSON 으로 싣는다)
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp()) — 호출부가 넘긴다
 * @return {WalletState} 쓰인 지갑 상태
 */
function writeWallet(transaction, ref, update, receipt, result, now) {
    const balances = (0, wallet_1.normalizeBalances)(update.next.balances);
    transaction.set(ref, {
        schemaVersion: exports.WALLET_SCHEMA_VERSION,
        rev: update.next.rev,
        balances,
        paidBalances: clampPaid(update.next.paidBalances, balances),
        updatedAt: now,
    });
    writeReceipt(transaction, ref, receipt, {
        source: update.source,
        before: update.before,
        after: balances,
        changes: update.changes,
        rev: update.next.rev,
    }, result, now);
    return update.next;
}
/**
 * 지갑을 새로 만들고 첫 영수증을 끊는다. `set` 이 아니라 `create` 라 경합하면 트랜잭션이
 * 재실행된다 — 두 초기화가 겹쳐 잔액이 두 번 이관되는 것을 막는다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {Balances} balances 최초 잔액
 * @param {string} source 개설 경로(WalletCreateSource) 또는 개설과 이동이 겹쳤을 때의 명령 이름
 * @param {ReceiptKey} receipt 영수증 번호
 * @param {unknown} result 재시도가 그대로 돌려받을 응답
 * @param {unknown} now 서버 시각
 * @return {WalletState} 만들어진 상태
 */
function createWallet(transaction, ref, balances, source, receipt, result, now) {
    // 이관으로 선 지갑은 전부 무상이다 — 유상분은 결제가 처음으로 채운다.
    const created = {
        rev: 1,
        balances: (0, wallet_1.normalizeBalances)(balances),
        paidBalances: {},
    };
    transaction.create(ref, {
        schemaVersion: exports.WALLET_SCHEMA_VERSION,
        rev: created.rev,
        balances: created.balances,
        paidBalances: created.paidBalances,
        updatedAt: now,
    });
    const before = (0, wallet_1.normalizeBalances)({});
    writeReceipt(transaction, ref, receipt, {
        source,
        before,
        after: created.balances,
        changes: diffBalances(before, created.balances),
        rev: created.rev,
    }, result, now);
    return created;
}
/**
 * 지갑을 **쓰지 않은** 트랜잭션의 영수증. 낙인만 찍고 잔액은 그대로인 경로(claimPayout 의
 * 지급 0건 ack)가 여기 온다 — 영수증이 없으면 재시도가 첫 응답과 다른 답을 내민다.
 *
 * 규칙: **재화를 움직였거나, 재화 이동을 대신하는 낙인을 썼으면 그 txId 로 영수증을 끊는다.**
 * 재화와 무관한 쓰기(ensureSaveDocument 의 계정 복구 같은)는 여기 오지 않는다 — 적으면 감사 축이 오염된다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {string} source 명령 이름
 * @param {WalletState} wallet 손대지 않은 현재 지갑
 * @param {ReceiptKey} receipt 영수증 번호
 * @param {unknown} result 재시도가 그대로 돌려받을 응답
 * @param {unknown} now 서버 시각
 * @return {void}
 */
function writeReceiptOnly(transaction, ref, source, wallet, receipt, result, now) {
    const balances = (0, wallet_1.normalizeBalances)(wallet.balances);
    writeReceipt(transaction, ref, receipt, {
        source,
        before: balances,
        after: balances,
        changes: diffBalances(balances, balances),
        rev: wallet.rev,
    }, result, now);
}
/**
 * 영수증 문서를 쓴다. 평탄하게 잡는다 — 중첩을 넣으면 나중에 웨어하우스로 내보낼 때 값을 치른다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} wallet 지갑 문서 참조
 * @param {ReceiptKey} receipt 영수증 번호
 * @param {ReceiptBody} body 영수증 본문
 * @param {unknown} result 재시도가 그대로 돌려받을 응답
 * @param {unknown} now 서버 시각
 * @return {void}
 */
function writeReceipt(transaction, wallet, receipt, body, result, now) {
    const reference = receiptRef(wallet, receipt.txId);
    const document = {
        txId: receipt.txId,
        source: body.source,
        changes: body.changes,
        before: body.before,
        after: body.after,
        rev: body.rev,
        result: result === undefined ? null : JSON.stringify(result),
        // 인앱결제가 처음으로 채운다 — 스토어 영수증 원문 자리다.
        storeReceipt: null,
        createdAt: now,
    };
    if (receipt.kind === "client")
        transaction.create(reference, document);
    else
        transaction.set(reference, document);
}
//# sourceMappingURL=walletStore.js.map