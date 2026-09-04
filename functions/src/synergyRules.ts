export type SynergyEffectRule = {
  type: string;
  parameters: Readonly<Record<string, number>>;
};

export type SynergyTierRule = {
  tierIndex: number;
  requiredCount: number;
  effects: readonly SynergyEffectRule[];
};

export type SynergyRuleSet = ReadonlyMap<string, readonly SynergyTierRule[]>;

type Row = Record<string, unknown>;

// readSpecRows는 같은 payloadHash 세대에서 동일한 rows 배열 객체를 돌려주고,
// blob 갱신이나 clearSpecCache 뒤에는 새 배열을 만든다. 배열 참조를 키로 삼으면
// 별도 TTL 없이 실제 스펙 세대와 함께 파싱 결과가 교체되고, 낡은 세대는 GC될 수 있다.
const parsedRulesCache = new WeakMap<
  readonly Row[],
  WeakMap<readonly Row[], WeakMap<readonly Row[], SynergyRuleSet>>
>();

const EFFECT_PARAMETERS: Readonly<Record<string, readonly string[]>> = {
  Brand: ["damagePerMember"],
  Stat: ["bonusHp", "grantedKeywords", "dmgReduction"],
  Caretaker: ["amount"],
  Flow: ["amount"],
  Legacy: ["amount"],
  Predator: ["lifestealPercent"],
  Trace: ["grantMarkOnAttack", "bonusHpOnMarkedKill"],
};

const KEYWORD_FLAGS: Readonly<Record<string, number>> = {
  Ranged: 1, Peerless: 2, Execution: 4, Taunt: 8, Cunning: 16,
  Mark: 32, Healer: 64, Invincible: 128, BonusHp: 256, Immortal: 512,
};

function text(row: Row, key: string): string {
  const value = row[key];
  if (typeof value !== "string" || value.trim() === "") throw new Error(`synergy_${key}_missing`);
  return value.trim();
}

function integer(row: Row, key: string, min: number): number {
  const value = typeof row[key] === "number" ? row[key] : Number(row[key]);
  if (!Number.isSafeInteger(value) || value < min) throw new Error(`synergy_${key}_invalid`);
  return value;
}

function parseParameters(effectType: string, raw: unknown): Readonly<Record<string, number>> {
  const allowed = EFFECT_PARAMETERS[effectType];
  if (allowed == null) throw new Error(`synergy_effect_type_unsupported:${effectType}`);
  const result: Record<string, number> = {};
  const source = raw == null ? "" : String(raw).trim();
  if (source === "") return result;
  for (const token of source.split(";")) {
    const pair = token.trim();
    if (pair === "") continue;
    const separator = pair.indexOf("=");
    if (separator <= 0 || separator === pair.length - 1) {
      throw new Error(`synergy_parameter_shape:${pair}`);
    }
    const key = pair.slice(0, separator).trim();
    const rawValue = pair.slice(separator + 1).trim();
    if (!allowed.includes(key)) throw new Error(`synergy_parameter_unsupported:${effectType}.${key}`);
    let value: number;
    if (key === "grantedKeywords") {
      value = 0;
      for (const name of rawValue.split(/[|/]/).map((item) => item.trim()).filter(Boolean)) {
        const flag = KEYWORD_FLAGS[name];
        if (flag == null) throw new Error(`synergy_keyword_unsupported:${name}`);
        value |= flag;
      }
    } else if (key === "grantMarkOnAttack" && (rawValue === "true" || rawValue === "false")) {
      value = rawValue === "true" ? 1 : 0;
    } else {
      value = Number(rawValue);
    }
    if (!Number.isSafeInteger(value) || value < 0) {
      throw new Error(`synergy_parameter_invalid:${effectType}.${key}`);
    }
    if (result[key] != null) throw new Error(`synergy_parameter_duplicate:${effectType}.${key}`);
    result[key] = value;
  }
  const required = effectType === "Trace" ? ["grantMarkOnAttack"] :
    effectType === "Stat" ? [] : EFFECT_PARAMETERS[effectType];
  for (const key of required) {
    if (result[key] == null) throw new Error(`synergy_parameter_missing:${effectType}.${key}`);
  }
  if (effectType === "Stat" &&
      result.bonusHp == null && result.grantedKeywords == null && result.dmgReduction == null) {
    throw new Error("synergy_parameter_missing:Stat");
  }
  return result;
}

export function parseSynergyRules(
  definitionRows: readonly Row[],
  tierRows: readonly Row[],
  effectRows: readonly Row[],
): SynergyRuleSet {
  const definitions = new Set<string>();
  for (const row of definitionRows) {
    const id = text(row, "synergyId");
    if (definitions.has(id)) throw new Error(`synergy_definition_duplicate:${id}`);
    definitions.add(id);
  }
  if (definitions.size === 0) throw new Error("synergy_definitions_empty");

  const tiers = new Map<string, {tierIndex: number; requiredCount: number; effects: SynergyEffectRule[]}[]>();
  const tierKeys = new Set<string>();
  for (const row of tierRows) {
    const synergyId = text(row, "synergyId");
    const tierIndex = integer(row, "tierIndex", 0);
    const requiredCount = integer(row, "requiredCount", 1);
    if (!definitions.has(synergyId)) throw new Error(`synergy_tier_definition_missing:${synergyId}`);
    const key = `${synergyId}:${tierIndex}`;
    if (tierKeys.has(key)) throw new Error(`synergy_tier_duplicate:${key}`);
    tierKeys.add(key);
    const list = tiers.get(synergyId) ?? [];
    list.push({tierIndex, requiredCount, effects: []});
    tiers.set(synergyId, list);
  }

  const effectOrders = new Map<string, Set<number>>();
  const effectTypes = new Map<string, Set<string>>();
  for (const row of effectRows) {
    const synergyId = text(row, "synergyId");
    const tierIndex = integer(row, "tierIndex", 0);
    const effectOrder = integer(row, "effectOrder", 0);
    const effectType = text(row, "effectType");
    const key = `${synergyId}:${tierIndex}`;
    const tier = tiers.get(synergyId)?.find((item) => item.tierIndex === tierIndex);
    if (tier == null) throw new Error(`synergy_effect_tier_missing:${key}`);
    const orders = effectOrders.get(key) ?? new Set<number>();
    if (orders.has(effectOrder)) throw new Error(`synergy_effect_order_duplicate:${key}:${effectOrder}`);
    orders.add(effectOrder);
    effectOrders.set(key, orders);
    const types = effectTypes.get(key) ?? new Set<string>();
    if (types.has(effectType)) throw new Error(`synergy_effect_type_duplicate:${key}:${effectType}`);
    types.add(effectType);
    effectTypes.set(key, types);
    tier.effects[effectOrder] = {type: effectType, parameters: parseParameters(effectType, row.parameters)};
  }

  for (const synergyId of definitions) {
    const list = tiers.get(synergyId);
    if (list == null || list.length === 0) throw new Error(`synergy_tiers_missing:${synergyId}`);
    list.sort((a, b) => a.tierIndex - b.tierIndex);
    for (let index = 0; index < list.length; index++) {
      const tier = list[index];
      if (tier.tierIndex !== index) throw new Error(`synergy_tier_gap:${synergyId}:${index}`);
      if (index > 0 && tier.requiredCount <= list[index - 1].requiredCount) {
        throw new Error(`synergy_required_count_order:${synergyId}:${index}`);
      }
      let effectGap = tier.effects.length === 0;
      for (let order = 0; order < tier.effects.length; order++) {
        if (tier.effects[order] == null) effectGap = true;
      }
      if (effectGap) {
        throw new Error(`synergy_effect_gap:${synergyId}:${index}`);
      }
    }
  }
  return tiers;
}

export function parseSynergyRulesCached(
  definitionRows: readonly Row[],
  tierRows: readonly Row[],
  effectRows: readonly Row[],
): SynergyRuleSet {
  let byTierRows = parsedRulesCache.get(definitionRows);
  if (byTierRows == null) {
    byTierRows = new WeakMap();
    parsedRulesCache.set(definitionRows, byTierRows);
  }
  let byEffectRows = byTierRows.get(tierRows);
  if (byEffectRows == null) {
    byEffectRows = new WeakMap();
    byTierRows.set(tierRows, byEffectRows);
  }
  let rules = byEffectRows.get(effectRows);
  if (rules == null) {
    rules = parseSynergyRules(definitionRows, tierRows, effectRows);
    byEffectRows.set(effectRows, rules);
  }
  return rules;
}

export function resolveSynergyTier(
  rules: SynergyRuleSet,
  synergyId: string,
  count: number,
): SynergyTierRule | null {
  const tiers = rules.get(synergyId);
  if (tiers == null) throw new Error(`synergy_rule_missing:${synergyId}`);
  let active: SynergyTierRule | null = null;
  for (const tier of tiers) {
    if (tier.requiredCount > count) break;
    active = tier;
  }
  return active;
}
