using System;
using System.Collections.Generic;

namespace TeamfightTCG.BattleCore
{
    public enum BattleCommandKind : byte
    {
        Attack = 1,
        Mulligan = 2,
        Surrender = 3,
        AiTakeover = 4,
    }

    /// <summary>서버와 클라이언트가 공유하는 고정 8바이트 명령 계약.
    ///
    /// <para>바이트 레이아웃 — <c>seq(u16 LE) turn(u16 LE) actorOwner(u8) kind(u8) a(s8) b|flags</c>.
    /// 마지막 바이트는 하위 니블이 <see cref="B"/>, 상위 니블이 <see cref="Flags"/>다.</para>
    ///
    /// <para><b>B 니블은 부호 없는 0~15다.</b> 슬롯 번호(0~2)만 들어오므로 음수를 실을 일이 없고,
    /// 서버 디코더(functions/src/battleCommand.ts)도 <c>packed &amp; 0x0f</c>로 부호 없이 읽는다.
    /// 여기서 부호 확장을 하면 같은 바이트를 양쪽이 다르게 해석해 재시뮬이 조용히 갈린다.
    /// 음수가 필요한 값은 <see cref="A"/>(온전한 s8)에 싣는다 — 뮬리건 스킵(-1)이 그 자리다.</para></summary>
    public readonly struct BattleCommand
    {
        public const int RecordSize = 8;

        public readonly ushort Seq;
        public readonly ushort Turn;
        public readonly byte ActorOwner;
        public readonly BattleCommandKind Kind;
        public readonly sbyte A;
        public readonly sbyte B;
        public readonly byte Flags;

        public BattleCommand(ushort _seq, ushort _turn, byte _actorOwner,
            BattleCommandKind _kind, sbyte _a, sbyte _b, byte _flags)
        {
            Seq = _seq;
            Turn = _turn;
            ActorOwner = _actorOwner;
            Kind = _kind;
            A = _a;
            B = _b;
            Flags = (byte)(_flags & 0x0f);
        }

        public void AppendTo(List<byte> _destination)
        {
            if (_destination == null) throw new ArgumentNullException(nameof(_destination));
            _destination.Add((byte)(Seq & 0xff));
            _destination.Add((byte)(Seq >> 8));
            _destination.Add((byte)(Turn & 0xff));
            _destination.Add((byte)(Turn >> 8));
            _destination.Add(ActorOwner);
            _destination.Add((byte)Kind);
            _destination.Add(unchecked((byte)A));
            _destination.Add((byte)(unchecked((byte)B) & 0x0f | (Flags << 4)));
        }

        public static bool TryRead(byte[] _source, int _offset, out BattleCommand _command)
        {
            _command = default;
            if (_source == null || _offset < 0 || _source.Length - _offset < RecordSize)
                return false;

            ushort t_seq = (ushort)(_source[_offset] | _source[_offset + 1] << 8);
            ushort t_turn = (ushort)(_source[_offset + 2] | _source[_offset + 3] << 8);
            byte t_packed = _source[_offset + 7];
            // 부호 확장하지 않는다 — 서버 디코더와 같은 규약(위 B 니블 설명 참조).
            var t_b = (sbyte)(t_packed & 0x0f);
            _command = new BattleCommand(
                t_seq,
                t_turn,
                _source[_offset + 4],
                (BattleCommandKind)_source[_offset + 5],
                unchecked((sbyte)_source[_offset + 6]),
                t_b,
                (byte)(t_packed >> 4));
            return true;
        }
    }
}
