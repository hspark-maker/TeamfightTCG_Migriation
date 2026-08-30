import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {
  isKnownEnv,
  requireUid,
  saveDocument,
  SCHEMA_VERSION,
  WalletPatch,
} from "../save/saveDocument";
import {migrateFromSaveSlot} from "../currency/walletMigration";
import {createWallet, readWallet, walletRef} from "../currency/walletStore";

/** 이 명령이 승급시킬 수 있는 가장 낮은 세이브 스키마 버전. 이관 직전 판인 v7 이다. */
export const MIGRATABLE_SCHEMA_VERSION = SCHEMA_VERSION - 1;

/** 트랜잭션이 돌려주는 것. created 가 false 면 아무것도 쓰지 않았고 revision 은 뜻이 없다. */
interface EnsureWalletOutcome {
  created: boolean;
  revision: number;
  wallet: WalletPatch;
}

/**
 * 승급 대상 문서인지 판정한다. save/saveDocument 의 assertWritableSchema 를 쓰지 않는다 —
 * 저쪽은 "이미 v8 인 문서만 쓴다"가 계약이고, 이 명령은 그 v8 로 **올리는** 유일한 자리라
 * 판정이 정반대다. v7(이관 대상)과 v8(이미 이관됨, 지갑만 세우는 멱등 경로)만 받는다.
 *
 * export 인 것은 순수 회귀(scripts/test-schema-window.js)가 이 판정을 못박기 때문이다.
 * @param {unknown} rawVersion 문서에 적힌 schemaVersion 원본 값
 * @param {string} env 환경 id
 * @param {string} uid 유저 uid
 */
export function assertMigratableSchema(
  rawVersion: unknown,
  env: string,
  uid: string,
): void {
  const documentVersion =
    typeof rawVersion === "number" ? rawVersion : Number.NaN;
  if (documentVersion === MIGRATABLE_SCHEMA_VERSION ||
      documentVersion === SCHEMA_VERSION) {
    return;
  }

  const drift = {
    uid,
    env,
    serverSchemaVersion: SCHEMA_VERSION,
    migratableSchemaVersion: MIGRATABLE_SCHEMA_VERSION,
    documentSchemaVersion: rawVersion ?? null,
  };
  const seen = `document v${String(rawVersion)} vs server v${SCHEMA_VERSION}`;

  if (!Number.isFinite(documentVersion)) {
    logger.error("wallet migration: save schema unreadable", drift);
    throw new HttpsError(
      "failed-precondition",
      `Save schema is unreadable (${seen}): the document's schemaVersion ` +
      "is missing or is not a number.",
      drift,
    );
  }

  if (documentVersion > SCHEMA_VERSION) {
    logger.error("wallet migration: server is behind the document", drift);
    throw new HttpsError(
      "out-of-range",
      `Save schema drift (${seen}): the document is newer than this ` +
      "server. Deploy functions built from the same commit as the client " +
      "(UserSaveData.VERSION); retrying will not help.",
      drift,
    );
  }

  logger.error("wallet migration: document is too old to migrate", drift);
  throw new HttpsError(
    "failed-precondition",
    `Save schema drift (${seen}): the document is older than the oldest ` +
    `schema this server can migrate (v${MIGRATABLE_SCHEMA_VERSION}). ` +
    "It must be deleted and recreated before it is writable.",
    drift,
  );
}

/**
 * 지갑 문서를 확보한다 — 없으면 세이브의 currency 잔액을 그대로 옮겨 담아 만들고,
 * 있으면 아무것도 쓰지 않고 현재 잔액만 돌려준다. 부트가 부르는 멱등 명령이다.
 *
 * 생성과 세이브 승급(currency 삭제 + schemaVersion)은 **한 트랜잭션**이다 —
 * 갈라 두면 "잔액은 지웠는데 지갑이 없는" 중간 상태에서 유저 재화가 증발한다.
 */
export const ensureWallet = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");

  if (!isKnownEnv(env)) {
    throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  }

  const saveReference = saveDocument(env, uid);
  const walletReference = walletRef(db, env, uid);

  const outcome = await db.runTransaction<EnsureWalletOutcome>(
    async (transaction) => {
      const walletSnapshot = await transaction.get(walletReference);
      if (walletSnapshot.exists) {
        const existing = readWallet(walletSnapshot);
        return {
          created: false,
          revision: 0,
          wallet: {rev: existing.rev, balances: existing.balances},
        };
      }

      const saveSnapshot = await transaction.get(saveReference);
      if (!saveSnapshot.exists) {
        // ensureAccount 가 먼저다. 여기서 세이브를 만들면 스타터 지급이 두 곳으로 갈린다.
        throw new HttpsError(
          "failed-precondition",
          "Save document does not exist. Call ensureAccount first.",
        );
      }

      const current = saveSnapshot.data() ?? {};
      assertMigratableSchema(current.schemaVersion, env, uid);

      const now = FieldValue.serverTimestamp();
      // 패치의 schemaVersion 은 항상 SCHEMA_VERSION 이다 — 이 트랜잭션을 통과한 문서는
      // v7 이었든 v8 이었든 반드시 v8 로 남는다.
      const migration = migrateFromSaveSlot(
        current, FieldValue.delete(), SCHEMA_VERSION);
      const created = createWallet(
        transaction, walletReference, migration.balances, "walletCreate:migration",
        {kind: "boot", txId: "walletCreate:migration"}, undefined, now);

      const revision = Number(current.revision ?? 0) + 1;
      transaction.update(saveReference, {
        ...migration.slotPatch,
        revision,
        updatedAt: now,
      });

      return {
        created: true,
        revision,
        wallet: {rev: created.rev, balances: created.balances},
      };
    });

  if (!outcome.created) {
    logger.info("ensureWallet noop", {uid, env, rev: outcome.wallet.rev});
    // revision 을 **싣지 않는다**. 클라는 revision > 0 을 "이 명령이 세이브를 썼다" 의
    // 센티널로 쓰고, 0/누락이면 세이브 채택을 건너뛴다. 아무것도 안 쓴 호출이 revision 을
    // 실어 보내면 클라가 갱신되지 않은 슬롯을 채택 경로에 태운다.
    return {created: false, wallet: outcome.wallet};
  }

  logger.info("ensureWallet migrated", {
    uid, env,
    revision: outcome.revision,
    rev: outcome.wallet.rev,
    balances: outcome.wallet.balances,
  });

  // 세이브를 썼으니 revision 이 오른다. updatedSlots 는 비어 있다 —
  // 이관은 currency 슬롯을 **지우는** 것이라 채택할 새 슬롯 값이 없다.
  return {
    created: true,
    revision: outcome.revision,
    updatedSlots: {},
    wallet: outcome.wallet,
  };
});
