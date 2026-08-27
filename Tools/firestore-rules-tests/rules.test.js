// firestore.rules.prod 회귀 테스트.
//
// 룰을 에뮬레이터에 명시 주입하므로 루트 firebase.json 이 어떤 룰을 가리키든 무관하다.
// 포트도 8081 로 갈라 놓아, 루트 설정(8080)으로 띄운 다른 에뮬레이터와 부딪히지 않는다.
import { test, before, beforeEach, after } from 'node:test';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  initializeTestEnvironment,
  assertFails,
  assertSucceeds,
} from '@firebase/rules-unit-testing';
import { doc, setDoc, getDoc, deleteDoc, Timestamp } from 'firebase/firestore';
import { saveDocument, freshAccountDocument, SCHEMA_VERSION } from './fixtures/saveDocument.js';

const RULES_PATH = process.env.RULES_FILE ?? fileURLToPath(new URL('../../firestore.rules', import.meta.url));
const PROJECT_ID = 'tcg-rules-test';
const UID = 'player-a';
const OTHER_UID = 'player-b';

let testEnv;

const savePath = (_uid = UID, _env = 'test', _docId = 'current') =>
  `envs/${_env}/users/${_uid}/save/${_docId}`;

const authed = (_uid = UID) => testEnv.authenticatedContext(_uid).firestore();
const unauthed = () => testEnv.unauthenticatedContext().firestore();

async function seed(_revision = 1, _overrides = {}, _uid = UID) {
  await testEnv.withSecurityRulesDisabled(async (_ctx) => {
    await setDoc(doc(_ctx.firestore(), savePath(_uid)), saveDocument(_revision, _overrides));
  });
}

before(async () => {
  const [t_host, t_port] = (process.env.FIRESTORE_EMULATOR_HOST ?? '127.0.0.1:8081').split(':');
  testEnv = await initializeTestEnvironment({
    projectId: PROJECT_ID,
    firestore: { rules: readFileSync(RULES_PATH, 'utf8'), host: t_host, port: Number(t_port) },
  });
});

beforeEach(async () => {
  await testEnv.clearFirestore();
});

after(async () => {
  await testEnv?.cleanup();
});

// --- 1·2. 실제 클라가 보내는 문서가 통과하는가 (핵심 회귀) -------------------

test('1. 실제 15키 문서로 create (revision 1)', async () => {
  await assertSucceeds(setDoc(doc(authed(), savePath()), saveDocument(1)));
});

test('2. 이어서 update (revision 2)', async () => {
  await seed(1);
  await assertSucceeds(setDoc(doc(authed(), savePath()), saveDocument(2)));
});

test('2b. 본인 문서 읽기', async () => {
  await seed(1);
  await assertSucceeds(getDoc(doc(authed(), savePath())));
});

// --- 3·4. 소유권 ------------------------------------------------------------

test('3. 남의 uid 문서 읽기는 거부', async () => {
  await seed(1, {}, OTHER_UID);
  await assertFails(getDoc(doc(authed(UID), savePath(OTHER_UID))));
});

test('3b. 남의 uid 문서 쓰기는 거부', async () => {
  await assertFails(setDoc(doc(authed(UID), savePath(OTHER_UID)), saveDocument(1)));
});

test('4. 미인증 읽기·쓰기는 거부', async () => {
  await seed(1);
  await assertFails(getDoc(doc(unauthed(), savePath())));
  await assertFails(setDoc(doc(unauthed(), savePath()), saveDocument(2)));
});

// --- 5·6. revision 단조 -----------------------------------------------------

test('5. revision 건너뛰기(n+2)는 거부', async () => {
  await seed(1);
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(3)));
});

test('5b. revision 감소·정체는 거부', async () => {
  await seed(2);
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(1)));
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(2)));
});

test('6. create 시 revision 이 1이 아니면 거부', async () => {
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(2)));
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(0)));
});

// --- 7·8. 필드 목록 / 삭제 --------------------------------------------------

test('7. 알 수 없는 top-level 필드는 거부', async () => {
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(1, { serverSecret: 'x' })),
  );
});

test('7b. 메타 필드 누락은 거부', async () => {
  const t_doc = saveDocument(1);
  delete t_doc.deviceId;
  await assertFails(setDoc(doc(authed(), savePath()), t_doc));
});

// 감사에서 나온 구멍 — 슬롯이 optional 이면 필드 생략으로 세이브를 비울 수 있다.
test('7c. 슬롯 누락 update 는 거부 (세이브 비우기 우회)', async () => {
  await seed(1);
  const t_meta = saveDocument(2);
  for (const t_slot of ['currency', 'ownership', 'deck', 'cardGrowth', 'keywordGrowth',
    'rank', 'albumReward', 'tournament', 'tutorial', 'profile']) {
    delete t_meta[t_slot];
  }
  await assertFails(setDoc(doc(authed(), savePath()), t_meta));
});

test('7d. 슬롯 하나만 빠져도 거부', async () => {
  await seed(1);
  const t_doc = saveDocument(2);
  delete t_doc.ownership;
  await assertFails(setDoc(doc(authed(), savePath()), t_doc));
});

test('8. delete 는 거부', async () => {
  await seed(1);
  await assertFails(deleteDoc(doc(authed(), savePath())));
});

// --- 9. 경로 화이트리스트 ---------------------------------------------------

// 감사에서 나온 구멍 — create 에 상한이 없으면 큰 값이 영구 고착된다(update 는 >= 라 못 내린다).
test('8b. create 시 schemaVersion 이 현재 버전이 아니면 거부', async () => {
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(1, { schemaVersion: 999999 })),
  );
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(1, { schemaVersion: 1 })),
  );
});

test('9. 알 수 없는 envId 는 거부', async () => {
  await assertFails(
    setDoc(doc(authed(), savePath(UID, 'dev')), saveDocument(1)),
  );
});

test('9b. 알 수 없는 docId 는 거부', async () => {
  await assertFails(
    setDoc(doc(authed(), savePath(UID, 'test', 'backup')), saveDocument(1)),
  );
});

test('9c. live 환경은 통과', async () => {
  await assertSucceeds(
    setDoc(doc(authed(), savePath(UID, 'live')), saveDocument(1)),
  );
});

// --- 10~13. 메타 형식 -------------------------------------------------------

test('10. updatedAt 을 클라 시각으로 넣으면 거부', async () => {
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(1, { updatedAt: Timestamp.fromDate(new Date()) })),
  );
});

test('11. deviceId 길이가 32가 아니면 거부', async () => {
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(1, { deviceId: '0123456789abcdef0123456789abcde' })),
  );
});

test('11b. appVersion 이 64자를 넘으면 거부', async () => {
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(1, { appVersion: 'v'.repeat(65) })),
  );
});

test('12. schemaVersion 하향은 거부', async () => {
  await seed(1);
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(2, { schemaVersion: SCHEMA_VERSION - 1 })),
  );
});

test('13. 슬롯 타입 위반은 거부', async () => {
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(1, { currency: 'x' })));
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(1, { ownership: [1, 2] })));
});

// 클라는 CurrencySaveData.Normalize 덕에 언제나 4재화를 싣는다. 그래서 룰이 4키를
// 전부 요구하는 게 맞다. 키를 빼거나 타입을 바꾸는 조작은 거부돼야 한다.
test('13b. 재화 4키 계약 — 키 누락·타입 변조·미지 재화는 거부', async () => {
  await assertFails(setDoc(doc(authed(), savePath()),
    saveDocument(1, { currency: { balances: { Gold: 100 } } })));
  await assertFails(setDoc(doc(authed(), savePath()),
    saveDocument(1, { currency: { balances: { Gold: '100', Diamond: 0, Energy: 0, Shard: 0 } } })));
  await assertFails(setDoc(doc(authed(), savePath()),
    saveDocument(1, { currency: { balances: { Gold: -1, Diamond: 0, Energy: 0, Shard: 0 } } })));
  await assertFails(setDoc(doc(authed(), savePath()),
    saveDocument(1, { currency: { balances: { Gold: 100, Diamond: 0, Energy: 0, Shard: 0, Ruby: 1 } } })));
});

// --- 14. 신규 계정 방어 -----------------------------------------------------

// 신규 계정의 첫 문서(create 경로). 픽스처는 에뮬레이터에 Unity 클라를 붙여 캡처한 모양이다.
//
// 이 경로는 실클라로 검증하지 못했다 — 문서를 지우고 재부트하면 Unity Firestore 네이티브
// 클라이언트가 에디터 2회차 Play 에서 "client is offline" 을 뱉어(도메인 리로드를 넘어
// 살아남는다) 부트가 룰까지 가지도 못한다. 에디터 완전 재시작이 필요하다.
// 그래서 create 는 여기가 유일한 방어선이다.
//
// ProfileSaveData 의 nickname/avatarId/frameId 가 null 인 건 실수가 아니라 설계다 —
// 기본 id 를 세이브에 굳히지 않으려는 것이고, ProfileManager.Init 이 IsNullOrEmpty 폴백을 한다.
// 룰에서 'profile.nickname is string' 으로 조이면 신규 유저가 첫 저장부터 막힌다.
test('14. 신규 계정 첫 문서로 create 가 통과한다 (profile 3필드 null)', async () => {
  await assertSucceeds(setDoc(doc(authed(), savePath()), freshAccountDocument()));
});

// 신규 계정은 성장 항목이 아직 없다 — 기본값 이하 항목은 저장에서 빠져 빈 map 으로 나간다.
// 룰이 'cardGrowth is map' 만 보므로 통과해야 한다. 여기가 막히면 신규 계정이 전부 막힌다.
test('14c. 빈 성장 map · 빈 덱 슬롯도 통과', async () => {
  const t_doc = freshAccountDocument();
  await assertSucceeds(setDoc(doc(authed(), savePath()), t_doc));
});

// create 로 들어온 문서가 이어서 update 되는지까지 봐야 신규 계정 한 바퀴가 닫힌다.
test('14d. 신규 계정 create 직후 update 가 이어진다', async () => {
  await assertSucceeds(setDoc(doc(authed(), savePath()), freshAccountDocument()));
  await assertSucceeds(setDoc(doc(authed(), savePath()), saveDocument(2)));
});

// 슬롯 '안쪽'의 null 은 허용하고(위 14), 슬롯 '자체'의 null 은 거부한다.
// 이 경계가 흐려지면 14를 통과시키려다 슬롯 통째 null 까지 열어주게 된다.
// 클라는 슬롯 통째 null 을 절대 안 보낸다 — UserSaveData 의 슬롯 10개가 전부
// 프로퍼티 이니셜라이저로 non-null 이다. 그러니 이게 오면 조작이거나 콘솔 수작업이다.
// (DataSaveManager.Normalize 가 읽기 쪽에서 복구해 주긴 하지만 그건 안전망이지 계약이 아니다.)
test('14b. 슬롯 자체가 null 이면 거부', async () => {
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(1, { profile: null })));
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(1, { currency: null })));
});

// --- 15. 스펙 표 ------------------------------------------------------------

test('15. specs 는 인증되면 읽히고, 미인증은 거부', async () => {
  await testEnv.withSecurityRulesDisabled(async (_ctx) => {
    await setDoc(doc(_ctx.firestore(), 'envs/test/specs/Card'), { rowCount: 1 });
    await setDoc(doc(_ctx.firestore(), 'envs/test/specs/Card/rows/1'), { id: 1 });
  });

  await assertSucceeds(getDoc(doc(authed(), 'envs/test/specs/Card')));
  await assertSucceeds(getDoc(doc(authed(), 'envs/test/specs/Card/rows/1')));
  await assertFails(getDoc(doc(unauthed(), 'envs/test/specs/Card')));
  await assertFails(getDoc(doc(unauthed(), 'envs/test/specs/Card/rows/1')));
});

// specs 쓰기는 admin 커스텀 클레임 전용이다. 익명 플레이어는 이 클레임을 절대 못 갖는다.
// 에디터 업로더(SpecFirestoreUploader)는 ID 토큰을 실어야 통과한다 — functions/scripts/grant-admin.js 참조.
test('15b. specs 쓰기는 admin 클레임만 통과', async () => {
  await assertFails(setDoc(doc(unauthed(), 'envs/test/specs/Card'), { rowCount: 2 }));
  await assertFails(setDoc(doc(authed(), 'envs/test/specs/Card'), { rowCount: 2 }));
  const t_admin = testEnv.authenticatedContext('deployer', { admin: true }).firestore();
  await assertSucceeds(setDoc(doc(t_admin, 'envs/test/specs/Card'), { rowCount: 2 }));
  await assertSucceeds(setDoc(doc(t_admin, 'envs/test/specs/Card/rows/1'), { id: 1 }));
});

// --- 그 외 경로 -------------------------------------------------------------

test('16. 화이트리스트 밖 경로는 거부', async () => {
  await assertFails(setDoc(doc(authed(), 'envs/test/users/player-a/other/x'), { a: 1 }));
  await assertFails(getDoc(doc(authed(), 'anything/else')));
});

// 매치 문서는 서버(Admin SDK)만 쓴다. 클라가 여기 손대면 결과 대조가 무의미해진다.
test('16c. 매치 결과 문서는 클라가 읽지도 쓰지도 못한다', async () => {
  await testEnv.withSecurityRulesDisabled(async (_ctx) => {
    await setDoc(doc(_ctx.firestore(), 'envs/test/matches/m1'), { status: 'pending' });
  });
  await assertFails(getDoc(doc(authed(), 'envs/test/matches/m1')));
  await assertFails(setDoc(doc(authed(), 'envs/test/matches/m1'), { status: 'confirmed' }));
});

test('16b. save/current 하위 서브컬렉션은 거부', async () => {
  await assertFails(setDoc(doc(authed(), `${savePath()}/shadow/x`), { a: 1 }));
  await assertFails(getDoc(doc(authed(), `${savePath()}/shadow/x`)));
});
