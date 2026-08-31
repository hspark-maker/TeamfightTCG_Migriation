"use strict";
/**
 * 지갑 문서(`envs/{env}/users/{uid}/wallet/current`)를 아는 유일한 파일.
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
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.WALLET_SCHEMA_VERSION = void 0;
exports.nextWallet = nextWallet;
exports.walletRef = walletRef;
exports.readWallet = readWallet;
exports.writeWallet = writeWallet;
exports.createWallet = createWallet;
exports.ledgerEntry = ledgerEntry;
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
    const source = paid ?? {};
    const next = {};
    for (const key of currencyKeys_1.CURRENCY_KEYS) {
        const value = Math.min((0, saveValues_1.intOf)(source[key]), (0, saveValues_1.intOf)(balances[key]));
        if (value > 0)
            next[key] = value;
    }
    return next;
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
 * @return {WalletState} 다음 상태
 */
function nextWallet(current, balances) {
    const nextBalances = (0, wallet_1.normalizeBalances)(balances);
    return {
        rev: current.rev + 1,
        balances: nextBalances,
        paidBalances: clampPaid(current.paidBalances, nextBalances),
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
 * 스냅샷에서 지갑을 읽는다. 문서가 없거나 필드가 깨져도 4키 0 · rev 0 으로 선다
 * — 판정은 호출부가 하고, 여기서 던지면 미러가 순수 계약을 잃는다.
 * @param {DocumentSnapshot} snapshot 지갑 문서 스냅샷
 * @return {WalletState} 지갑 상태
 */
function readWallet(snapshot) {
    const data = snapshot.exists ? snapshot.data() : undefined;
    const rev = Number(data?.rev);
    const balances = (0, wallet_1.normalizeBalances)((data?.balances ?? {}));
    return {
        rev: Number.isFinite(rev) && rev > 0 ? Math.trunc(rev) : 0,
        balances,
        // paidBalances 가 없는 문서는 유상 지급 이전의 지갑이라 전부 무상이다.
        paidBalances: clampPaid((data?.paidBalances ?? {}), balances),
    };
}
/**
 * 지갑을 쓴다. **받은 값을 그대로 싣는 직렬화기다** — rev 를 올리는 것은 nextWallet 이다.
 * 여기서 또 올리면 두 번 오르고, 여기서만 올리면 호출부가 상태를 손으로 조립하게 된다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {WalletState} next 쓸 상태(rev 는 갱신 후 값)
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp()) — 호출부가 넘긴다
 * @return {void}
 */
function writeWallet(transaction, ref, next, now) {
    const balances = (0, wallet_1.normalizeBalances)(next.balances);
    transaction.set(ref, {
        schemaVersion: exports.WALLET_SCHEMA_VERSION,
        rev: next.rev,
        balances,
        paidBalances: clampPaid(next.paidBalances, balances),
        updatedAt: now,
    });
}
/**
 * 지갑을 새로 만든다. `set` 이 아니라 `create` 라 경합하면 트랜잭션이 재실행된다
 * — 두 초기화가 겹쳐 잔액이 두 번 이관되는 것을 막는다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 지갑 문서 참조
 * @param {Balances} balances 최초 잔액
 * @param {unknown} now 서버 시각
 * @return {WalletState} 만들어진 상태
 */
function createWallet(transaction, ref, balances, now) {
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
    return created;
}
/**
 * 원장 한 줄. **아직 호출자가 없다** — 인앱결제 착수 때 붙는 자리다.
 * txId 는 호출부가 정한다(결제는 스토어 주문 id, 도메인은 `{command}:{seed}`) — 그것이 멱등 키다.
 * @param {string} source 명령 이름
 * @param {CurrencyGain[]} changes 부호 있는 증감 목록
 * @param {Balances} after 반영 후 잔액
 * @param {number} rev 이 기록 직후의 지갑 rev
 * @param {unknown} now 서버 시각
 * @return {object} 원장 문서 값
 */
function ledgerEntry(source, changes, after, rev, now) {
    const delta = {};
    for (const change of changes) {
        delta[change.currency] = (delta[change.currency] ?? 0) + change.amount;
    }
    return {
        source,
        changes: delta,
        after: (0, wallet_1.normalizeBalances)(after),
        rev,
        createdAt: now,
        receipt: null,
    };
}
//# sourceMappingURL=walletStore.js.map