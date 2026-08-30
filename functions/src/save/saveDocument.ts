import {HttpsError} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {
  DocumentData,
  FieldValue,
  Transaction,
} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {ENVIRONMENTS, isKnownEnv} from "./environments";
import {Balances} from "../currency/wallet";
import {
  createWallet,
  readWallet,
  walletRef,
  WalletPatch,
  WalletState,
  writeWallet,
} from "../currency/walletStore";

// 환경 판정의 원본은 save/environments 다. 기존 호출부가 saveDocument 에서 가져다 쓰므로 재수출한다.
export {isKnownEnv};

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
export const SCHEMA_VERSION = 8;

/** 서버가 쓴 슬롯의 **갱신 후 전체 값**. 부분 leaf가 아니다. */
export type SlotPatch = Record<string, Record<string, unknown>>;

/** mutate 콜백이 돌려주는 것. wallet 은 지갑을 바꾼 명령만 채운다. */
export interface SaveMutation {
  slots: SlotPatch;
  wallet?: WalletState;
}

// WalletPatch 선언은 walletStore(미러 대상)로 옮겼다 — 재화 codebase 도 같은 응답 모양을 쓴다.
// 기존 import 경로를 깨지 않으려고 여기서 재수출한다.
export type {WalletPatch};

/** 모든 callable 응답이 공유하는 채택 계약. */
export interface SaveMutationResult {
  revision: number;
  updatedSlots: SlotPatch;
  wallet: WalletPatch;
}

/**
 * 세이브 문서 참조. 클라 PlayerSaveFirestorePaths 와 같은 경로여야 한다.
 * @param {string} env 환경 id (live/test)
 * @param {string} uid 유저 uid
 * @return {FirebaseFirestore.DocumentReference} 문서 참조
 */
export function saveDocument(env: string, uid: string) {
  if (!ENVIRONMENTS.includes(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }
  return db
    .collection("envs").doc(env)
    .collection("users").doc(uid)
    .collection("save").doc("current");
}

/**
 * 호출자의 uid를 꺼낸다.
 * @param {{uid: string} | undefined} auth callable 인증 정보
 * @return {string} uid
 */
export function requireUid(auth?: {uid: string}): string {
  if (!auth?.uid) {
    throw new HttpsError("unauthenticated", "Sign-in is required.");
  }
  return auth.uid;
}

/**
 * 문서 스키마 버전이 이 서버가 쓸 수 있는 값인지 판정한다. **정확히 SCHEMA_VERSION** 만
 * 통과시키고, 벗어날 때 낮음/높음을 다른 오류 코드로 가른다 — 원인도 조치도 다르기 때문이다.
 * 클라 PlayerSaveCloud 의 부트 게이트가 remote>client / remote<client 를
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
export function assertWritableSchema(
  rawVersion: unknown,
  env: string,
  uid: string,
): void {
  const documentVersion =
    typeof rawVersion === "number" ? rawVersion : Number.NaN;
  if (documentVersion === SCHEMA_VERSION) {
    return;
  }

  // 로그와 에러 메시지 양쪽에 기대값·실제값을 모두 남긴다 — 드리프트는
  // 전 callable을 한꺼번에 죽이므로 "왜"가 남지 않으면 원인 추적이 막힌다.
  const drift = {
    uid,
    env,
    serverSchemaVersion: SCHEMA_VERSION,
    documentSchemaVersion: rawVersion ?? null,
  };
  const seen = `document v${String(rawVersion)} vs server v${SCHEMA_VERSION}`;

  if (!Number.isFinite(documentVersion)) {
    logger.error("save schema unreadable", drift);
    throw new HttpsError(
      "failed-precondition",
      `Save schema is unreadable (${seen}): the document's schemaVersion ` +
      "is missing or is not a number.",
      drift,
    );
  }

  if (documentVersion > SCHEMA_VERSION) {
    logger.error("save schema drift: server is behind the document", drift);
    throw new HttpsError(
      "out-of-range",
      `Save schema drift (${seen}): the document is newer than this ` +
      "server. Deploy functions built from the same commit as the client " +
      "(UserSaveData.VERSION); retrying will not help.",
      drift,
    );
  }

  logger.error("save schema drift: document is stale", drift);
  throw new HttpsError(
    "failed-precondition",
    `Save schema drift (${seen}): the document is older than the schema this ` +
    `server writes (v${SCHEMA_VERSION}). It must be migrated (ensureWallet) ` +
    "or deleted and recreated before it is writable.",
    drift,
  );
}

/**
 * 세이브 문서를 트랜잭션 1회로 읽고 고친다. revision +1 과 updatedAt 은
 * 여기서만 움직인다 — callable 하나당 문서 쓰기 1회라는 계약의 집행 지점.
 *
 * 지갑 문서도 **항상 함께 읽어** 콜백에 넘기고 응답에 싣는다(옵션 플래그를 두지 않는다).
 * 바뀌지 않은 지갑을 매번 내보내는 것은 클라 채택이 단조·멱등이라 무해하고,
 * 클라가 어떤 이유로든 드리프트했을 때 다음 명령이 스스로 맞춰 준다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {Function} mutate 현재 문서·트랜잭션·지갑을 받아 갱신할 슬롯 전체 값을 돌려준다
 * @return {Promise<SaveMutationResult>} 새 revision · 갱신된 슬롯 · 지갑
 */
export async function mutateSave(
  env: string,
  uid: string,
  mutate: (
    current: DocumentData,
    transaction: Transaction,
    wallet: WalletState,
  ) => Promise<SaveMutation> | SaveMutation,
): Promise<SaveMutationResult> {
  const reference = saveDocument(env, uid);
  const walletReference = walletRef(db, env, uid);

  return db.runTransaction(async (transaction) => {
    const snapshot = await transaction.get(reference);
    if (!snapshot.exists) {
      throw new HttpsError(
        "failed-precondition",
        "Save document does not exist.",
      );
    }

    const current = snapshot.data() ?? {};
    assertWritableSchema(current.schemaVersion, env, uid);

    // 지갑 읽기는 콜백 진입 **전에** 끝낸다 — Firestore 트랜잭션은 모든 읽기가 모든 쓰기보다
    // 앞서야 하는데, openPack 처럼 재실행되는 명령 안에서 읽으면 그 순서가 깨진다.
    const walletSnapshot = await transaction.get(walletReference);
    let wallet = readWallet(walletSnapshot);

    // 여기서 승급하지 않는다 — 위 판정을 통과한 문서는 이미 v8 이고, v7 이관은
    // 그것만을 위해 있는 commands/ensureWallet 의 일이다.
    //
    // 다만 지갑 부재는 메운다: v8 문서는 currency 슬롯이 없으므로 지갑이 사라진 계정은
    // 잔액을 주장하는 곳이 어디에도 없다. 잔액 0 으로 세우는 것이 그 상태의 정답이고
    // 잃는 것이 없다. 안 세우면 지갑을 쓰는 명령이 전부 실패해 계정이 굳는다.
    const creatingWallet = !walletSnapshot.exists;

    const revision = Number(current.revision ?? 0) + 1;
    const outcome = await mutate(current, transaction, wallet);

    transaction.update(reference, {
      ...outcome.slots,
      revision,
      updatedAt: FieldValue.serverTimestamp(),
    });

    if (creatingWallet) {
      // set 이 아니라 create 다 — 이 트랜잭션 밖에서 ensureWallet 이 먼저 지갑을 세웠으면
      // 재실행되어 그쪽 이관 잔액을 0 으로 덮어쓰는 것을 막는다.
      wallet = createWallet(
        transaction,
        walletReference,
        outcome.wallet?.balances ?? wallet.balances,
        FieldValue.serverTimestamp(),
      );
    } else if (outcome.wallet !== undefined) {
      writeWallet(
        transaction, walletReference, outcome.wallet,
        FieldValue.serverTimestamp());
      wallet = outcome.wallet;
    }

    return {
      revision,
      updatedSlots: outcome.slots,
      wallet: {rev: wallet.rev, balances: wallet.balances},
    };
  });
}

/** 계정 확보 결과. created 가 false 면 문서가 이미 있었고 이 호출은 아무것도 쓰지 않았다. */
export interface EnsureAccountOutcome {
  revision: number;
  created: boolean;
  walletCreated: boolean;
  /** 메타가 깨진 기존 문서를 버리고 다시 만들었는가. 복구 경로를 로그로 남기기 위한 표시다. */
  repaired: boolean;
  /** 복구 시 버려진 문서의 최상위 필드 이름들. 원인 추적용이며 값은 남기지 않는다. */
  discardedFields: string[];
}

/**
 * 클라 PlayerSaveDocument.TryReadMeta 와 같은 판정이다 — 여기서 통과시킨 문서만 클라가 부트할 수 있다.
 * @param {FirebaseFirestore.DocumentData | undefined} data 문서 본문
 * @return {boolean} 메타가 온전한가
 */
function hasUsableMeta(data: FirebaseFirestore.DocumentData | undefined): boolean {
  if (data == null) return false;
  const schemaVersion = data.schemaVersion;
  const revision = data.revision;
  return Number.isInteger(schemaVersion) && (schemaVersion as number) > 0 &&
         Number.isInteger(revision) && (revision as number) >= 0;
}

/**
 * 세이브 문서를 확보한다 — 없으면 만들고, 있으면 그대로 둔 채 현재 revision 만 돌려준다.
 *
 * mutateSave 와 갈라 두는 이유: 저쪽은 "callable 1회 = 문서 쓰기 1회, revision +1" 이 계약이고
 * R5~R8 이 전부 그 위에 선다. 생성 분기를 섞으면 그 불변식이 흐려진다.
 *
 * 이미 있는 문서에는 스키마 검사를 하지 않는다 — 드리프트는 클라 부트가 다시 읽으며
 * MarkUpdateRequired / Fail 로 훨씬 나은 표면을 만든다. 여기서 던지면 그 갈래를 못 밟는다.
 *
 * 지갑도 **같은 트랜잭션**에서 만든다. 두 문서가 갈라지면 세이브만 있는 계정이 생기고,
 * 그 계정은 부트의 ensureWallet 이 0 잔액 지갑을 세워 스타터 골드를 영영 잃는다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {string} deviceId 클라 기기 id (32자 hex)
 * @param {string} appVersion 클라 앱 버전
 * @param {Function} buildSlots 새 문서에 실을 슬롯 9개
 * @param {Balances} starterBalances 같은 트랜잭션에서 세울 지갑의 최초 잔액
 * @return {Promise<EnsureAccountOutcome>} 새 revision 과 생성 여부
 */
export async function ensureSaveDocument(
  env: string,
  uid: string,
  deviceId: string,
  appVersion: string,
  buildSlots: () => SlotPatch,
  starterBalances: Balances,
): Promise<EnsureAccountOutcome> {
  const reference = saveDocument(env, uid);
  const walletReference = walletRef(db, env, uid);

  return db.runTransaction(async (transaction) => {
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
      schemaVersion: SCHEMA_VERSION,
      revision: 1,
      updatedAt: FieldValue.serverTimestamp(),
      deviceId,
      appVersion,
    };

    const walletCreated = !walletSnapshot.exists;
    if (walletCreated) {
      createWallet(transaction, walletReference, starterBalances, FieldValue.serverTimestamp());
    }

    // 메타가 없거나 깨진 문서는 클라가 부트조차 못 하는데(TryReadMeta 실패 → Fail),
    // 그대로 두면 여기서도 noop 이라 계정이 영구 잠긴다 — 룰에 delete 경로도 없다.
    // 스키마 밖 문서는 유효한 세이브였던 적이 없으므로 버리고 새로 만드는 것이 유일한 복구다.
    if (snapshot.exists) {
      const discardedFields = Object.keys(data ?? {}).sort();
      transaction.set(reference, fresh);
      return {revision: 1, created: true, walletCreated, repaired: true, discardedFields};
    }

    // set 이 아니라 create 다 — 트랜잭션 밖에서 누가 먼저 만들었으면 재실행되어 덮어쓰기가 막힌다.
    transaction.create(reference, fresh);

    return {revision: 1, created: true, walletCreated, repaired: false, discardedFields: []};
  });
}
