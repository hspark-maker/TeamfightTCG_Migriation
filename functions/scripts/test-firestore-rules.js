const fs = require("node:fs");
const assert = require("node:assert/strict");
const {initializeTestEnvironment, assertFails, assertSucceeds} = require("@firebase/rules-unit-testing");
const {doc, getDoc, serverTimestamp, setDoc} = require("firebase/firestore");

function save(revision = 1, rankPoints = 0) {
  return {
    schemaVersion: 7, revision, updatedAt: serverTimestamp(), deviceId: "a".repeat(32), appVersion: "test",
    currency: {balances: {Gold: 100, Diamond: 0, Energy: 0, Shard: 0}},
    ownership: {}, deck: {}, cardGrowth: {}, keywordGrowth: {},
    rank: {points: rankPoints, claimedTiers: []}, albumReward: {}, tournament: {}, tutorial: {}, profile: {},
  };
}

(async () => {
  const testEnv = await initializeTestEnvironment({
    projectId: "bm-cardbattle",
    firestore: {rules: fs.readFileSync("firestore.rules", "utf8")},
  });
  try {
    const alice = testEnv.authenticatedContext("alice").firestore("cardbattle");
    const bob = testEnv.authenticatedContext("bob").firestore("cardbattle");
    const anon = testEnv.unauthenticatedContext().firestore("cardbattle");
    const saveRef = doc(alice, "envs/live/users/alice/save/current");
    const matchRef = doc(alice, "envs/live/matches/m1");

    // 세이브 — 본인만, revision 은 정확히 +1, 스키마 전수 검증
    await assertSucceeds(setDoc(saveRef, save()));
    await assertSucceeds(setDoc(saveRef, save(2, 40)));
    await assertFails(setDoc(saveRef, save(2, 40)));                    // revision 되감기·정체
    await assertFails(setDoc(saveRef, save(4, 40)));                    // revision 건너뛰기
    await assertFails(setDoc(saveRef, {...save(3, 40), extra: true}));  // 모르는 필드
    await assertFails(setDoc(doc(bob, "envs/live/users/alice/save/current"), save(3)));
    await assertFails(setDoc(doc(alice, "envs/live/users/alice/save/other"), save()));
    await assertFails(setDoc(doc(alice, "envs/dev/users/alice/save/current"), save()));
    await assertFails(getDoc(doc(bob, "envs/live/users/alice/save/current")));
    await assertFails(getDoc(doc(anon, "envs/live/users/alice/save/current")));
    assert.equal((await getDoc(saveRef)).data().rank.points, 40);

    // 매치 문서 — 서버(Admin SDK) 전용. 클라이언트는 읽기도 쓰기도 못 한다.
    await assertFails(setDoc(matchRef, {status: "confirmed"}));
    await assertFails(getDoc(matchRef));
    await assertFails(setDoc(doc(alice, "envs/live/matches/m1/sub/x"), {a: 1}));

    // 스펙 — 로그인한 클라는 읽기만, 쓰기는 admin 클레임만
    await assertSucceeds(getDoc(doc(alice, "envs/live/specs/Card")));
    await assertFails(getDoc(doc(anon, "envs/live/specs/Card")));
    await assertFails(setDoc(doc(alice, "envs/live/specs/Card"), {revision: 1}));
    const admin = testEnv.authenticatedContext("root", {admin: true}).firestore("cardbattle");
    await assertSucceeds(setDoc(doc(admin, "envs/live/specs/Card"), {revision: 1}));
    await assertSucceeds(setDoc(doc(admin, "envs/live/specs/Card/rows/1"), {a: 1}));

    // 미지정 경로
    await assertFails(getDoc(doc(alice, "randomStuff/x")));
    await assertFails(setDoc(doc(alice, "randomStuff/x"), {a: 1}));

    console.log("firestore-rules tests: pass");
  } finally {
    await testEnv.cleanup();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
