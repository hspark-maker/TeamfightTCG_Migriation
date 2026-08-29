using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using TeamfightTCG.BattleCore;
using UnityEngine;

/// <summary>재시뮬에 필요한 플레이어 입력만 고정 8바이트 레코드로 보관한다.
///
/// <para><b>재생 계약</b> — Attack 레코드의 flags bit1(<c>derived</c>)은 "플레이어 입력이 아니라
/// 규칙이 스스로 만든 파생 공격"(처형 재공격 등)이라는 뜻이다. 재시뮬레이터는 이 레코드를
/// <b>재생하지 않는다</b> — 같은 규칙이 서버에서도 스스로 만들어내므로, 재생하면 이중 적용된다.
/// 기록은 남기되(양쪽 로그 대조용) 입력으로 먹이지 않는다.</para></summary>
public static class BattleCommandLog
{
    public const int RecordSize = BattleCommand.RecordSize;

    /// <summary>초과하면 로그를 통째로 버리고 서버가 그 매치를 무효 처리한다(= 보상 차단).
    /// 정상 플레이가 여기 닿으면 안 되므로 도달 시 LogError로 관측한다.</summary>
    public const int MaxCommands = 1024;

    static readonly List<byte> s_bytes = new List<byte>(RecordSize * 64);

    public static int Count => s_bytes.Count / RecordSize;
    public static bool IsTruncated { get; private set; }

    /// <summary>AI 인수 이후처럼 "더 기록해도 대조에 쓸 수 없는" 구간에서 기록을 멈춘다.
    /// <see cref="IsTruncated"/>와 달리 <b>이미 쌓인 로그는 유효하다</b>.</summary>
    public static bool IsFrozen { get; private set; }

    public static void Reset()
    {
        s_bytes.Clear();
        IsTruncated = false;
        IsFrozen = false;
    }

    public static void Freeze() => IsFrozen = true;

    public static void RecordAttack(int _actorOwner, int _attackerSlot, int _defenderSlot,
        bool _cunningSwap, bool _derived)
    {
        byte t_flags = 0;
        if (_cunningSwap) t_flags |= 1;
        if (_derived) t_flags |= 2;   // 재생 금지 표식 — 위 재생 계약 참조
        Record(_actorOwner, BattleCommandKind.Attack, _attackerSlot, _defenderSlot, t_flags);
    }

    public static void RecordMulligan(int _actorOwner, int _chosenSlot)
        => Record(_actorOwner, BattleCommandKind.Mulligan, _chosenSlot, 0, 0);

    public static void RecordSurrender(int _actorOwner)
        => Record(_actorOwner, BattleCommandKind.Surrender, 0, 0, 0);

    public static void RecordAiTakeover(int _actorOwner)
        => Record(_actorOwner, BattleCommandKind.AiTakeover, 0, 0, 0);

    static void Record(int _actorOwner, BattleCommandKind _kind, int _a, int _b, byte _flags)
    {
        if (!DeckConfig.IsMultiplayer || IsTruncated || IsFrozen) return;

        // owner가 확정되지 않은 상태(초기화 실패로 MyOwnerIndex가 -1 등)에서 기록하면
        // 양쪽 로그가 갈린다. 조용히 건너뛰면 그 사실이 숨으므로 로그를 명시적으로 무효화한다.
        if (_actorOwner != 0 && _actorOwner != 1)
        {
            Debug.LogError($"[BattleCommandLog] ownerIndex 미확정({_actorOwner}) — 명령 로그를 무효화합니다.");
            IsTruncated = true;
            return;
        }
        if (Count >= MaxCommands)
        {
            Debug.LogError(
                $"[BattleCommandLog] 명령 수가 상한({MaxCommands})에 도달해 로그를 버립니다 — " +
                "이 매치는 서버에서 무효 처리됩니다. 상한 재검토가 필요합니다.");
            IsTruncated = true;
            return;
        }

        var t_command = new BattleCommand(
            (ushort)Count,
            (ushort)Math.Max(0, Math.Min(ushort.MaxValue, TurnRunner.TurnCount)),
            (byte)_actorOwner,
            _kind,
            unchecked((sbyte)_a),
            unchecked((sbyte)_b),
            _flags);
        t_command.AppendTo(s_bytes);
    }

    public static string SerializeBase64() => IsTruncated ? string.Empty : Convert.ToBase64String(s_bytes.ToArray());

    public static string HashHex()
    {
        byte[] t_payload = IsTruncated ? Array.Empty<byte>() : s_bytes.ToArray();
        using SHA256 t_sha = SHA256.Create();
        byte[] t_hash = t_sha.ComputeHash(t_payload);
        var t_builder = new StringBuilder(t_hash.Length * 2);
        foreach (byte t_byte in t_hash) t_builder.Append(t_byte.ToString("x2"));
        return t_builder.ToString();
    }
}
