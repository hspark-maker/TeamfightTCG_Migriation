"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.Rng = void 0;
exports.simulateBattle = simulateBattle;
const battleCommand_1 = require("./battleCommand");
const MASK64 = (BigInt(1) << BigInt(64)) - BigInt(1);
const GOLDEN = BigInt("0x9e3779b97f4a7c15");
const DECK_SALT = BigInt("0xd1b54a32d192ed03");
const FNV_OFFSET = BigInt("14695981039346656037");
const FNV_PRIME = BigInt("1099511628211");
// 회귀 테스트가 직접 벡터를 대조할 수 있게 노출한다(scripts/test-battle-sim.js).
// 이 RNG 가 C# MatchRandom/DeterministicRandom 과 비트 단위로 같아야 재시뮬레이션이 성립한다.
class Rng {
    constructor(seed) {
        this.draws = 0;
        this.state = seed === BigInt(0) ? GOLDEN : seed & MASK64;
    }
    static mix(value) {
        let z = value & MASK64;
        z = ((z ^ (z >> BigInt(30))) * BigInt("0xbf58476d1ce4e5b9")) & MASK64;
        z = ((z ^ (z >> BigInt(27))) * BigInt("0x94d049bb133111eb")) & MASK64;
        return (z ^ (z >> BigInt(31))) & MASK64;
    }
    static deckSeed(seed, owner) {
        return Rng.mix((seed ^ (DECK_SALT * BigInt(owner + 1))) + GOLDEN);
    }
    next() {
        this.state = (this.state + GOLDEN) & MASK64;
        this.draws++;
        return Rng.mix(this.state);
    }
    range(max) {
        return max <= 1 ? 0 : Number(this.next() % BigInt(max));
    }
}
exports.Rng = Rng;
const SYNERGY_ALIASES = {
    "덩치": "Bulk", "돌보미": "Caretaker", "포식자": "Predator", "흐름": "Flow",
    "유산": "Legacy", "수호자": "Guardian", "비늘": "Scale", "낙인": "Brand",
};
function synergyName(raw) {
    return SYNERGY_ALIASES[raw.trim()] ?? raw.trim();
}
function synergyAmount(name, count) {
    if (name === "Brand" || name === "Flow" || name === "Caretaker")
        return count >= 5 ? 2 : count >= 3 ? 1 : 0;
    if (name === "Bulk")
        return count >= 4 ? 6 : count >= 2 ? 3 : 0;
    if (name === "Scale")
        return count >= 4 ? 2 : count >= 2 ? 1 : 0;
    if (name === "Legacy")
        return count >= 4 ? 2 : count >= 2 ? 1 : 0;
    if (name === "Predator")
        return count >= 4 ? 75 : count >= 2 ? 50 : 0;
    if (name === "Guardian")
        return count >= 2 ? 1 : 0;
    return 0;
}
function has(card, keyword) {
    return ((card.unlocked | card.runtime | card.synergyKeywords) & keyword) !== 0;
}
// boardOrder = 클라가 실제로 셔플한 뒤의 보드 순서(슬롯 0..2 → 대기열). 골든 재생에서 이걸 주면
// 자체 셔플 대신 그 순서를 그대로 놓는다. 셔플 차이와 규칙 차이를 분리해서 보기 위한 것이다 —
// 라이브 정산 경로는 이 값이 없으므로 종전대로 시드에서 셔플한다.
function makeField(owner, deck, specs, seed, boardOrder, _fail) {
    const cards = [];
    for (const snapshot of deck) {
        const spec = specs.get(snapshot.cardId);
        // 두 실패를 갈라야 다음 작업이 정해진다 — 표에 카드가 없는 것과, 있는데 maxHp 열이 비어 있는 것은
        // 원인도 고칠 곳도 다르다.
        if (spec == null) {
            if (_fail != null)
                _fail.reason = `card_spec_missing:${snapshot.cardId}`;
            return null;
        }
        if (!(spec.maxHp > 0)) {
            if (_fail != null)
                _fail.reason = `card_spec_max_hp_missing:${snapshot.cardId}`;
            return null;
        }
        const maxHp = spec.maxHp + snapshot.hpBonus;
        cards.push({ cardId: snapshot.cardId, owner, slot: -1, hp: maxHp, maxHp, baseMaxHp: spec.maxHp,
            bonusHp: 0, evolution: Math.max(spec.defaultEvolutionStage, snapshot.evolutionStage),
            attackCount: 0, reduction: 0, flowBonus: 0, legacyStack: 0,
            unlocked: snapshot.unlockedKeywords, runtime: 0, synergyKeywords: 0,
            synergyEnabled: snapshot.synergyUnlocked, shield: false, reviveUsed: false,
            justSpawned: false, returned: false, revealed: false, everRevealed: false,
            synergies: spec.synergies.map(synergyName), specKeywords: spec.keywords });
    }
    if (boardOrder != null) {
        // 빈 배열 = 클라가 보드 순서를 기록하지 못했다. 여기서 시드 셔플로 떨어뜨리면
        // 확실히 다른 보드가 나오고, 그게 "규칙이 갈렸다"로 보고된다. 명시적으로 실패시킨다.
        if (boardOrder.length === 0) {
            if (_fail != null)
                _fail.reason = `board_order_empty:owner${owner}`;
            return null;
        }
        if (boardOrder.length !== cards.length) {
            if (_fail != null) {
                _fail.reason = `board_order_size:owner${owner}:${boardOrder.length}/${cards.length}`;
            }
            return null;
        }
        const pool = cards.slice();
        const ordered = [];
        for (const cardId of boardOrder) {
            const index = pool.findIndex((card) => card.cardId === cardId);
            if (index < 0) {
                if (_fail != null)
                    _fail.reason = `board_order_card:owner${owner}:${cardId}`;
                return null;
            }
            ordered.push(pool[index]);
            pool.splice(index, 1);
        }
        cards.length = 0;
        cards.push(...ordered);
    }
    else {
        const shuffle = new Rng(Rng.deckSeed(seed, owner));
        for (let i = cards.length - 1; i > 0; i--) {
            const j = shuffle.range(i + 1);
            [cards[i], cards[j]] = [cards[j], cards[i]];
        }
    }
    const field = { owner, slots: [null, null, null], waiting: [], fallen: [], flow: 0, active: [] };
    cards.forEach((card, index) => {
        if (index < 3) {
            card.slot = index;
            card.revealed = true;
            card.everRevealed = true;
            card.justSpawned = has(card, 128 /* Keyword.Invincible */);
            field.slots[index] = card;
        }
        else
            field.waiting.push(card);
    });
    const order = [];
    const counts = new Map();
    for (const card of cards) {
        if (card.synergyEnabled) {
            for (const name of card.synergies) {
                if (!counts.has(name))
                    order.push(name);
                counts.set(name, (counts.get(name) ?? 0) + 1);
            }
        }
    }
    field.active = order.map((name) => ({ name, amount: synergyAmount(name, counts.get(name) ?? 0) }))
        .filter((item) => item.amount > 0);
    for (const card of cards) {
        for (const active of field.active) {
            if (card.synergies.includes(active.name)) {
                if (active.name === "Bulk")
                    card.bonusHp += active.amount;
                if (active.name === "Scale")
                    card.reduction += active.amount;
            }
        }
    }
    for (const card of field.slots)
        if (card != null && belongs(card, field, "Guardian"))
            card.shield = true;
    return field;
}
function belongs(card, field, name) {
    return card.synergyEnabled && card.synergies.includes(name) &&
        field.active.some((item) => item.name === name);
}
function heal(card, amount, overheal = false) {
    if (amount <= 0 || (!overheal && card.hp >= card.maxHp))
        return 0;
    const before = card.hp;
    card.hp = overheal ? card.hp + amount : Math.min(card.maxHp, card.hp + amount);
    return card.hp - before;
}
function damage(card, raw) {
    if (raw <= 0 || card.hp <= 0)
        return 0;
    if (has(card, 128 /* Keyword.Invincible */)) {
        card.runtime &= ~128 /* Keyword.Invincible */;
        return 0;
    }
    if (card.shield) {
        card.shield = false;
        return 0;
    }
    let amount = Math.max(1, raw - card.reduction);
    const before = card.hp + card.bonusHp;
    const absorbed = Math.min(amount, card.bonusHp);
    card.bonusHp -= absorbed;
    amount -= absorbed;
    card.hp = Math.max(0, card.hp - amount);
    return before - card.hp - card.bonusHp;
}
function entered(field, card) {
    for (const active of field.active) {
        if (!belongs(card, field, active.name))
            continue;
        if (active.name === "Guardian")
            card.shield = true;
        if (active.name === "Caretaker") {
            for (const ally of field.slots) {
                if (ally != null && ally.hp > 0 && belongs(ally, field, active.name)) {
                    heal(ally, active.amount);
                    ally.bonusHp += active.amount;
                }
            }
        }
        if (active.name === "Flow") {
            field.flow += active.amount;
            for (const ally of field.slots)
                if (ally != null && ally.hp > 0 && belongs(ally, field, active.name))
                    ally.flowBonus = field.flow;
        }
    }
}
function fill(field) {
    for (let slot = 0; slot < 3 && field.waiting.length > 0; slot++) {
        if (field.slots[slot] == null) {
            const card = field.waiting.shift();
            const cunningReturn = card.returned && has(card, 16 /* Keyword.Cunning */);
            card.returned = false;
            card.slot = slot;
            card.revealed = true;
            card.everRevealed = true;
            field.slots[slot] = card;
            entered(field, card);
            card.justSpawned = has(card, 128 /* Keyword.Invincible */) || cunningReturn;
        }
    }
}
function beginTurn(field) {
    const healers = field.slots.filter((card) => card != null && has(card, 64 /* Keyword.Healer */));
    for (const healer of healers)
        for (const ally of field.slots)
            if (ally != null && ally !== healer)
                heal(ally, 1, true);
    for (const card of field.slots) {
        if (card != null) {
            if (card.justSpawned) {
                card.justSpawned = false;
                continue;
            }
            const legacy = field.active.find((item) => item.name === "Legacy");
            if (legacy != null && belongs(card, field, "Legacy"))
                card.legacyStack += legacy.amount;
        }
    }
}
function endTurn(opposite) {
    opposite.slots.forEach((card) => {
        if (card != null)
            card.shield = false;
    });
    opposite.waiting.forEach((card) => {
        card.shield = false;
    });
}
function removeDead(field) {
    for (let slot = 0; slot < 3; slot++) {
        const card = field.slots[slot];
        if (card == null || card.hp > 0)
            continue;
        for (const active of field.active) {
            if (belongs(card, field, active.name)) {
                if (active.name === "Legacy" && card.legacyStack > 0) {
                    for (const ally of field.slots)
                        if (ally != null && ally !== card && ally.hp > 0)
                            heal(ally, card.legacyStack, true);
                }
            }
        }
        // 불사 키워드. 시너지 Lethal 이 먼저 살릴 기회를 갖고 그래도 죽어 있을 때만 발동한다 —
        // 클라 AttackProcessor.RemoveDead 와 같은 순서다(유산으로 살면 부활을 소비하지 않는다).
        if (card.hp <= 0 && has(card, 512 /* Keyword.Immortal */) && !card.reviveUsed) {
            card.reviveUsed = true;
            card.hp = Math.max(1, Math.floor(card.maxHp / 2));
        }
        if (card.hp > 0)
            continue;
        card.slot = -1;
        field.fallen.push(card.cardId);
        field.slots[slot] = null;
    }
}
function attack(command, fields, rng) {
    const own = fields[command.actorOwner];
    const enemy = fields[1 - command.actorOwner];
    const attacker = own.slots[command.a];
    const defender = enemy.slots[command.b];
    if (attacker == null || defender == null || attacker.hp <= 0 || defender.hp <= 0)
        return { again: false, error: "illegal_attack_slot" };
    if ((command.flags & 2) === 0) {
        const taunts = enemy.slots.filter((card) => card != null && card.hp > 0 && has(card, 8 /* Keyword.Taunt */));
        if (taunts.length > 0 && !has(defender, 8 /* Keyword.Taunt */))
            return { again: false, error: "taunt_target_required" };
    }
    const brand = own.active.find((item) => item.name === "Brand");
    if (brand != null && belongs(attacker, own, "Brand")) {
        const count = own.slots.filter((card) => card != null && card.hp > 0 && belongs(card, own, "Brand")).length;
        damage(defender, Math.min(3, count) * brand.amount);
    }
    const attackDamage = (has(attacker, 8 /* Keyword.Taunt */) ? Math.max(1, Math.floor(attacker.hp / 2)) : attacker.hp) + attacker.flowBonus;
    const counterDamage = (has(defender, 8 /* Keyword.Taunt */) ? Math.max(1, Math.floor(defender.hp / 2)) : defender.hp) + defender.flowBonus;
    const splashDamage = has(attacker, 2 /* Keyword.Peerless */) ? Math.floor(attacker.hp / 2) : 0;
    const takesCounter = defender.hp > 0 && !has(attacker, 1 /* Keyword.Ranged */) && !has(defender, 32 /* Keyword.Mark */);
    const shouldSwap = has(attacker, 16 /* Keyword.Cunning */) && own.waiting.length > 0;
    if (Boolean(command.flags & 1) !== shouldSwap)
        return { again: false, error: "cunning_flag_mismatch" };
    const dealt = damage(defender, attackDamage);
    if (takesCounter)
        damage(attacker, counterDamage);
    if (has(attacker, 2 /* Keyword.Peerless */)) {
        const adjacent = [];
        if (command.b > 0 && enemy.slots[command.b - 1] != null)
            adjacent.push(enemy.slots[command.b - 1]);
        if (command.b < 2 && enemy.slots[command.b + 1] != null)
            adjacent.push(enemy.slots[command.b + 1]);
        if (adjacent.length > 0)
            damage(adjacent[rng.range(adjacent.length)], splashDamage);
    }
    if (attacker.hp > 0 && defender.hp > 0 && attacker.evolution >= 2 && attacker.specKeywords === 0) {
        damage(defender, Math.floor(attacker.baseMaxHp / 2));
    }
    const defenderKilled = defender.hp === 0;
    if (shouldSwap && attacker.hp > 0) {
        const incoming = own.waiting.shift();
        incoming.slot = command.a;
        incoming.revealed = true;
        incoming.everRevealed = true;
        own.slots[command.a] = incoming;
        entered(own, incoming);
        incoming.justSpawned = has(incoming, 128 /* Keyword.Invincible */) || (incoming.returned && has(incoming, 16 /* Keyword.Cunning */));
        incoming.returned = false;
        attacker.returned = true;
        attacker.slot = -1;
        attacker.revealed = false;
        own.waiting.push(attacker);
    }
    removeDead(own);
    removeDead(enemy);
    // [AfterAttack] 포식자 흡혈. C# 은 AttackFlow.RunAfterAttack 이 Execute(=RemoveDead 포함) **뒤**에
    // 돌고 진입부가 `if (!_attacker.IsAlive) return;` 다. 이 순서를 지키지 않으면
    // 반격으로 죽은 포식자가 회복으로 되살아나 슬롯에 남고, 그 뒤 전투가 통째로 갈린다.
    const predator = own.active.find((item) => item.name === "Predator");
    if (predator != null && attacker.hp > 0 && belongs(attacker, own, "Predator")) {
        heal(attacker, Math.floor(dealt * predator.amount / 100));
    }
    fill(own);
    fill(enemy);
    return { again: defenderKilled && attacker.hp > 0 && has(attacker, 4 /* Keyword.Execution */) };
}
function remaining(field) {
    return field.waiting.length + field.slots.filter((card) => card != null).length;
}
function stateHash(fields, draws) {
    let hash = FNV_OFFSET;
    const fold = (value) => {
        const unsigned = value >>> 0;
        for (let i = 0; i < 4; i++) {
            hash ^= BigInt((unsigned >>> (i * 8)) & 0xff);
            hash = (hash * FNV_PRIME) & MASK64;
        }
    };
    const foldCard = (card) => {
        if (card == null) {
            fold(-1);
            return;
        }
        [card.cardId, card.slot, card.owner, card.hp, card.maxHp, card.bonusHp, card.evolution,
            card.attackCount, card.reduction, card.flowBonus, card.legacyStack, card.unlocked,
            card.runtime, card.synergyKeywords, card.synergyEnabled ? 1 : 0, card.shield ? 1 : 0,
            card.reviveUsed ? 1 : 0, card.justSpawned ? 1 : 0, card.returned ? 1 : 0,
            card.revealed ? 1 : 0, card.everRevealed ? 1 : 0].forEach(fold);
    };
    for (const field of fields) {
        fold(field.owner);
        fold(field.flow);
        field.slots.forEach(foldCard);
        fold(field.waiting.length);
        field.waiting.forEach(foldCard);
    }
    fold(draws);
    if (hash === BigInt(0))
        hash = BigInt(1);
    return hash.toString(16).padStart(16, "0");
}
function simulateBattle(input) {
    try {
        const seed = BigInt(`0x${input.seedHex}`);
        const rng = new Rng(seed);
        // 기본값은 "어디서 실패했는지 못 밝혔다"는 뜻이어야 한다. 특정 사유를 기본값으로 두면
        // 사유를 안 채운 실패 경로가 전부 그 이름을 뒤집어써서 원인 추적이 엉뚱한 데로 간다.
        const fail = { reason: "field_build_failed" };
        const a = makeField(0, input.decks[0], input.specs, seed, input.boardOrders?.[0], fail);
        const b = makeField(1, input.decks[1], input.specs, seed, input.boardOrders?.[1], fail);
        if (a == null || b == null)
            return { ok: false, reason: fail.reason };
        const fields = [a, b];
        const firstOwner = rng.range(2);
        const commands = (0, battleCommand_1.decodeBattleCommands)(Buffer.from(input.commandLog, "base64"));
        let activeTurn = 0;
        let activeOwner = firstOwner;
        let expectedDerived = false;
        let expectedDerivedTarget = -1;
        let turnStarted = false;
        const checkpoints = [];
        for (const command of commands) {
            if (command.kind === 2) {
                if (turnStarted || command.actorOwner !== 1 - firstOwner)
                    return { ok: false, reason: "illegal_mulligan" };
                if (command.a >= 0) {
                    const field = fields[command.actorOwner];
                    if (field.slots[command.a] == null || field.waiting.length === 0)
                        return { ok: false, reason: "illegal_mulligan_slot" };
                    const index = rng.range(field.waiting.length);
                    const incoming = field.waiting[index];
                    field.waiting.splice(index, 1);
                    const outgoing = field.slots[command.a];
                    incoming.slot = command.a;
                    incoming.revealed = true;
                    incoming.everRevealed = true;
                    field.slots[command.a] = incoming;
                    incoming.justSpawned = has(incoming, 128 /* Keyword.Invincible */);
                    if (belongs(incoming, field, "Guardian"))
                        incoming.shield = true;
                    outgoing.slot = -1;
                    outgoing.revealed = false;
                    field.waiting.push(outgoing);
                }
                continue;
            }
            if (command.kind === 3) {
                return { ok: true, winnerOwner: 1 - command.actorOwner,
                    remaining: [remaining(a), remaining(b)], finalStateHash: stateHash(fields, rng.draws),
                    drawCount: rng.draws, checkpoints };
            }
            if (command.kind === 4)
                return { ok: false, reason: "ai_takeover_not_authoritative" };
            if (command.kind !== 1)
                return { ok: false, reason: "unsupported_command" };
            const derived = Boolean(command.flags & 2);
            if (!derived) {
                if (expectedDerived)
                    return { ok: false, reason: "missing_derived_attack" };
                if (!turnStarted) {
                    activeTurn = 1;
                    turnStarted = true;
                }
                else {
                    activeOwner = 1 - activeOwner;
                    if (activeOwner === firstOwner)
                        activeTurn++;
                }
                if (command.turn !== activeTurn || command.actorOwner !== activeOwner)
                    return { ok: false, reason: "turn_order_mismatch" };
                beginTurn(fields[activeOwner]);
            }
            else if (!expectedDerived || command.turn !== activeTurn || command.actorOwner !== activeOwner) {
                return { ok: false, reason: "derived_attack_mismatch" };
            }
            if (derived && command.b !== expectedDerivedTarget)
                return { ok: false, reason: "derived_target_mismatch" };
            const resolved = attack(command, fields, rng);
            if (resolved.error != null) {
                // 실패해도 여기까지 모은 체크포인트를 함께 돌려준다 — 규칙 위반이 첫 발산이 아니라
                // 앞선 턴에서 이미 갈린 상태의 증상일 수 있고, 그건 해시 체인으로만 구분된다.
                // 어느 명령에서 규칙이 갈렸는지 없으면 골든 대조가 "실패했다"까지만 알려준다.
                // 양쪽 보드를 같이 실어야 C# 로그와 눈으로 맞출 수 있다.
                const dump = (field) => field.slots
                    .map((card, slot) => card == null ? `${slot}:-` :
                    `${slot}:id${card.cardId}hp${card.hp}kw${card.unlocked | card.runtime | card.synergyKeywords}`)
                    .join(",");
                return { ok: false, checkpoints, reason: `${resolved.error}@seq${command.seq}` +
                        `(turn=${command.turn} actor=${command.actorOwner} a=${command.a} b=${command.b}` +
                        ` flags=${command.flags} own=[${dump(fields[command.actorOwner])}]` +
                        ` enemy=[${dump(fields[1 - command.actorOwner])}])` };
            }
            expectedDerived = resolved.again;
            expectedDerivedTarget = -1;
            if (expectedDerived) {
                const targets = fields[1 - activeOwner].slots
                    .filter((card) => card != null && card.hp > 0);
                if (targets.length === 0)
                    expectedDerived = false;
                else
                    expectedDerivedTarget = targets[rng.range(targets.length)].slot;
            }
            if (!expectedDerived) {
                endTurn(fields[1 - activeOwner]);
                checkpoints.push({ turn: activeTurn, actingOwner: activeOwner,
                    stateHash: stateHash(fields, rng.draws), drawCount: rng.draws });
            }
            if (remaining(a) === 0 || remaining(b) === 0)
                break;
        }
        if (remaining(a) > 0 && remaining(b) > 0)
            return { ok: false, reason: "command_log_incomplete" };
        // 동시 전멸이면 승자가 없다. 여기서 한쪽을 고르면 클라(EBattleLoopEnd.Draw)와 판정이 갈린다.
        const drawn = remaining(a) === 0 && remaining(b) === 0;
        const winnerOwner = drawn ? -1 : remaining(a) > 0 ? 0 : 1;
        return { ok: true, winnerOwner, draw: drawn, remaining: [remaining(a), remaining(b)],
            finalStateHash: stateHash(fields, rng.draws), drawCount: rng.draws, checkpoints };
    }
    catch (error) {
        // 예외 메시지를 그대로 reason 에 담으면 매치 문서(클라가 읽는다)에 내부 경로·스택 조각이 남는다.
        // 진단은 Functions 로그로 하고, 문서에는 고정 토큰만 남긴다.
        console.error("[battleSim] unhandled", error);
        return { ok: false, reason: "internal_error" };
    }
}
//# sourceMappingURL=battleSimulation.js.map