import {HttpsError} from "firebase-functions/v2/https";
import {
  DocumentData,
  FieldValue,
  Transaction,
} from "firebase-admin/firestore";
import {db} from "../firebaseApp";

/** 클라 UserSaveData.VERSION 과 같아야 한다. */
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
    const schemaVersion = Number(current.schemaVersion ?? 0);
    if (schemaVersion !== SCHEMA_VERSION) {
      throw new HttpsError(
        "failed-precondition",
        `Save schema v${schemaVersion} is not writable by this server.`,
      );
    }

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
