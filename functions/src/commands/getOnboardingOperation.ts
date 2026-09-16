import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv, requireUid, saveDocument, assertWritableSchema} from "../save/saveDocument";
import {isClientReceiptId} from "../save/receiptId";
import {onboardingFingerprint, onboardingResult} from "../save/onboardingOperation";
import {rejectDomain} from "../save/domainReject";
import {readWallet, walletRef} from "../currency/walletStore";
import {readMissionCatalog} from "../missions/missionSpec";
import {applyPeriodReset, missionResponse, missionsRef, readMissions} from "../missions/missionStore";
import {missionPeriod} from "../missions/period";

/** 불변 명령 결과와 일관된 현재 상태를 분리하여 조회한다. 쓰기·revision 증가는 없다. */
export const getOnboardingOperation = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  const txId = request.data?.txId;
  const command = String(request.data?.command ?? "");
  const args = request.data?.args;
  if (!isKnownEnv(env) || !isClientReceiptId(txId) || !args || typeof args !== "object" || Array.isArray(args)) {
    throw new HttpsError("invalid-argument", "Valid env, txId, command and args are required.");
  }
  let fingerprint: string;
  try {
    fingerprint = onboardingFingerprint(command, args);
  } catch {
    throw new HttpsError("invalid-argument", "Unsupported onboarding command.");
  }
  const catalog = await readMissionCatalog(env);
  const period = missionPeriod(Date.now());
  const reference = saveDocument(env, uid);
  const operationReference = reference.parent.parent!.collection("onboardingOperations").doc(txId);
  return db.runTransaction(async (transaction) => {
    const [saveSnapshot, walletSnapshot, operationSnapshot, missionSnapshot] = await transaction.getAll(
      reference, walletRef(db, env, uid), operationReference, missionsRef(db, env, uid));
    if (!saveSnapshot.exists || !walletSnapshot.exists) {
      throw new HttpsError("failed-precondition", "Account is not initialized.");
    }
    const save = saveSnapshot.data()!;
    assertWritableSchema(save.schemaVersion, env, uid);
    const wallet = readWallet(walletSnapshot);
    const current = {
      save, wallet: {rev: wallet.rev, balances: wallet.balances},
      missions: missionResponse(applyPeriodReset(readMissions(missionSnapshot), period), period, catalog),
    };
    if (!operationSnapshot.exists) return {found: false, operation: null, current};
    const operation = operationSnapshot.data()!;
    if (operation.command !== command || operation.fingerprint !== fingerprint) {
      rejectDomain("TxIdReused", "Onboarding txId does not match command arguments.", {uid, env, txId, command});
    }
    return {
      found: true,
      operation: {command, txId, result: onboardingResult(JSON.parse(operation.cachedJson))},
      current,
    };
  }, {readOnly: true});
});
