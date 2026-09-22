import {createHash} from "node:crypto";
import {FieldPath, FieldValue} from "firebase-admin/firestore";
import {CallableRequest, HttpsError, onCall} from "firebase-functions/v2/https";
import {db} from "../firebaseApp";
import {isKnownEnv, mutateSave, requireUid, saveDocument} from "../save/saveDocument";
import {isClientReceiptId} from "../save/receiptId";
import {rejectDomain} from "../save/domainReject";
import {withCountedTransaction} from "../observability/countedTransaction";
import {measuredCallable} from "../observability/requestMetrics";
import {CurrencyGain, grant} from "../currency/wallet";
import {nextWallet} from "../currency/walletStore";
import {GrantedItems, grantRewardItems, loadItemGrantContext} from "../rewards/itemGrant";
import {parseRewardRows} from "../rewardTable";
import {readSpecRows} from "../packs/packSpecReader";
import {rankRef} from "../rank/rankStore";
import {missionPeriod} from "../missions/period";
import {readMissionCatalog} from "../missions/missionSpec";
import {applyGuideProgress} from "../missions/guideMutation";
import {beginMissionBump, commitMissionProgress, missionResponse, MissionResponse} from "../missions/missionStore";
import {
  isMailId, isMailTime, isRecipientUid, MAIL_CLAIM_BATCH_SIZE, MAIL_PAGE_SIZE,
  MailDocument, mailState, parseMailContent, readMail, validateMailItems,
} from "../mail/mail";
import {claimableMail, mailCollection} from "../mail/mailStore";

function requestScope(request: CallableRequest) {
  const uid = requireUid(request.auth);
  const env = request.data?.env;
  if (typeof env !== "string" || !isKnownEnv(env)) throw new HttpsError("invalid-argument", "Known env required.");
  return {uid, env};
}

export const sendMail = onCall(measuredCallable("sendMail", async (request) => {
  const {uid: adminUid, env} = requestScope(request);
  if (request.auth?.token?.admin !== true) throw new HttpsError("permission-denied", "admin claim required");
  const {uid, mailId} = request.data ?? {};
  if (!isRecipientUid(uid) || !isMailId(mailId)) throw new HttpsError("invalid-argument", "Valid uid and mailId required.");
  const content = parseMailContent(request.data);
  const fingerprint = createHash("sha256").update(JSON.stringify(content)).digest("hex");
  const reference = mailCollection(env, uid).doc(mailId);
  return withCountedTransaction("sendMail", async (tx) => {
    const [existing, recipient] = await tx.getAll(reference, saveDocument(env, uid));
    if (existing.exists) {
      if (existing.data()?.fingerprint !== fingerprint) {
        rejectDomain("MailIdReused", "mailId was used with different content.", {uid, env, mailId, adminUid});
      }
      return {mailId, created: false};
    }
    if (!recipient.exists) throw new HttpsError("not-found", "Recipient save does not exist.");
    if (content.rewards.items.length) {
      const itemContext = await loadItemGrantContext(env, content.rewards.items);
      validateMailItems(content.rewards, itemContext);
    }
    const nowMs = Date.now();
    if (content.expiresAtMs <= nowMs) throw new HttpsError("invalid-argument", "Mail must expire in the future.");
    const mail: MailDocument = {...content, schemaVersion: 1, createdAtMs: nowMs,
      claimedAtMs: null, createdBy: adminUid, fingerprint};
    tx.create(reference, mail);
    return {mailId, created: true};
  });
}));

export const getMailbox = onCall(measuredCallable("getMailbox", async (request) => {
  const {uid, env} = requestScope(request);
  const limit = request.data?.limit ?? 20;
  const cursor = request.data?.cursor;
  if (!Number.isInteger(limit) || limit < 1 || limit > MAIL_PAGE_SIZE ||
      (cursor != null && (!isMailTime(cursor.createdAtMs) || !isMailId(cursor.mailId)))) {
    throw new HttpsError("invalid-argument", `limit must be 1..${MAIL_PAGE_SIZE}; cursor must identify a mail.`);
  }
  const serverNowMs = Date.now();
  let query = mailCollection(env, uid).orderBy("createdAtMs", "desc").orderBy(FieldPath.documentId(), "desc");
  if (cursor != null) query = query.startAfter(cursor.createdAtMs, cursor.mailId);
  const [page, pending] = await Promise.all([query.limit(limit + 1).get(), claimableMail(env, uid, serverNowMs).limit(1).get()]);
  const mails = page.docs.slice(0, limit).map((snapshot) => {
    const mail = readMail(snapshot.data());
    return {mailId: snapshot.id, title: mail.title, body: mail.body, rewards: mail.rewards,
      createdAtMs: mail.createdAtMs, expiresAtMs: mail.expiresAtMs, claimedAtMs: mail.claimedAtMs,
      state: mailState(mail, serverNowMs)};
  });
  const last = mails[mails.length - 1];
  return {mails, serverNowMs, hasClaimable: !pending.empty,
    nextCursor: page.size > limit ? {createdAtMs: last.createdAtMs, mailId: last.mailId} : null};
}));

async function claim(request: CallableRequest, all: boolean) {
  const {uid, env} = requestScope(request);
  const {txId, mailId} = request.data ?? {};
  if (!isClientReceiptId(txId) || (!all && !isMailId(mailId))) {
    throw new HttpsError("invalid-argument", "Valid client txId and mailId required.");
  }
  const command = all ? "claimAllMail" : "claimMail";
  let claimedMailIds: string[] = [];
  let granted: CurrencyGain[] = [];
  let items: GrantedItems = {slots: {}, cards: [], currencies: []};
  let missions: MissionResponse | undefined;
  let hasMore = false;
  const result = await mutateSave(env, uid, command, {kind: "client", txId}, async (current, tx, wallet, preparePacks) => {
    // A fresh selection on each transaction attempt prevents stale batch retries from double granting.
    const nowMs = Date.now();
    const snapshots = all ? (await tx.get(claimableMail(env, uid, nowMs).limit(MAIL_CLAIM_BATCH_SIZE + 1))).docs :
      [await tx.get(mailCollection(env, uid).doc(mailId))];
    hasMore = snapshots.length > MAIL_CLAIM_BATCH_SIZE;
    const selected = snapshots.slice(0, MAIL_CLAIM_BATCH_SIZE);
    if (!selected.length) rejectDomain("MailNothingToClaim", "No claimable mail.", {uid, env});
    const mails = selected.map((snapshot) => {
      if (!snapshot.exists) rejectDomain("MailNotFound", "Mail not found.", {uid, env, mailId: snapshot.id});
      const mail = readMail(snapshot.data());
      if (mail.claimedAtMs !== null) rejectDomain("MailAlreadyClaimed", "Mail already claimed.", {uid, env, mailId: snapshot.id});
      if (mail.expiresAtMs <= nowMs) rejectDomain("MailExpired", "Mail has expired.", {uid, env, mailId: snapshot.id});
      return mail;
    });
    const rewards = mails.flatMap((mail) => mail.rewards.items);
    // Specs are loaded after receipt lookup so lost-response retries also work during spec outages.
    const context = rewards.length ? await loadItemGrantContext(env, rewards) : null;
    const rows = context ? parseRewardRows(await readSpecRows(env, "Reward")) : [];
    const catalog = context ? await readMissionCatalog(env) : [];
    const period = missionPeriod(nowMs);
    const rank = context ? await tx.get(rankRef(db, env, uid)) : null;
    const bump = context ? await beginMissionBump(tx, db, env, uid, period, current) : null;
    if (context) for (const mail of mails) validateMailItems(mail.rewards, context);
    // One combined grant makes duplicates across different mails obey the same ownership snapshot.
    items = context ? grantRewardItems(current, rewards, context, rows, "",
      Number(rank?.data()?.points ?? current.rank?.points ?? 0)) : {slots: {}, cards: [], currencies: []};
    granted = [...mails.flatMap((mail) => mail.rewards.currencies), ...items.currencies];
    await preparePacks(items.packs?.length ?? 0);
    const claimedAtMs = Date.now();
    if (mails.some((mail) => mail.expiresAtMs <= claimedAtMs)) {
      rejectDomain("MailExpired", "Mail expired during claim; refresh the mailbox.", {uid, env});
    }
    // Every read, including pack statistics, must finish before the first write.
    missions = undefined;
    if (bump && context) {
      applyGuideProgress(bump, current, items.slots, context.cards, catalog);
      commitMissionProgress(tx, bump, FieldValue.serverTimestamp());
      missions = missionResponse(bump.state, period, catalog);
    }
    claimedMailIds = selected.map((snapshot) => snapshot.id);
    for (const snapshot of selected) tx.update(snapshot.ref, {claimedAtMs});
    return {slots: items.slots, wallet: granted.length ? nextWallet(wallet, grant(wallet.balances, granted), command) : undefined};
  }, (adopted) => ({...adopted, claimedMailIds, granted, cards: items.cards, packs: items.packs ?? [], hasMore,
    ...(missions ? {missions} : {})}));
  if (!all && (result.claimedMailIds.length !== 1 || result.claimedMailIds[0] !== mailId)) {
    rejectDomain("TxIdReused", "Claim txId was used for another mail.", {uid, env, mailId, txId});
  }
  return result;
}

export const claimMail = onCall(measuredCallable("claimMail", (request) => claim(request, false)));
export const claimAllMail = onCall(measuredCallable("claimAllMail", (request) => claim(request, true)));
