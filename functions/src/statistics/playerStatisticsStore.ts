import type {DocumentReference, DocumentSnapshot, Firestore, Transaction} from "firebase-admin/firestore";
import {isDeepStrictEqual} from "node:util";
import {AchievementState} from "../achievements/achievementProgress";
import {achievementsRef, readAchievements, writeAchievements} from "../achievements/achievementStore";
import {projectAchievements, readStatistics, StatisticsState} from "./playerStatistics";

export function playerStatisticsRef(db: Firestore, env: string, uid: string): DocumentReference {
  return db.doc(`envs/${env}/users/${uid}/statistics/current`);
}

export interface StatisticsContext {
  ref: DocumentReference;
  legacyRef: DocumentReference;
  state: StatisticsState;
  achievements: AchievementState;
  original: unknown;
  exists: boolean;
}

export function statisticsFromSnapshots(
  snapshot: DocumentSnapshot, legacySnapshot: DocumentSnapshot, nowMs: number,
): StatisticsContext {
  const achievements = readAchievements(legacySnapshot);
  const data = snapshot.data();
  const state = readStatistics(data, achievements, nowMs);
  return {ref: snapshot.ref, legacyRef: legacySnapshot.ref, state,
    achievements: projectAchievements(state, achievements), exists: snapshot.exists,
    original: data ?? null};
}

export async function beginStatistics(
  tx: Transaction, db: Firestore, env: string, uid: string,
): Promise<StatisticsContext> {
  const [snapshot, legacy] = await tx.getAll(playerStatisticsRef(db, env, uid), achievementsRef(db, env, uid));
  return statisticsFromSnapshots(snapshot, legacy, Date.now());
}

export function commitStatistics(tx: Transaction, context: StatisticsContext, now: unknown): void {
  const {state} = context;
  context.achievements = projectAchievements(state, context.achievements);
  // The old document remains a projection, not a second producer. Preserve permanent claim markers.
  writeAchievements(tx, context.legacyRef, context.achievements, now);
  state.legacyProgress = {...context.achievements.progress};
  state.legacyRevision = context.achievements.revision;
  state.revision++;
  tx.set(context.ref, {...state, schemaVersion: 1});
}

export function statisticsChanged(context: StatisticsContext): boolean {
  return !context.exists || !isDeepStrictEqual({...context.state, schemaVersion: 1}, context.original);
}
