"use strict";
/**
 * 튜토리얼 무료 한 방 문서(`envs/{env}/users/{uid}/grants/current`)를 아는 유일한 파일.
 *
 * 신규 계정 지급은 Gold 뿐이라 온보딩이 시키는 카드 강화(Shard)·키워드 강화(Energy)는 낼 돈이 없다.
 * 그 한 방을 클라 정적 필드가 들고 있으면 앱 재시작으로 되살아나므로 계정당 1회를 서버가 소유한다.
 *
 * walletStore 관용구를 따른다 — `db`·`transaction`·`now` 를 전부 인자로 받고 `HttpsError` 를 모른다.
 * 세이브 문서와 별도 경로라 세이브 스키마(룰 isValidSave 의 15키)와 무관하게 승급한다.
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.GRANT_SCHEMA_VERSION = void 0;
exports.grantsRef = grantsRef;
exports.readGrants = readGrants;
exports.hasFreeShot = hasFreeShot;
exports.writeGrantUsed = writeGrantUsed;
/** 무료 한 방 문서의 스키마 축. 세이브 SCHEMA_VERSION 과 별개로 승급한다. */
exports.GRANT_SCHEMA_VERSION = 1;
/**
 * 무료 한 방 문서 참조. 세이브·지갑과 같은 유저 경로 아래 선다.
 * @param {Firestore} db 명명 DB 핸들
 * @param {string} env 환경 id
 * @param {string} uid 사용자 id
 * @return {DocumentReference} 무료 한 방 문서 참조
 */
function grantsRef(db, env, uid) {
    return db.doc(`envs/${env}/users/${uid}/grants/current`);
}
/**
 * 스냅샷에서 소진 여부를 읽는다. 문서가 없거나 필드가 깨졌으면 **미사용**으로 선다
 * — 무료 한 방을 못 읽었다고 온보딩을 막으면 낼 돈이 없는 신규 계정이 그 자리에서 멈춘다.
 * @param {DocumentSnapshot} snapshot 무료 한 방 문서 스냅샷
 * @return {TutorialGrants} 축별 소진 여부
 */
function readGrants(snapshot) {
    const data = snapshot.exists ? snapshot.data() : undefined;
    return {
        enhanceCard: data?.enhanceCard === true,
        enhanceKeyword: data?.enhanceKeyword === true,
    };
}
/**
 * 이 축의 무료 한 방이 남아 있는가.
 * @param {TutorialGrants} grants 축별 소진 여부
 * @param {GrantAxis} axis 성장 축
 * @return {boolean} 아직 안 썼으면 true
 */
function hasFreeShot(grants, axis) {
    return !grants[axis];
}
/**
 * 이 축을 소진으로 찍는다. 같은 트랜잭션에서 읽은 상태를 받아 **문서 전체**를 쓴다
 * — 다른 축의 낙인이 지워지지 않게 하려는 것이고, 두 호출이 겹치면 트랜잭션이 재실행된다.
 * @param {Transaction} transaction 진행 중인 트랜잭션
 * @param {DocumentReference} ref 무료 한 방 문서 참조
 * @param {GrantAxis} axis 소진할 축
 * @param {TutorialGrants} grants 이 트랜잭션에서 읽은 상태
 * @param {unknown} now 서버 시각(FieldValue.serverTimestamp()) — 호출부가 넘긴다
 * @return {void}
 */
function writeGrantUsed(transaction, ref, axis, grants, now) {
    const next = { ...grants, [axis]: true };
    transaction.set(ref, {
        schemaVersion: exports.GRANT_SCHEMA_VERSION,
        enhanceCard: next.enhanceCard,
        enhanceKeyword: next.enhanceKeyword,
        updatedAt: now,
    });
}
//# sourceMappingURL=tutorialGrants.js.map