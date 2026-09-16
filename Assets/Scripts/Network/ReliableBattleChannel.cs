using System;
using System.Collections.Generic;

/// <summary>In-memory replay window for one battle. Frames use little-endian sequence/ack headers.</summary>
internal sealed class ReliableBattleChannel
{
    public const int MaxPending = 256;
    public const int MaxPayload = 65536;
    const int HeaderSize = 8;

    sealed class Delivery
    {
        public byte[] Payload;
        public Action<byte[]> Callback;
    }

    readonly Dictionary<int, byte[]> pending = new Dictionary<int, byte[]>();
    readonly Dictionary<int, Delivery> buffered = new Dictionary<int, Delivery>();
    bool draining;

    public int Sent { get; private set; }
    public int Received { get; private set; }
    public int Acknowledged { get; private set; }
    public bool HasPending { get { return pending.Count != 0; } }

    public byte[] Enqueue(byte[] payload)
    {
        if (payload == null || payload.Length == 0 || payload.Length > MaxPayload)
            throw new ArgumentException("Invalid battle payload.", "payload");
        if (pending.Count >= MaxPending || Sent == int.MaxValue)
            throw new InvalidOperationException("Battle replay window exhausted.");

        var retained = (byte[])payload.Clone();
        pending.Add(++Sent, retained);
        return Frame(Sent, retained);
    }

    public IEnumerable<byte[]> PendingFrames()
    {
        var snapshot = new List<byte[]>(pending.Count);
        for (int sequence = Acknowledged; sequence < Sent; sequence++)
            snapshot.Add(Frame(sequence + 1, pending[sequence + 1]));
        return snapshot;
    }

    public byte[] AckFrame()
    {
        return Frame(0, null);
    }

    public void Receive(byte[] frame, Action<byte[]> deliver)
    {
        if (frame == null || frame.Length < HeaderSize || frame.Length > HeaderSize + MaxPayload)
            throw new ArgumentException("Invalid battle frame size.", "frame");
        int sequence = ReadInt(frame, 0);
        int ack = ReadInt(frame, 4);
        if (sequence < 0 || ack < 0 || ack > Sent ||
            (sequence == 0 ? frame.Length != HeaderSize : frame.Length == HeaderSize))
            throw new ArgumentException("Invalid battle frame header.", "frame");
        if ((long)sequence - Received > MaxPending)
            throw new InvalidOperationException("Battle receive window exceeded.");
        if (sequence > Received && deliver == null)
            throw new ArgumentException("Battle payload requires a callback.", "deliver");

        while (Acknowledged < ack)
            pending.Remove(++Acknowledged);

        if (sequence > Received && !buffered.ContainsKey(sequence))
        {
            var payload = new byte[frame.Length - HeaderSize];
            Buffer.BlockCopy(frame, HeaderSize, payload, 0, payload.Length);
            buffered.Add(sequence, new Delivery { Payload = payload, Callback = deliver });
        }

        // A callback may synchronously receive more frames. The outer drain owns delivery order.
        if (draining) return;
        draining = true;
        try
        {
            Delivery next;
            while (Received < int.MaxValue && buffered.TryGetValue(Received + 1, out next))
            {
                buffered.Remove(++Received);
                next.Callback(next.Payload);
            }
        }
        finally
        {
            draining = false;
        }
    }

    byte[] Frame(int sequence, byte[] payload)
    {
        var frame = new byte[HeaderSize + (payload == null ? 0 : payload.Length)];
        WriteInt(frame, 0, sequence);
        WriteInt(frame, 4, Received);
        if (payload != null) Buffer.BlockCopy(payload, 0, frame, HeaderSize, payload.Length);
        return frame;
    }

    static int ReadInt(byte[] bytes, int offset)
    {
        return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
    }

    static void WriteInt(byte[] bytes, int offset, int value)
    {
        for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (8 * i));
    }
}
