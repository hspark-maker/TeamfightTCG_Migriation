import {onDocumentWritten} from "firebase-functions/v2/firestore";
import {FieldValue} from "firebase-admin/firestore";
import {db, DATABASE_ID} from "../firebaseApp";
import {isKnownEnv} from "../save/environments";
import {readSpecRows} from "../packs/packSpecReader";
import {evaluateGuideProgress} from "../missions/guideProgress";
import {missionsRef, readMissions} from "../missions/missionStore";

/** 직접 저장한 덱도 기록한다. 이벤트 순서/중복과 무관하게 최고 진행도만 합친다. */
export const syncGuideProgress = onDocumentWritten({
  document: "envs/{env}/users/{uid}/save/current", database: DATABASE_ID,
}, async (event) => {
  const {env, uid} = event.params;
  const after = event.data?.after;
  if (!isKnownEnv(env) || !after?.exists) return;
  const current = after.data() ?? {};
  const before = event.data?.before.data();
  if (before && ["deck", "ownership", "cardGrowth", "adventure"].every((slot) =>
    JSON.stringify(before[slot]) === JSON.stringify(current[slot]))) return;
  const cards = await readSpecRows(env, "Card");
  if (!cards.length) throw new Error("Guide card spec is unavailable.");
  const reference = missionsRef(db, env, uid);
  await db.runTransaction(async (transaction) => {
    const state = readMissions(await transaction.get(reference));
    const progress = evaluateGuideProgress(current, cards, state.progress);
    if (JSON.stringify(progress) !== JSON.stringify(state.progress)) {
      transaction.set(reference, {progress, updatedAt: FieldValue.serverTimestamp()}, {merge: true});
    }
  });
});
