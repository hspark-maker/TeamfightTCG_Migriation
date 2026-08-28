import {HttpsError, onCall} from "firebase-functions/v2/https";
import * as logger from "firebase-functions/logger";
import {FieldValue} from "firebase-admin/firestore";
import {db} from "../firebaseApp";
import {
  assertWritableSchema,
  isKnownEnv,
  requireUid,
  saveDocument,
  SCHEMA_VERSION,
  WalletPatch,
} from "../save/saveDocument";
import {migrateFromSaveSlot} from "../currency/walletMigration";
import {createWallet, readWallet, walletRef} from "../currency/walletStore";

/** 트랜잭션이 돌려주는 것. created 가 false 면 아무것도 쓰지 않았고 revision 은 뜻이 없다. */
interface EnsureWalletOutcome {
  created: boolean;
  revision: number;
  wallet: WalletPatch;
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
      assertWritableSchema(current.schemaVersion, env, uid);

      const now = FieldValue.serverTimestamp();
      const migration = migrateFromSaveSlot(
        current, FieldValue.delete(), SCHEMA_VERSION);
      const created = createWallet(
        transaction, walletReference, migration.balances, now);

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
