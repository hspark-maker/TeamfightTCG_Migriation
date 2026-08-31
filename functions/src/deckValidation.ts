import {createHash} from "node:crypto";
import {LIMIT_BREAK_STAGE_CEILING} from "./growth/enhanceRules";
import {LimitBreakCurve, limitBreakHpBonus} from "./growth/limitBreakTable";

export type CardSnapshot = {
  cardId: number;
  level: number;
  hpBonus: number;
  evolutionStage: number;
  unlockedKeywords: number;
  synergyUnlocked: boolean;
};

export type CardSpecForValidation = {
  id: number;
  maxHp: number;
  keywords: number;
  keywordUnlockLevel: number;
  defaultEvolutionStage: number;
  synergies: string[];
  hp2: number;
  hp3: number;
  hp4: number;
};

export type DeckValidationFailure = {
  ok: false;
  code: string;
  cardId: number;
};

export type DeckValidationResult = {ok: true} | DeckValidationFailure;

export const LOCKED_DECK_SIZE = 6;

/**
 * 저장된 한계돌파 단계가 **코드 천장 이하인데 곡선만 넘을 때**의 실패 코드.
 *
 * 위조가 아니라 표 사고다 — 서버가 이미 지급한 단계를 곡선 축소·행 결손이 뒤늦게 부정한 것이라,
 * 호출부는 이 코드만 rejectLock 이 아닌 unavailable 로 접어야 한다(매치 문서에 rejected 를 박으면
 * 아무 잘못 없는 상대 몫까지 그 매치가 통째로 탄다).
 */
export const LIMIT_BREAK_CURVE_SHRUNK = "limit_break_curve_shrunk";

const BASE_LEVEL = 1;
const MAX_LEVEL = 4;
const FIRST_EVOLUTION_LEVEL = 3;
const SECOND_EVOLUTION_LEVEL = 4;
const MAX_KEYWORD_GROWTH = 10;

const KEYWORD_FLAGS: Readonly<Record<string, number>> = {
  Ranged: 1,
  Peerless: 2,
  Execution: 4,
  Taunt: 8,
  Cunning: 16,
  Mark: 32,
  Healer: 64,
  Invincible: 128,
  BonusHp: 256,
  Immortal: 512,
};

const GROWABLE_KEYWORDS = [
  "Ranged",
  "Peerless",
  "Execution",
  "Taunt",
  "Cunning",
  "Healer",
] as const;

function integer(value: unknown): number | null {
  if (typeof value === "number" && Number.isSafeInteger(value)) return value;
  if (typeof value === "string" && /^-?\d+$/.test(value)) {
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) ? parsed : null;
  }
  return null;
}

function record(value: unknown): Record<string, unknown> | null {
  if (value == null || typeof value !== "object" || Array.isArray(value)) return null;
  return value as Record<string, unknown>;
}

function keywordFlags(value: unknown): number | null {
  const numeric = integer(value);
  if (numeric != null) return numeric;
  if (typeof value !== "string") return null;
  if (value.trim() === "") return 0;

  let flags = 0;
  for (const token of value.split(/[\s,|]+/).filter(Boolean)) {
    const flag = KEYWORD_FLAGS[token];
    if (flag == null) return null;
    flags |= flag;
  }
  return flags;
}

function stringList(value: unknown): string[] | null {
  if (value === "") return [];
  if (value == null) return null;
  if (typeof value !== "string") return null;
  const result: string[] = [];
  const seen = new Set<string>();
  for (const raw of value.split(/[|/]/)) {
    const item = raw.trim();
    if (item === "" || seen.has(item)) continue;
    seen.add(item);
    result.push(item);
  }
  return result;
}

export function parseCardSpecRow(raw: unknown): CardSpecForValidation | null {
  const row = record(raw);
  if (row == null) return null;
  const id = integer(row.id);
  const keywords = keywordFlags(row.keywords);
  const keywordUnlockLevel = integer(row.keywordUnlockLevel);
  const hp2 = integer(row.hp2);
  const hp3 = integer(row.hp3);
  const hp4 = integer(row.hp4);
  if (id == null || id <= 0 || keywords == null ||
      keywordUnlockLevel == null ||
      hp2 == null || hp3 == null || hp4 == null) return null;

  // 아래 셋은 서버 재시뮬레이션용으로 나중에 추가된 열이다. 아직 업로드되지 않은 표가 있으므로
  // **없어도 덱 잠금은 통과시킨다** — 여기서 막으면 구 데이터로는 매치 자체가 성립하지 않는다.
  // 대신 값이 없으면 0 / [] 로 떨어지고, 시뮬레이터가 그걸 보고 재생을 포기한다(makeField).
  // 업로더가 모든 값을 문자열로 쓰고 null 은 빈 문자열이 된다 — 나중에 추가된 열에서 ""를
  // 파싱 실패로 보면 카드 전체가 표에서 사라지고, 원인이 "카드가 없다"로 잘못 보고된다.
  // 여기서는 "" 를 "값 없음"으로 읽고, 정말 필요한지는 소비처(시뮬레이터)가 판정한다.
  const absent = (value: unknown) => value === undefined || value === "";
  const maxHp = absent(row.maxHp) ? 0 : integer(row.maxHp);
  const defaultEvolutionStage = absent(row.defaultEvolutionStage) ? 0 : integer(row.defaultEvolutionStage);
  const synergies = row.synergies === undefined ? [] : stringList(row.synergies);
  if (maxHp == null || maxHp < 0 || defaultEvolutionStage == null ||
      defaultEvolutionStage < 0 || defaultEvolutionStage > 3 || synergies == null) return null;
  return {id, maxHp, keywords, keywordUnlockLevel, defaultEvolutionStage,
    synergies, hp2, hp3, hp4};
}

export function validateDeckShape(snapshots: readonly CardSnapshot[]): string | null {
  if (snapshots.length !== LOCKED_DECK_SIZE) {
    return `deck_size:expected=${LOCKED_DECK_SIZE},got=${snapshots.length}`;
  }
  const cardIds = new Set<number>();
  for (const snapshot of snapshots) {
    if (cardIds.has(snapshot.cardId)) return `duplicate_card:${snapshot.cardId}`;
    cardIds.add(snapshot.cardId);
  }
  // 덱 스냅샷 순서 규약: cardId 오름차순.
  // computeDeckHash는 배열 순서를 그대로 직렬화하므로 정규화가 없으면
  // 같은 덱이 순서만 달라도 다른 해시가 되고, 클라가 임의 순서를 제출할 수 있다.
  for (let i = 1; i < snapshots.length; i++) {
    if (snapshots[i - 1].cardId >= snapshots[i].cardId) {
      return `deck_order:index=${i},cardId=${snapshots[i].cardId}`;
    }
  }
  return null;
}

function fail(code: string, cardId: number): DeckValidationFailure {
  return {ok: false, code, cardId};
}

function savedGrowth(save: Record<string, unknown>, cardId: number):
  {level: number; limitBreak: number} | null {
  const growth = record(save.cardGrowth);
  const entries = growth == null ? null : record(growth.entries);
  const entry = entries == null ? null : record(entries[String(cardId)]);
  if (entry == null) return {level: BASE_LEVEL, limitBreak: 0};
  const level = integer(entry.level);
  const limitBreak = integer(entry.limitBreak);
  if (level == null || limitBreak == null) return null;
  return {level, limitBreak};
}

function keywordGrowthLevels(save: Record<string, unknown>): Record<string, unknown> | null {
  const growth = record(save.keywordGrowth);
  return growth == null ? null : record(growth.levels);
}

function expectedHpBonus(
  spec: CardSpecForValidation,
  level: number,
  limitBreak: number,
  curve: LimitBreakCurve,
  unlockedKeywords: number,
  levels: Record<string, unknown>
): number | null {
  const gains = [0, 0, spec.hp2, spec.hp3, spec.hp4];
  let result = 0;
  for (let current = BASE_LEVEL + 1; current <= Math.min(level, MAX_LEVEL); current++) {
    result += gains[current];
  }
  // 한계돌파 가산도 누적이다 — 위 레벨 루프와 같은 성격이라 곡선의 1..stage 합을 쓴다.
  result += limitBreakHpBonus(curve, limitBreak);

  for (const name of GROWABLE_KEYWORDS) {
    if ((unlockedKeywords & KEYWORD_FLAGS[name]) === 0) continue;
    const key = String(KEYWORD_FLAGS[name]);
    const savedLevel = levels[key] == null ? 0 : integer(levels[key]);
    if (savedLevel == null || savedLevel < 0 || savedLevel > MAX_KEYWORD_GROWTH) return null;
    result += savedLevel;
  }
  return result;
}

// 한계돌파 곡선은 **필수 인자**다. 선택 인자에 하드코딩 폴백을 두면 곡선의 진실원이 다시 둘이 된다
// — 순수 모듈이라 표를 스스로 못 읽으므로 호출부(lockDeck)가 읽어 주입한다.
export function validateDeckSnapshots(
  snapshots: readonly CardSnapshot[],
  specs: ReadonlyMap<number, CardSpecForValidation>,
  rawSave: unknown,
  limitBreak: LimitBreakCurve
): DeckValidationResult {
  const save = record(rawSave);
  if (save == null) return fail("save_shape_invalid", 0);
  const ownership = record(save.ownership);
  const ownedRaw = ownership?.cardIds;
  if (!Array.isArray(ownedRaw)) return fail("ownership_shape_invalid", 0);
  const owned = new Set<number>();
  for (const value of ownedRaw) {
    const id = integer(value);
    if (id == null) return fail("ownership_shape_invalid", 0);
    owned.add(id);
  }
  const levels = keywordGrowthLevels(save);
  if (levels == null) return fail("keyword_growth_shape_invalid", 0);

  for (const snapshot of snapshots) {
    const spec = specs.get(snapshot.cardId);
    if (spec == null) return fail("card_not_found", snapshot.cardId);
    if (!owned.has(snapshot.cardId)) return fail("card_not_owned", snapshot.cardId);

    const growth = savedGrowth(save, snapshot.cardId);
    if (growth == null) return fail("card_growth_shape_invalid", snapshot.cardId);
    // 천장 초과는 **위조**다. 어떤 표로도 그 값이 나올 수 없으므로 지금처럼 덱을 거절한다.
    if (growth.level < BASE_LEVEL || growth.level > MAX_LEVEL ||
        growth.limitBreak < 0 || growth.limitBreak > LIMIT_BREAK_STAGE_CEILING) {
      return fail("saved_growth_out_of_range", snapshot.cardId);
    }
    // 천장 이하인데 곡선만 넘었다 = 표가 깎인 것이다. 갈래를 나눠 호출부가 거절과 표 사고를 구별한다.
    if (growth.limitBreak > limitBreak.maxStage) {
      return fail(LIMIT_BREAK_CURVE_SHRUNK, snapshot.cardId);
    }
    if (snapshot.level !== growth.level) return fail("level_mismatch", snapshot.cardId);

    const unlocked = snapshot.level >= spec.keywordUnlockLevel ? spec.keywords : 0;
    const hpBonus = expectedHpBonus(spec, snapshot.level, growth.limitBreak, limitBreak, unlocked, levels);
    if (hpBonus == null) return fail("keyword_growth_out_of_range", snapshot.cardId);
    if (snapshot.hpBonus !== hpBonus) return fail("hp_bonus_mismatch", snapshot.cardId);

    const evolution = snapshot.level >= SECOND_EVOLUTION_LEVEL ? 2 :
      snapshot.level >= FIRST_EVOLUTION_LEVEL ? 1 : 0;
    if (snapshot.evolutionStage !== evolution) return fail("evolution_mismatch", snapshot.cardId);
    if (snapshot.unlockedKeywords !== unlocked) return fail("keywords_mismatch", snapshot.cardId);
    if (snapshot.synergyUnlocked !== (snapshot.level >= FIRST_EVOLUTION_LEVEL)) {
      return fail("synergy_mismatch", snapshot.cardId);
    }
  }
  return {ok: true};
}

// 배열 순서를 그대로 직렬화한다. 호출 전에 validateDeckShape로
// cardId 오름차순을 강제해야 한다 — 클라(NetworkGameController.ComputeDeckHash)와
// 바이트 레이아웃(4 + n*24, 빅엔디안)이 일치해야 하고 순서도 같은 규약을 따른다.
export function computeDeckHash(snapshots: readonly CardSnapshot[]): string {
  const buffer = Buffer.alloc(4 + snapshots.length * 24);
  buffer.writeInt32BE(snapshots.length, 0);
  let offset = 4;
  for (const snapshot of snapshots) {
    buffer.writeInt32BE(snapshot.cardId, offset);
    buffer.writeInt32BE(snapshot.level, offset + 4);
    buffer.writeInt32BE(snapshot.hpBonus, offset + 8);
    buffer.writeInt32BE(snapshot.evolutionStage, offset + 12);
    buffer.writeInt32BE(snapshot.unlockedKeywords, offset + 16);
    buffer.writeInt32BE(snapshot.synergyUnlocked ? 1 : 0, offset + 20);
    offset += 24;
  }
  return createHash("sha256").update(buffer).digest("hex");
}
