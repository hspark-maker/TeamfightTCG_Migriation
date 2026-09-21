import type {DocumentReference, DocumentSnapshot, Firestore, Transaction} from "firebase-admin/firestore";
import {achievementCount, AchievementState} from "./achievementProgress";
import {isAchievementId} from "./achievementCatalog";

export function achievementsRef(db: Firestore, env: string, uid: string): DocumentReference {
  return db.doc(`envs/${env}/users/${uid}/achievements/current`);
}

export function readAchievements(snapshot: DocumentSnapshot): AchievementState {
  const data = snapshot.data() ?? {};
  const progress: Record<string, number> = {};
  const claimed: Record<string, boolean> = {};
  if (data.progress && typeof data.progress === "object") {
    for (const [key, value] of Object.entries(data.progress)) {
      if (/^(WinBattle|DestroyCards|CompleteAlbum|WinStreak|OpenPack|PlaySynergy:[A-Za-z][A-Za-z0-9_]{0,63})$/.test(key)) {
        progress[key] = achievementCount(value);
      }
    }
  }
  if (data.claimed && typeof data.claimed === "object") {
    for (const [key, value] of Object.entries(data.claimed)) {
      if (isAchievementId(key) && value === true) claimed[key] = true;
    }
  }
  return {progress, claimed, currentWinStreak: achievementCount(data.currentWinStreak), revision: achievementCount(data.revision)};
}

export function writeAchievements(
  tx: Transaction, ref: DocumentReference, state: AchievementState, now: unknown,
): void {
  state.revision++;
  tx.set(ref, {...state, schemaVersion: 1, updatedAt: now});
}

export function achievementResponse(state: AchievementState): {
  revision: number; progress: Record<string, number>; claimed: Record<string, boolean>;
} {
  return {revision: state.revision, progress: {...state.progress}, claimed: {...state.claimed}};
}
