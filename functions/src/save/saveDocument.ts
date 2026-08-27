import {HttpsError} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {
  DocumentData,
  FieldValue,
  Transaction,
} from "firebase-admin/firestore";
import {db} from "../firebaseApp";

/**
 * 세이브 문서 스키마 버전. 클라 쪽 쌍둥이 상수와 **항상 같은 값**이어야 한다.
 *   Assets/Scripts/OutGame/Save/2.Domain/UserSaveData.cs
 *   -> UserSaveData.VERSION
 * TS와 C#이 상수를 공유할 방법이 없으니, 이 줄을 고치는 커밋은 반드시 저
 * 파일도 같이 고친다. 정책: 필드·슬롯 추가는 버전을 올리지 않고 흡수하고,
 * 파괴적 변경일 때만 서버·클라를 동시에 올린 뒤 기존 문서를 삭제·재생성한다.
 */
export const SCHEMA_VERSION = 7;

const ENVIRONMENTS = ["live", "test"];

/**
 * 알려진 환경인가. 던지지 않고 묻는 쪽(진단 함수)이 쓴다.
 * @param {string} env 환경 id
 * @return {boolean} 알려진 환경이면 true
 */
export function isKnownEnv(env: string): boolean {
  return ENVIRONMENTS.includes(env);
}

/** 서버가 쓴 슬롯의 **갱신 후 전체 값**. 부분 leaf가 아니다. */
export type SlotPatch = Record<string, Record<string, unknown>>;

/** 모든 callable 응답이 공유하는 채택 계약. */
export interface SaveMutationResult {
  revision: number;
  updatedSlots: SlotPatch;
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
 * 문서 스키마 버전이 이 서버가 쓸 수 있는 값인지 판정한다. 같지 않을 때
 * 낮음/높음을 다른 오류 코드로 가른다 — 원인도 조치도 다르기 때문이다.
 * 클라 PlayerSaveCloud 의 부트 게이트가 remote>client / remote<client 를
 * 가르는 것과 같은 축이다.
 * @param {unknown} rawVersion 문서에 적힌 schemaVersion 원본 값
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 */
function assertWritableSchema(
  rawVersion: unknown,
  env: string,
  uid: string,
): void {
  const documentVersion =
    typeof rawVersion === "number" ? rawVersion : Number.NaN;
  if (documentVersion === SCHEMA_VERSION) return;

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
    `Save schema drift (${seen}): the document is older than this server. ` +
    "It must be migrated or deleted and recreated before it is writable.",
    drift,
  );
}

/**
 * 세이브 문서를 트랜잭션 1회로 읽고 고친다. revision +1 과 updatedAt 은
 * 여기서만 움직인다 — callable 하나당 문서 쓰기 1회라는 계약의 집행 지점.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {Function} mutate 현재 문서를 받아 갱신할 슬롯 전체 값을 돌려준다
 * @return {Promise<SaveMutationResult>} 새 revision 과 갱신된 슬롯
 */
export async function mutateSave(
  env: string,
  uid: string,
  mutate: (
    current: DocumentData,
    transaction: Transaction,
  ) => Promise<SlotPatch> | SlotPatch,
): Promise<SaveMutationResult> {
  const reference = saveDocument(env, uid);

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

    const revision = Number(current.revision ?? 0) + 1;
    const updatedSlots = await mutate(current, transaction);

    transaction.update(reference, {
      ...updatedSlots,
      revision,
      updatedAt: FieldValue.serverTimestamp(),
    });

    return {revision, updatedSlots};
  });
}

/** 계정 확보 결과. created 가 false 면 문서가 이미 있었고 이 호출은 아무것도 쓰지 않았다. */
export interface EnsureAccountOutcome {
  revision: number;
  created: boolean;
}

/**
 * 세이브 문서를 확보한다 — 없으면 만들고, 있으면 그대로 둔 채 현재 revision 만 돌려준다.
 *
 * mutateSave 와 갈라 두는 이유: 저쪽은 "callable 1회 = 문서 쓰기 1회, revision +1" 이 계약이고
 * R5~R8 이 전부 그 위에 선다. 생성 분기를 섞으면 그 불변식이 흐려진다.
 *
 * 이미 있는 문서에는 스키마 검사를 하지 않는다 — 드리프트는 클라 부트가 다시 읽으며
 * MarkUpdateRequired / Fail 로 훨씬 나은 표면을 만든다. 여기서 던지면 그 갈래를 못 밟는다.
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 * @param {string} deviceId 클라 기기 id (32자 hex)
 * @param {string} appVersion 클라 앱 버전
 * @param {Function} buildSlots 새 문서에 실을 슬롯 10개
 * @return {Promise<EnsureAccountOutcome>} 새 revision 과 생성 여부
 */
export async function ensureSaveDocument(
  env: string,
  uid: string,
  deviceId: string,
  appVersion: string,
  buildSlots: () => SlotPatch,
): Promise<EnsureAccountOutcome> {
  const reference = saveDocument(env, uid);

  return db.runTransaction(async (transaction) => {
    const snapshot = await transaction.get(reference);
    if (snapshot.exists) {
      return {revision: Number(snapshot.data()?.revision ?? 0), created: false};
    }

    // set 이 아니라 create 다 — 트랜잭션 밖에서 누가 먼저 만들었으면 재실행되어 덮어쓰기가 막힌다.
    transaction.create(reference, {
      ...buildSlots(),
      schemaVersion: SCHEMA_VERSION,
      revision: 1,
      updatedAt: FieldValue.serverTimestamp(),
      deviceId,
      appVersion,
    });

    return {revision: 1, created: true};
  });
}
