import {achievementCount, AchievementState} from "../achievements/achievementProgress";
import {SYNERGY_ID} from "../achievements/achievementCatalog";
import {albumScopeCardIds, AlbumEntryRow, AlbumThemeRow, isCompleted, lockedThemeIds} from "../completionTable";
import {readOwnedIds} from "../packs/packSlots";

export interface BattleStatistics {
  battles: number;
  wins: number;
  losses: number;
  draws: number;
  cardsDestroyed: number;
  attacks: number;
  damageDealt: number;
  healed: number;
  synergyTriggers: number;
  currentWinStreak: number;
  bestWinStreak: number;
}

export interface PlayerStatistics {
  revision: number;
  trackedSinceMs: number;
  lifetime: {
    wins: number; cardsDestroyed: number; currentWinStreak: number; bestWinStreak: number;
    packsOpened: number; albumsCompleted: number; synergyPlays: Record<string, number>;
  };
  battle: {all: BattleStatistics; ranked: BattleStatistics; adventure: BattleStatistics};
}

export interface StatisticsState extends PlayerStatistics {
  // Temporary compatibility watermark. Old functions may still update achievements during rollout/rollback.
  legacyProgress: Record<string, number>;
  legacyRevision: number;
}

const add = (left: number, right: number): number =>
  Math.min(Number.MAX_SAFE_INTEGER, achievementCount(left) + achievementCount(right));
const record = (value: unknown): Record<string, unknown> =>
  value != null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};

export function emptyBattleStatistics(): BattleStatistics {
  return {battles: 0, wins: 0, losses: 0, draws: 0, cardsDestroyed: 0, attacks: 0,
    damageDealt: 0, healed: 0, synergyTriggers: 0, currentWinStreak: 0, bestWinStreak: 0};
}

function readBattle(value: unknown): BattleStatistics {
  const result = emptyBattleStatistics();
  const source = record(value);
  for (const key of Object.keys(result) as (keyof BattleStatistics)[]) result[key] = achievementCount(source[key]);
  return result;
}

export function readStatistics(data: unknown, legacy: AchievementState, nowMs: number): StatisticsState {
  const source = record(data);
  const lifetime = record(source.lifetime);
  const battle = record(source.battle);
  const synergyPlays: Record<string, number> = {};
  for (const [id, value] of Object.entries(record(lifetime.synergyPlays))) {
    if (SYNERGY_ID.test(id)) synergyPlays[id] = achievementCount(value);
  }
  const state: StatisticsState = {
    revision: achievementCount(source.revision), trackedSinceMs: achievementCount(source.trackedSinceMs) || nowMs,
    lifetime: {
      wins: achievementCount(lifetime.wins), cardsDestroyed: achievementCount(lifetime.cardsDestroyed),
      currentWinStreak: achievementCount(lifetime.currentWinStreak), bestWinStreak: achievementCount(lifetime.bestWinStreak),
      packsOpened: achievementCount(lifetime.packsOpened), albumsCompleted: achievementCount(lifetime.albumsCompleted),
      synergyPlays,
    },
    battle: {all: readBattle(battle.all), ranked: readBattle(battle.ranked), adventure: readBattle(battle.adventure)},
    legacyProgress: Object.fromEntries(Object.entries(record(source.legacyProgress))
      .map(([key, value]) => [key, achievementCount(value)])),
    legacyRevision: achievementCount(source.legacyRevision),
  };
  // Import positive increments from an old writer, never infer historical losses or battle denominators.
  const delta = (key: string): number =>
    Math.max(0, achievementCount(legacy.progress[key]) - achievementCount(state.legacyProgress[key]));
  state.lifetime.wins = add(state.lifetime.wins, delta("WinBattle"));
  state.lifetime.cardsDestroyed = add(state.lifetime.cardsDestroyed, delta("DestroyCards"));
  state.lifetime.packsOpened = add(state.lifetime.packsOpened, delta("OpenPack"));
  state.lifetime.bestWinStreak = Math.max(state.lifetime.bestWinStreak, achievementCount(legacy.progress.WinStreak));
  state.lifetime.albumsCompleted = Math.max(state.lifetime.albumsCompleted, achievementCount(legacy.progress.CompleteAlbum));
  for (const key of Object.keys(legacy.progress)) {
    if (key.startsWith("PlaySynergy:") && SYNERGY_ID.test(key.slice(12))) {
      const id = key.slice(12);
      state.lifetime.synergyPlays[id] = add(state.lifetime.synergyPlays[id] ?? 0, delta(key));
    }
  }
  if (data == null || legacy.revision !== state.legacyRevision) {
    state.lifetime.currentWinStreak = legacy.currentWinStreak;
    // A legacy writer has no mode/outcome history. Do not bridge streaks across an unknown interval.
    if (data != null) {
      for (const bucket of Object.values(state.battle)) bucket.currentWinStreak = 0;
    }
  }
  state.legacyRevision = legacy.revision;
  return state;
}

export function statisticsProgress(state: PlayerStatistics): Record<string, number> {
  const l = state.lifetime;
  // Omit zero keys to preserve the legacy wire format. Counters only increase.
  return Object.fromEntries(Object.entries({WinBattle: l.wins, DestroyCards: l.cardsDestroyed,
    WinStreak: l.bestWinStreak, OpenPack: l.packsOpened, CompleteAlbum: l.albumsCompleted,
    ...Object.fromEntries(Object.entries(l.synergyPlays).map(([id, count]) => [`PlaySynergy:${id}`, count])),
  }).filter(([, count]) => count > 0));
}

export function projectAchievements(state: PlayerStatistics, legacy: AchievementState): AchievementState {
  return {...legacy, progress: statisticsProgress(state), currentWinStreak: state.lifetime.currentWinStreak};
}

export function statisticsResponse(state: StatisticsState): PlayerStatistics & {achievementRevision: number} {
  return {revision: state.revision, trackedSinceMs: state.trackedSinceMs, achievementRevision: state.legacyRevision,
    lifetime: {...state.lifetime, synergyPlays: {...state.lifetime.synergyPlays}},
    battle: {all: {...state.battle.all}, ranked: {...state.battle.ranked}, adventure: {...state.battle.adventure}}};
}

export interface StatisticsBattleResult {
  verified: boolean; tutorial: boolean; mode: "ranked" | "adventure"; won: boolean; draw: boolean;
  destroyed: number; attacks: number; damageDealt: number; healed: number; synergyTriggers: number; synergies: string[];
}

export function applyStatisticsBattle(state: StatisticsState, result: StatisticsBattleResult): void {
  if (!result.verified || result.tutorial) return;
  const won = result.won && !result.draw;
  const l = state.lifetime;
  l.wins = add(l.wins, won ? 1 : 0);
  l.cardsDestroyed = add(l.cardsDestroyed, result.destroyed);
  l.currentWinStreak = won ? add(l.currentWinStreak, 1) : 0;
  l.bestWinStreak = Math.max(l.bestWinStreak, l.currentWinStreak);
  for (const id of new Set(result.synergies)) {
    if (SYNERGY_ID.test(id)) l.synergyPlays[id] = add(l.synergyPlays[id] ?? 0, 1);
  }
  for (const b of [state.battle.all, state.battle[result.mode]]) {
    b.battles = add(b.battles, 1);
    b.wins = add(b.wins, won ? 1 : 0);
    b.losses = add(b.losses, !won && !result.draw ? 1 : 0);
    b.draws = add(b.draws, result.draw ? 1 : 0);
    b.cardsDestroyed = add(b.cardsDestroyed, result.destroyed);
    b.attacks = add(b.attacks, result.attacks);
    b.damageDealt = add(b.damageDealt, result.damageDealt);
    b.healed = add(b.healed, result.healed);
    b.synergyTriggers = add(b.synergyTriggers, result.synergyTriggers);
    b.currentWinStreak = won ? add(b.currentWinStreak, 1) : 0;
    b.bestWinStreak = Math.max(b.bestWinStreak, b.currentWinStreak);
  }
}

export function applyStatisticsPacks(state: StatisticsState, opened: number): void {
  state.lifetime.packsOpened = add(state.lifetime.packsOpened, opened);
}

export function applyStatisticsAlbums(
  state: StatisticsState, save: Record<string, unknown>, entries: AlbumEntryRow[], themes: AlbumThemeRow[],
): void {
  const owned = new Set(readOwnedIds(save.ownership));
  const locked = lockedThemeIds(themes);
  const completed = [...new Set(themes.filter((theme) => !theme.locked).map((theme) => theme.themeId))]
    .filter((themeId) => isCompleted(albumScopeCardIds(entries, {kind: "theme", themeId}, locked), owned)).length;
  state.lifetime.albumsCompleted = Math.max(state.lifetime.albumsCompleted, completed);
}
