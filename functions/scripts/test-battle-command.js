"use strict";

const assert = require("node:assert/strict");
const {
  BATTLE_COMMAND_RECORD_BYTES,
  MAX_BATTLE_COMMANDS,
  decodeBattleCommands,
  validateBattleCommands,
} = require("../lib/battleCommand.js");

// 클라 BattleCommandLog.Record 와 같은 바이트 레이아웃으로 레코드를 만든다.
// seq(LE u16) turn(LE u16) actorOwner(u8) kind(u8) a(s8) b(low nibble)|flags(high nibble)
function record({seq, turn, actorOwner, kind, a = 0, b = 0, flags = 0}) {
  const buffer = Buffer.alloc(BATTLE_COMMAND_RECORD_BYTES);
  buffer.writeUInt16LE(seq, 0);
  buffer.writeUInt16LE(turn, 2);
  buffer[4] = actorOwner;
  buffer[5] = kind;
  buffer[6] = a < 0 ? 0x100 + a : a;
  buffer[7] = (b & 0x0f) | ((flags & 0x0f) << 4);
  return buffer;
}

const log = (...records) => Buffer.concat(records);

assert.equal(BATTLE_COMMAND_RECORD_BYTES, 8);
assert.equal(MAX_BATTLE_COMMANDS, 1024);

// 정상 로그: 뮬리건 스킵 → 공격 → 파생 공격 → 항복
const healthy = log(
  record({seq: 0, turn: 1, actorOwner: 1, kind: 2, a: -1}),
  record({seq: 1, turn: 1, actorOwner: 0, kind: 1, a: 0, b: 2, flags: 1}),
  record({seq: 2, turn: 2, actorOwner: 0, kind: 1, a: 1, b: 0, flags: 2}),
  record({seq: 3, turn: 3, actorOwner: 1, kind: 3})
);
assert.equal(validateBattleCommands(healthy, 4), null);

// 디코딩이 클라 인코딩을 그대로 되돌리는가
const decoded = decodeBattleCommands(healthy);
assert.equal(decoded.length, 4);
assert.deepEqual(decoded[0], {seq: 0, turn: 1, actorOwner: 1, kind: 2, a: -1, b: 0, flags: 0});
assert.equal(decoded[1].flags, 1);   // cunningSwap
assert.equal(decoded[2].flags, 2);   // derived — 재생 금지 표식
assert.equal(decoded[1].b, 2);       // defenderSlot 이 하위 니블에서 온전히 복원된다

// 길이 불일치
assert.equal(validateBattleCommands(healthy, 3), "command_log_length");
assert.equal(validateBattleCommands(Buffer.alloc(7), 1), "command_log_length");
assert.equal(validateBattleCommands(Buffer.alloc(0), 0), null);

// seq 는 0부터 연속이어야 한다 — 누락·재정렬 차단
assert.match(
  validateBattleCommands(log(
    record({seq: 0, turn: 1, actorOwner: 0, kind: 1}),
    record({seq: 2, turn: 1, actorOwner: 0, kind: 1})
  ), 2),
  /^command_seq:1$/
);
assert.match(
  validateBattleCommands(log(
    record({seq: 1, turn: 1, actorOwner: 0, kind: 1}),
    record({seq: 0, turn: 1, actorOwner: 0, kind: 1})
  ), 2),
  /^command_seq:0$/
);

// 턴 번호는 1 이상
assert.match(
  validateBattleCommands(record({seq: 0, turn: 0, actorOwner: 0, kind: 1}), 1),
  /^command_turn:0$/
);

// actorOwner 는 0/1 만 — 미확정(-1 → 0xff) 은 거절
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 2, kind: 1}), 1),
  /^command_actor:0$/
);
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 0xff, kind: 1}), 1),
  /^command_actor:0$/
);

// 공격: 슬롯 범위와 미정의 flags 비트
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 0, kind: 1, a: 3}), 1),
  /^command_attack:0$/
);
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 0, kind: 1, a: -1}), 1),
  /^command_attack:0$/
);
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 0, kind: 1, b: 3}), 1),
  /^command_attack:0$/
);
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 0, kind: 1, flags: 4}), 1),
  /^command_attack:0$/
);

// 뮬리건: -1(스킵) ~ 2 만, b/flags 는 0
assert.equal(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 1, kind: 2, a: 2}), 1),
  null
);
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 1, kind: 2, a: 3}), 1),
  /^command_mulligan:0$/
);
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 1, kind: 2, flags: 1}), 1),
  /^command_mulligan:0$/
);

// 항복·AI 인수는 페이로드가 비어 있어야 한다
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 0, kind: 3, a: 1}), 1),
  /^command_event:0$/
);
assert.equal(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 0, kind: 4}), 1),
  null
);

// 미정의 kind
assert.match(
  validateBattleCommands(record({seq: 0, turn: 1, actorOwner: 0, kind: 5}), 1),
  /^command_kind:0$/
);

// 상한 길이 로그도 통과한다(잘림 판정은 제출 계층이 한다)
const full = log(...Array.from({length: MAX_BATTLE_COMMANDS}, (_, index) =>
  record({seq: index, turn: 1, actorOwner: index % 2, kind: 1, a: 0, b: 1})));
assert.equal(validateBattleCommands(full, MAX_BATTLE_COMMANDS), null);

console.log("battle-command tests passed");
