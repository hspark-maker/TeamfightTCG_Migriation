import {FieldValue} from "firebase-admin/firestore";
import {HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv, mutateSave, requireUid, SaveMutation} from "../save/saveDocument";
import {isClientReceiptId} from "../save/receiptId";
import {rejectDomain} from "../save/domainReject";
import {CurrencyGain, grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {GrantedItems, grantRewardItems, loadItemGrantContext} from "../rewards/itemGrant";
import {rankRef} from "../rank/rankStore";
import {readSpecRows} from "../packs/packSpecReader";
import {parseRewardRows} from "../rewardTable";
import {attendanceDays} from "../attendance/attendanceSpec";
import {
  ATTENDANCE_DAYS, AttendanceResponse, attendanceResponse, judgeAttendanceClaim, readAttendance,
} from "../attendance/attendanceState";
import {attendanceRef} from "../attendance/attendanceStore";
import {missionPeriod} from "../missions/period";
import {readMissionCatalog} from "../missions/missionSpec";
import {applyGuideProgress} from "../missions/guideMutation";
import {beginMissionBump, commitMissionProgress, missionResponse, MissionResponse} from "../missions/missionStore";

export const claimAttendance = onCall(async (request) => {
  const uid = requireUid(request.auth);
  const env = String(request.data?.env ?? "");
  if (!isKnownEnv(env)) throw new HttpsError("invalid-argument", `Unknown env: ${env}`);
  const {txId, dailyKey, cycle, day} = request.data ?? {};
  if (!isClientReceiptId(txId) || typeof dailyKey !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(dailyKey) ||
      !Number.isSafeInteger(cycle) || cycle < 1 || !Number.isInteger(day) || day < 1 || day > ATTENDANCE_DAYS) {
    rejectDomain("AttendanceStale", "Refresh attendance before claiming.", {uid, env});
  }
  // Capture a single server date before I/O; retries cannot change which day this request claims.
  const nowMs = Date.now();
  const period = missionPeriod(nowMs);
  const rows = parseRewardRows(await readSpecRows(env, "Reward"));
  const reward = attendanceDays(rows)[day - 1].reward;
  const itemContext = reward.items.length ? await loadItemGrantContext(env, reward.items) : null;
  const catalog = itemContext ? await readMissionCatalog(env) : [];
  let items: GrantedItems = {slots: {}, cards: [], currencies: []};
  let granted: CurrencyGain[] = [];
  let attendance: AttendanceResponse | undefined;
  let missions: MissionResponse | undefined;
  return mutateSave(env, uid, "claimAttendance", {kind: "client", txId},
    async (current, transaction, wallet, preparePackStatistics): Promise<SaveMutation> => {
      const reference = attendanceRef(db, env, uid);
      const snapshot = await transaction.get(reference);
      const verdict = judgeAttendanceClaim(readAttendance(snapshot.data()), {dailyKey, cycle, day}, nowMs);
      if (!verdict.allow) rejectDomain(verdict.reason, "Refresh attendance before claiming.", {uid, env, dailyKey, cycle, day});
      const rank = itemContext ? await transaction.get(rankRef(db, env, uid)) : null;
      const missionBump = itemContext ? await beginMissionBump(transaction, db, env, uid, period, current) : null;
      items = itemContext ? grantRewardItems(current, reward.items, itemContext, rows, "",
        Number(rank?.data()?.points ?? (current.rank as {points?: number})?.points ?? 0)) :
        {slots: {}, cards: [], currencies: []};
      granted = [...reward.currencies, ...items.currencies];
      await preparePackStatistics(items.packs?.length ?? 0);
      if (missionBump && itemContext) {
        applyGuideProgress(missionBump, current, items.slots, itemContext.cards, catalog);
        commitMissionProgress(transaction, missionBump, FieldValue.serverTimestamp());
        missions = missionResponse(missionBump.state, period, catalog);
      }
      transaction.set(reference, {...verdict.state, schemaVersion: 1, updatedAt: FieldValue.serverTimestamp()});
      attendance = attendanceResponse(verdict.state, nowMs);
      return {slots: items.slots, wallet: granted.length ?
        nextWallet(wallet, grant(wallet.balances, granted), "claimAttendance") : undefined};
    },
    (adopted) => ({...adopted, attendance, granted, cards: items.cards, packs: items.packs ?? [],
      ...(missions ? {missions} : {})}));
});
