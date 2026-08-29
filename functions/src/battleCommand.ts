export const BATTLE_COMMAND_RECORD_BYTES = 8;
export const MAX_BATTLE_COMMANDS = 1024;

// 클라 Assets/Scripts/Battle/BattleCommandLog.cs 와 바이트 레이아웃·의미가 같아야 한다.
// kind: 1=Attack, 2=Mulligan, 3=Surrender, 4=AiTakeover.
// Attack 의 flags: bit0=cunningSwap, bit1=derived.
//
// b 는 마지막 바이트의 하위 니블이고 **부호 없는 0~15** 다(슬롯 번호 0~2 전용).
// 클라 BattleCommand.TryRead 도 부호 확장 없이 읽는다 — 한쪽만 확장하면 같은 바이트를
// 양쪽이 다르게 해석해 재시뮬이 조용히 갈린다. 음수는 a(온전한 s8)에만 싣는다.
//
// 재생 계약: derived(bit1) 는 "플레이어 입력이 아니라 규칙이 스스로 만든 파생 공격"(처형 재공격 등)이다.
// 재시뮬레이터는 이 레코드를 재생하지 말 것 — 같은 규칙이 서버에서도 스스로 만들어내므로
// 재생하면 공격이 이중 적용된다. 대조에는 쓰고 입력으로는 먹이지 않는다.
export type BattleCommand = {
  seq: number;
  turn: number;
  actorOwner: number;
  kind: number;
  a: number;
  b: number;
  flags: number;
};

export function decodeBattleCommands(raw: Buffer): BattleCommand[] {
  const commands: BattleCommand[] = [];
  for (let offset = 0; offset < raw.length; offset += BATTLE_COMMAND_RECORD_BYTES) {
    const packed = raw[offset + 7];
    commands.push({
      seq: raw[offset] | (raw[offset + 1] << 8),
      turn: raw[offset + 2] | (raw[offset + 3] << 8),
      actorOwner: raw[offset + 4],
      kind: raw[offset + 5],
      a: raw[offset + 6] === 0xff ? -1 : raw[offset + 6],
      b: packed & 0x0f,
      flags: packed >> 4,
    });
  }
  return commands;
}

export function validateBattleCommands(raw: Buffer, expectedCount: number): string | null {
  if (raw.length !== expectedCount * BATTLE_COMMAND_RECORD_BYTES) return "command_log_length";
  const commands = decodeBattleCommands(raw);
  for (let i = 0; i < commands.length; i++) {
    const command = commands[i];
    if (command.seq !== i) return `command_seq:${i}`;
    if (command.turn < 1) return `command_turn:${i}`;
    if (command.actorOwner !== 0 && command.actorOwner !== 1) return `command_actor:${i}`;
    if (command.kind === 1) {
      if (command.a < 0 || command.a > 2 || command.b > 2 || (command.flags & ~3) !== 0) {
        return `command_attack:${i}`;
      }
    } else if (command.kind === 2) {
      if ((command.a < -1 || command.a > 2) || command.b !== 0 || command.flags !== 0) {
        return `command_mulligan:${i}`;
      }
    } else if (command.kind === 3 || command.kind === 4) {
      if (command.a !== 0 || command.b !== 0 || command.flags !== 0) {
        return `command_event:${i}`;
      }
    } else {
      return `command_kind:${i}`;
    }
  }
  return null;
}
