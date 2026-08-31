"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.MAX_BATTLE_COMMANDS = exports.BATTLE_COMMAND_RECORD_BYTES = void 0;
exports.decodeBattleCommands = decodeBattleCommands;
exports.validateBattleCommands = validateBattleCommands;
exports.BATTLE_COMMAND_RECORD_BYTES = 8;
exports.MAX_BATTLE_COMMANDS = 1024;
function decodeBattleCommands(raw) {
    const commands = [];
    for (let offset = 0; offset < raw.length; offset += exports.BATTLE_COMMAND_RECORD_BYTES) {
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
function validateBattleCommands(raw, expectedCount) {
    if (raw.length !== expectedCount * exports.BATTLE_COMMAND_RECORD_BYTES)
        return "command_log_length";
    const commands = decodeBattleCommands(raw);
    for (let i = 0; i < commands.length; i++) {
        const command = commands[i];
        if (command.seq !== i)
            return `command_seq:${i}`;
        if (command.turn < 1)
            return `command_turn:${i}`;
        if (command.actorOwner !== 0 && command.actorOwner !== 1)
            return `command_actor:${i}`;
        if (command.kind === 1) {
            if (command.a < 0 || command.a > 2 || command.b > 2 || (command.flags & ~3) !== 0) {
                return `command_attack:${i}`;
            }
        }
        else if (command.kind === 2) {
            if ((command.a < -1 || command.a > 2) || command.b !== 0 || command.flags !== 0) {
                return `command_mulligan:${i}`;
            }
        }
        else if (command.kind === 3 || command.kind === 4) {
            if (command.a !== 0 || command.b !== 0 || command.flags !== 0) {
                return `command_event:${i}`;
            }
        }
        else {
            return `command_kind:${i}`;
        }
    }
    return null;
}
//# sourceMappingURL=battleCommand.js.map