// firestore.rules 회귀 테스트.
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
import {
  saveDocument,
  freshAccountDocument,
  serverFreshAccountDocument,
  legacyCurrencySlot,
  SCHEMA_VERSION,
} from './fixtures/saveDocument.js';
import { walletDocument, ledgerDocument } from './fixtures/walletDocument.js';
import { grantsDocument } from './fixtures/grantsDocument.js';

const RULES_PATH = process.env.RULES_FILE ?? fileURLToPath(new URL('../../firestore.rules', import.meta.url));
const PROJECT_ID = 'tcg-rules-test';
const UID = 'player-a';
const OTHER_UID = 'player-b';

let testEnv;

const savePath = (_uid = UID, _env = 'test', _docId = 'current') =>
  `envs/${_env}/users/${_uid}/save/${_docId}`;

const walletPath = (_uid = UID, _env = 'test', _docId = 'current') =>
  `envs/${_env}/users/${_uid}/wallet/${_docId}`;

const grantsPath = (_uid = UID, _env = 'test', _docId = 'current') =>
  `envs/${_env}/users/${_uid}/grants/${_docId}`;

const authed = (_uid = UID) => testEnv.authenticatedContext(_uid).firestore();
const unauthed = () => testEnv.unauthenticatedContext().firestore();

// 룰을 끄고 심는다 = Admin SDK(서버)가 쓴 상태의 재현이다.
// R4 이후 클라 create 가 막혀, 문서가 이미 있는 상태를 만드는 유일한 방법이기도 하다.
async function seedRaw(_path, _document) {
  await testEnv.withSecurityRulesDisabled(async (_ctx) => {
    await setDoc(doc(_ctx.firestore(), _path), _document);
  });
}

async function seed(_revision = 1, _overrides = {}, _uid = UID) {
  await seedRaw(savePath(_uid), saveDocument(_revision, _overrides));
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

// R4 부터 문서 생성은 서버(ensureAccount)만 한다. 페이로드를 실클라 그대로 두는 이유는
// "거부 이유가 문서 모양이 아니라 create 라는 행위 자체"임을 남기기 위해서다.
test('1. 실클라 14키 문서여도 클라 create 는 거부', async () => {
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(1)));
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
  await seed(1, {}, OTHER_UID);
  await assertFails(setDoc(doc(authed(UID), savePath(OTHER_UID)), saveDocument(2)));
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

// create 가 닫힌 뒤로는 revision 값을 고르는 문제가 아니다 — 어떤 값이든 클라는 문서를 못 만든다.
test('6. 클라 create 는 어떤 revision 으로도 거부', async () => {
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(0)));
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(1)));
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(2)));
});

// --- 7·8. 필드 목록 / 삭제 --------------------------------------------------

test('7. 알 수 없는 top-level 필드는 거부', async () => {
  await seed(1);
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(2, { serverSecret: 'x' })),
  );
});

test('7b. 메타 필드 누락은 거부', async () => {
  await seed(1);
  const t_doc = saveDocument(2);
  delete t_doc.deviceId;
  await assertFails(setDoc(doc(authed(), savePath()), t_doc));
});

// 감사에서 나온 구멍 — 슬롯이 optional 이면 필드 생략으로 세이브를 비울 수 있다.
test('7c. 슬롯 누락 update 는 거부 (세이브 비우기 우회)', async () => {
  await seed(1);
  const t_meta = saveDocument(2);
  for (const t_slot of ['ownership', 'deck', 'cardGrowth', 'keywordGrowth',
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

// 감사가 잡았던 구멍(999999 로 create 하면 영구 고착)은 create 를 닫아 원천 봉쇄됐다.
// 이제 새 문서의 schemaVersion 앵커는 functions/src/save/saveDocument.ts 의 SCHEMA_VERSION 이다.
test('8b. 클라 create 는 어떤 schemaVersion 으로도 거부', async () => {
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(1, { schemaVersion: 999999 })),
  );
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(1, { schemaVersion: 1 })),
  );
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(1, { schemaVersion: SCHEMA_VERSION })),
  );
});

test('9. 알 수 없는 envId 는 거부', async () => {
  await seedRaw(savePath(UID, 'dev'), saveDocument(1));
  await assertFails(
    setDoc(doc(authed(), savePath(UID, 'dev')), saveDocument(2)),
  );
});

test('9b. 알 수 없는 docId 는 거부', async () => {
  await seedRaw(savePath(UID, 'test', 'backup'), saveDocument(1));
  await assertFails(
    setDoc(doc(authed(), savePath(UID, 'test', 'backup')), saveDocument(2)),
  );
});

test('9c. live 환경은 통과', async () => {
  await seedRaw(savePath(UID, 'live'), saveDocument(1));
  await assertSucceeds(
    setDoc(doc(authed(), savePath(UID, 'live')), saveDocument(2)),
  );
});

// --- 10~13. 메타 형식 -------------------------------------------------------

// 10~13b 는 전부 update 로 검증한다. create 로 두면 "create 라서" 거부되는 통에
// isValidSave 를 통째로 지워도 전부 통과해 버린다 — 룰의 실질 방어력을 못 보게 된다.
test('10. updatedAt 을 클라 시각으로 넣으면 거부', async () => {
  await seed(1);
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(2, { updatedAt: Timestamp.fromDate(new Date()) })),
  );
});

test('11. deviceId 길이가 32가 아니면 거부', async () => {
  await seed(1);
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(2, { deviceId: '0123456789abcdef0123456789abcde' })),
  );
});

test('11b. appVersion 이 64자를 넘으면 거부', async () => {
  await seed(1);
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(2, { appVersion: 'v'.repeat(65) })),
  );
});

test('12. schemaVersion 하향은 거부', async () => {
  await seed(1);
  await assertFails(
    setDoc(doc(authed(), savePath()), saveDocument(2, { schemaVersion: SCHEMA_VERSION - 1 })),
  );
});

test('13. 슬롯 타입 위반은 거부', async () => {
  await seed(1);
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(2, { ownership: [1, 2] })));
});

// C7 — 공존이 닫혔다. 잔액의 진실원은 wallet/current 하나이고 세이브의 currency 슬롯은
// 금지 필드다(hasOnly 14키). 값 검증 블록은 도달 불가라 룰에서 걷어냈으므로, 모양별
// 계약을 따로 볼 자리가 없다 — 어떤 모양이든 키가 실렸다는 사실만으로 거부돼야 한다.
// 여기가 초록인 동안 클라가 15키를 보내면 그 클라의 저장은 전부 거부된다(의도된 벽).
test('13c. currency 가 실리면 모양과 무관하게 거부', async () => {
  await seed(1);
  // 구 클라(v7)가 실제로 보내던 정상 4재화 모양
  await assertFails(setDoc(doc(authed(), savePath()),
    saveDocument(2, { currency: legacyCurrencySlot() })));
  await assertFails(setDoc(doc(authed(), savePath()),
    saveDocument(2, { currency: { balances: { Gold: 100 } } })));
  await assertFails(setDoc(doc(authed(), savePath()),
    saveDocument(2, { currency: { balances: { Gold: '100', Diamond: 0, Energy: 0, Shard: 0 } } })));
  await assertFails(setDoc(doc(authed(), savePath()),
    saveDocument(2, { currency: { balances: { Gold: -1, Diamond: 0, Energy: 0, Shard: 0 } } })));
  await assertFails(setDoc(doc(authed(), savePath()),
    saveDocument(2, { currency: { balances: { Gold: 100, Diamond: 0, Energy: 0, Shard: 0, Ruby: 1 } } })));
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(2, { currency: 'x' })));
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(2, { currency: null })));
});

// 14키가 전부 필수라는 계약. 슬롯 생략으로 세이브를 비우는 우회를 막는다.
test('13d. 슬롯 누락은 거부', async () => {
  await seed(1);
  const t_new = saveDocument(2);
  delete t_new.ownership;
  await assertFails(setDoc(doc(authed(), savePath()), t_new));
});

// --- 14. 신규 계정 방어 -----------------------------------------------------

// R4 이후 신규 계정의 첫 문서는 서버(ensureAccount, Admin SDK)가 만든다. Admin 은 룰을 안 타므로
// 하네스가 서버 쓰기 자체는 볼 수 없다 — 여기서 봐야 하는 것은 그 산출물 위에서 클라의 다음
// 저장이 통과하는가다. 서버가 isValidSave 를 깨는 문서를 만들면 그 계정은 이후 모든 저장이
// 영구 거부되고 delete: if false 라 룰 층에 복구 경로가 없다.
//
// 서버 산출물의 모양 자체는 반대편에서 functions/scripts/test-fresh-account.js 가 못박는다.
//
// ProfileSaveData 의 nickname/avatarId/frameId 가 null 인 건 실수가 아니라 설계다 —
// 기본 id 를 세이브에 굳히지 않으려는 것이고, ProfileManager.Init 이 IsNullOrEmpty 폴백을 한다.
// 룰에서 'profile.nickname is string' 으로 조이면 신규 유저가 첫 저장부터 막힌다.
test('14. 서버가 만든 신규 계정 문서 위에서 클라 update 가 통과한다 (profile 3필드 null)', async () => {
  await seedRaw(savePath(), serverFreshAccountDocument());
  await assertSucceeds(setDoc(doc(authed(), savePath()), saveDocument(2)));
});

// 신규 계정은 성장 항목이 아직 없고 덱은 빈 슬롯까지 실린다. 룰이 'cardGrowth is map' 만
// 보므로 통과해야 한다 — 여기가 막히면 신규 계정의 첫 저장이 전부 막힌다.
test('14c. 빈 성장 map · 6칸 덱 슬롯을 그대로 되쓰는 update 도 통과', async () => {
  await seedRaw(savePath(), serverFreshAccountDocument());
  const t_doc = serverFreshAccountDocument({ revision: 2 });
  await assertSucceeds(setDoc(doc(authed(), savePath()), t_doc));
});

// R4 의 핵심 회귀 — 서버가 만드는 것과 똑같은 모양이어도 클라가 만들면 거부다.
// 이게 뚫리면 스타터 지급을 클라가 정하게 되고 골드·소유 카드가 위조된 채 첫 문서에 굳는다.
test('14d. 서버 문서와 같은 모양이어도 클라 create 는 거부', async () => {
  await assertFails(setDoc(doc(authed(), savePath()), serverFreshAccountDocument()));
  await assertFails(setDoc(doc(authed(), savePath()), freshAccountDocument()));
});

// 슬롯 '안쪽'의 null 은 허용하고(위 14), 슬롯 '자체'의 null 은 거부한다.
// 이 경계가 흐려지면 14를 통과시키려다 슬롯 통째 null 까지 열어주게 된다.
// 클라는 슬롯 통째 null 을 절대 안 보낸다 — UserSaveData 의 슬롯 9개가 전부
// 프로퍼티 이니셜라이저로 non-null 이다. 그러니 이게 오면 조작이거나 콘솔 수작업이다.
// (DataSaveManager.Normalize 가 읽기 쪽에서 복구해 주긴 하지만 그건 안전망이지 계약이 아니다.)
// 두 줄로 보는 이유: 필수 슬롯(profile)과 optional 슬롯(구 클라 currency)은 룰에서 검증을 타는
// 경로가 갈린다 — optional 쪽은 hasAny 게이트 안이라 한쪽만 보면 반쪽이 빈다.
test('14b. 슬롯 자체가 null 이면 거부', async () => {
  await seed(1);
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(2, { profile: null })));
  await assertFails(setDoc(doc(authed(), savePath()), saveDocument(2, { currency: null })));
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

// --- 17~19. 지갑(재화) ------------------------------------------------------

// 지갑은 서버(Admin SDK) 전용 쓰기다 — 잔액을 바꾸는 것은 Callable 뿐이고 클라는 읽기만 한다.
// 거부 케이스는 전부 withSecurityRulesDisabled 로 문서를 먼저 심는다. 안 심으면 룰이 아니라
// '문서가 없어서' 실패해 통과처럼 보이고, allow 를 통째로 열어도 초록이 된다.
async function seedWallet(_uid = UID, _env = 'test', _docId = 'current') {
  await seedRaw(walletPath(_uid, _env, _docId), walletDocument());
}

test('17. 소유자는 자기 지갑을 읽는다', async () => {
  await seedWallet();
  await assertSucceeds(getDoc(doc(authed(), walletPath())));
});

test('17b. 남의 uid 지갑 읽기는 거부', async () => {
  await seedWallet(OTHER_UID);
  await assertFails(getDoc(doc(authed(UID), walletPath(OTHER_UID))));
});

test('17c. 미인증 지갑 읽기는 거부', async () => {
  await seedWallet();
  await assertFails(getDoc(doc(unauthed(), walletPath())));
});

test('17d. 알 수 없는 envId 지갑 읽기는 거부', async () => {
  await seedWallet(UID, 'dev');
  await assertFails(getDoc(doc(authed(), walletPath(UID, 'dev'))));
});

test('17e. 알 수 없는 docId 지갑 읽기는 거부', async () => {
  await seedWallet(UID, 'test', 'other');
  await assertFails(getDoc(doc(authed(), walletPath(UID, 'test', 'other'))));
});

// 잔액을 클라가 만들 수 있으면 재화 발행권이 클라로 넘어간다. create·update·delete 셋 다 막혀야 한다.
test('18. 소유자도 지갑 create 는 거부', async () => {
  await assertFails(setDoc(doc(authed(), walletPath()), walletDocument()));
});

test('18b. 소유자도 지갑 update 는 거부', async () => {
  await seedWallet();
  await assertFails(setDoc(doc(authed(), walletPath()), walletDocument({ rev: 2 })));
});

test('18c. 소유자도 지갑 delete 는 거부', async () => {
  await seedWallet();
  await assertFails(deleteDoc(doc(authed(), walletPath())));
});

// 원장은 감사 기록이다 — 읽히면 잔액 추론 표면만 넓어진다.
test('19. 소유자도 원장 읽기는 거부', async () => {
  await seedWallet();
  await seedRaw(`${walletPath()}/ledger/tx1`, ledgerDocument());
  await assertFails(getDoc(doc(authed(), `${walletPath()}/ledger/tx1`)));
});

test('19b. 소유자도 원장 쓰기는 거부', async () => {
  await seedWallet();
  await seedRaw(`${walletPath()}/ledger/tx1`, ledgerDocument());
  await assertFails(setDoc(doc(authed(), `${walletPath()}/ledger/tx1`), ledgerDocument({ rev: 3 })));
});


// --- 20·21. 튜토리얼 무료 한 방 (envs/{env}/users/{uid}/grants/current) ------

// 축(카드 강화·키워드 강화)마다 계정당 1회를 서버가 소유한다. 지갑과 문서 모양은 같지만
// 읽기를 여는 이유가 다르다 — 화면이 "무료" 표시를 그리려면 남았는지 알아야 한다.
async function seedGrants(_uid = UID, _env = 'test', _docId = 'current') {
  await seedRaw(grantsPath(_uid, _env, _docId), grantsDocument());
}

test('20. 소유자는 자기 무료 한 방 문서를 읽는다', async () => {
  await seedGrants();
  await assertSucceeds(getDoc(doc(authed(), grantsPath())));
});

test('20b. 남의 uid 무료 한 방 읽기는 거부', async () => {
  await seedGrants(OTHER_UID);
  await assertFails(getDoc(doc(authed(UID), grantsPath(OTHER_UID))));
});

test('20c. 미인증 무료 한 방 읽기는 거부', async () => {
  await seedGrants();
  await assertFails(getDoc(doc(unauthed(), grantsPath())));
});

test('20d. 알 수 없는 envId 무료 한 방 읽기는 거부', async () => {
  await seedGrants(UID, 'dev');
  await assertFails(getDoc(doc(authed(), grantsPath(UID, 'dev'))));
});

test('20e. 알 수 없는 docId 무료 한 방 읽기는 거부', async () => {
  await seedGrants(UID, 'test', 'other');
  await assertFails(getDoc(doc(authed(), grantsPath(UID, 'test', 'other'))));
});

// 소진 낙인을 클라가 지울 수 있으면 무료 한 방이 무한이 된다 — 앱 재시작으로 되살아나던
// 정적 필드를 문서로 옮긴 이유가 그것이라 create·update·delete 셋 다 막혀야 한다.
test('21. 소유자도 무료 한 방 create 는 거부', async () => {
  await assertFails(setDoc(doc(authed(), grantsPath()), grantsDocument()));
});

test('21b. 소유자도 무료 한 방 update 는 거부', async () => {
  await seedGrants();
  await assertFails(setDoc(doc(authed(), grantsPath()), grantsDocument({ enhanceCard: false })));
});

test('21c. 소유자도 무료 한 방 delete 는 거부', async () => {
  await seedGrants();
  await assertFails(deleteDoc(doc(authed(), grantsPath())));
});
