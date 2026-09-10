import {onDocumentWritten} from "firebase-functions/v2/firestore";
import {FieldValue} from "firebase-admin/firestore";
import {db, DATABASE_ID} from "../firebaseApp";
import {isKnownEnv} from "../save/environments";
import {readSpecRows} from "../packs/packSpecReader";
import {evaluateGuideProgress} from "../missions/guideProgress";
import {readMissionCatalog} from "../missions/missionSpec";
import {missionsRef, readMissions} from "../missions/missionStore";

/** 직접 저장한 덱도 기록한다. 이벤트 순서/중복과 무관하게 최고 진행도만 합친다. */
export const syncGuideProgress = onDocumentWritten({
  document: "envs/{env}/users/{uid}/save/current", database: DATABASE_ID, retry: true,
}, async (event) => {
  const {env, uid} = event.params;
  const after = event.data?.after;
  if (!isKnownEnv(env) || !after?.exists) return;
  const current = after.data() ?? {};
  const before = event.data?.before.data();
  if (before && ["deck", "ownership", "cardGrowth", "adventure"].every((slot) =>
    JSON.stringify(before[slot]) === JSON.stringify(current[slot]))) return;
  const [cards, catalog] = await Promise.all([readSpecRows(env, "Card"), readMissionCatalog(env)]);
  if (!cards.length) throw new Error("Guide card spec is unavailable.");
  const reference = missionsRef(db, env, uid);
  await db.runTransaction(async (transaction) => {
    const state = readMissions(await transaction.get(reference));
    const progress = evaluateGuideProgress(current, cards, catalog, state.progress);
    // readMissions는 0을 생략한다. 미달성 가이드의 missing과 0은 같은 값이다.
    if (Object.entries(progress).some(([key, value]) => value !== (state.progress[key] ?? 0))) {
      transaction.set(reference, {progress, updatedAt: FieldValue.serverTimestamp()}, {merge: true});
    }
  });
});
