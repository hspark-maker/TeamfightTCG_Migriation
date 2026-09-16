using System;
using System.Collections.Generic;
using System.Linq;

public static class ReliableBattleChannelTests
{
    static int checks;

    public static string Run()
    {
        checks = 0;
        ReplayAndOrder();
        Ownership();
        ReentrantDelivery();
        Validation();
        RandomizedReconnect();
        return "PASS: ReliableBattleChannel (" + checks + " checks)";
    }

    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { checks++; return; }
        catch (InvalidOperationException) { checks++; return; }
        throw new Exception("Invalid input accepted.");
    }

    static byte[] Packet(int sequence, int ack, params byte[] payload)
    {
        var bytes = new byte[8 + payload.Length];
        for (int i = 0; i < 4; i++)
        {
            bytes[i] = (byte)(sequence >> (8 * i));
            bytes[4 + i] = (byte)(ack >> (8 * i));
        }
        Array.Copy(payload, 0, bytes, 8, payload.Length);
        return bytes;
    }

    static void ReplayAndOrder()
    {
        var sender = new ReliableBattleChannel();
        var receiver = new ReliableBattleChannel();
        var values = new List<byte>();
        Action<byte[]> deliver = p => values.Add(p[0]);
        var first = sender.Enqueue(new byte[] { 1 });
        var second = sender.Enqueue(new byte[] { 2 });
        var third = sender.Enqueue(new byte[] { 3 });
        receiver.Receive(third, deliver);
        receiver.Receive(third, deliver);
        Check(receiver.Received == 0 && values.Count == 0, "Gap delivered prematurely.");
        receiver.Receive(first, deliver);
        var staleAck = receiver.AckFrame();
        sender.Receive(staleAck, null);
        Check(sender.Acknowledged == 1 && sender.PendingFrames().Count() == 2, "Partial ack failed.");
        receiver.Receive(second, deliver);
        Check(values.SequenceEqual(new byte[] { 1, 2, 3 }), "Out-of-order replay failed.");
        foreach (var replay in sender.PendingFrames()) receiver.Receive(replay, deliver);
        receiver.Receive(first, deliver);
        Check(values.Count == 3, "Lost ack replay duplicated delivery.");
        sender.Receive(receiver.AckFrame(), null);
        sender.Receive(staleAck, null);
        Check(sender.Acknowledged == 3 && !sender.HasPending, "Ack regressed or pending not cleared.");
        receiver.Enqueue(new byte[] { 4 });
        var piggyback = receiver.PendingFrames().Single();
        Check(piggyback[4] == 3, "Fresh cumulative ack absent.");
    }

    static void Ownership()
    {
        var sender = new ReliableBattleChannel();
        var receiver = new ReliableBattleChannel();
        var input = new byte[] { 7 };
        var emitted = sender.Enqueue(input);
        input[0] = 8;
        emitted[8] = 9;
        var snapshot = sender.PendingFrames().ToArray();
        Check(snapshot[0][8] == 7, "Caller mutated retained payload.");
        snapshot[0][8] = 10;
        Check(sender.PendingFrames().Single()[8] == 7, "Replay snapshot mutated pending payload.");
        byte observed = 0;
        var outOfOrder = Packet(2, 0, 11);
        receiver.Receive(outOfOrder, p => observed = p[0]);
        outOfOrder[8] = 12;
        receiver.Receive(Packet(1, 0, 1), p => { });
        Check(observed == 11, "Caller mutated buffered incoming payload.");
        var fresh = sender.PendingFrames();
        sender.Receive(Packet(0, 1), null);
        Check(fresh.Single()[8] == 7, "PendingFrames was not a snapshot.");
        var replayOwner = new ReliableBattleChannel();
        replayOwner.Enqueue(new byte[] { 1 });
        replayOwner.Receive(Packet(1, 0, 2), p => { });
        Check(replayOwner.PendingFrames().Single()[4] == 1, "Replay reused stale ack header.");
    }

    static void ReentrantDelivery()
    {
        var channel = new ReliableBattleChannel();
        var events = new List<int>();
        channel.Receive(Packet(1, 0, 1), p =>
        {
            Check(channel.Received == 1, "Receive cursor advanced after callback.");
            events.Add(1);
            channel.Receive(Packet(3, 0, 3), q => events.Add(3));
            channel.Receive(Packet(2, 0, 2), q => events.Add(2));
            channel.Receive(Packet(1, 0, 1), q => events.Add(99));
            events.Add(10);
        });
        Check(events.SequenceEqual(new int[] { 1, 10, 2, 3 }), "Reentrant delivery reordered or lost callbacks.");
        try { channel.Receive(Packet(4, 0, 4), p => { throw new Exception("callback"); }); }
        catch (Exception) { }
        channel.Receive(Packet(5, 0, 5), p => events.Add(5));
        Check(channel.Received == 5 && events.Last() == 5, "Throwing callback stranded drain.");
    }

    static void Validation()
    {
        var channel = new ReliableBattleChannel();
        Reject(() => channel.Enqueue(null));
        Reject(() => channel.Enqueue(new byte[0]));
        Reject(() => channel.Enqueue(new byte[ReliableBattleChannel.MaxPayload + 1]));
        Reject(() => channel.Receive(null, null));
        Reject(() => channel.Receive(new byte[7], null));
        Reject(() => channel.Receive(new byte[ReliableBattleChannel.MaxPayload + 9], null));
        Reject(() => channel.Receive(Packet(-1, 0, 1), p => { }));
        Reject(() => channel.Receive(Packet(0, -1), null));
        Reject(() => channel.Receive(Packet(0, 1), null));
        Reject(() => channel.Receive(Packet(0, 0, 1), null));
        Reject(() => channel.Receive(Packet(1, 0), p => { }));
        Reject(() => channel.Receive(Packet(257, 0, 1), p => { }));
        Reject(() => channel.Receive(Packet(int.MaxValue, 0, 1), p => { }));
        Reject(() => channel.Receive(Packet(1, 0, 1), null));
        var max = channel.Enqueue(new byte[ReliableBattleChannel.MaxPayload]);
        Check(max.Length == ReliableBattleChannel.MaxPayload + 8, "Maximum legal payload rejected.");
        for (int i = 1; i < ReliableBattleChannel.MaxPending; i++) channel.Enqueue(new byte[] { 1 });
        Reject(() => channel.Enqueue(new byte[] { 1 }));
        Check(channel.Sent == 256, "Overflow mutated sent sequence.");
        Reject(() => channel.Receive(Packet(257, 256, 1), p => { }));
        Check(channel.Acknowledged == 0, "Invalid packet mutated ack.");
        channel.Receive(Packet(0, 1), null);
        channel.Enqueue(new byte[] { 1 });
        Check(channel.Sent == 257 && channel.PendingFrames().Count() == 256, "Ack did not free window slot.");
        var exhausted = new ReliableBattleChannel();
        typeof(ReliableBattleChannel).GetField("<Sent>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(exhausted, int.MaxValue);
        Reject(() => exhausted.Enqueue(new byte[] { 1 }));
        Check(exhausted.Sent == int.MaxValue, "Sequence counter wrapped.");
    }

    static void RandomizedReconnect()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            var random = new Random(seed);
            var sender = new ReliableBattleChannel();
            var receiver = new ReliableBattleChannel();
            var delivered = new List<byte>();
            var packets = new List<byte[]>();
            for (int i = 0; i < 200; i++) packets.Add(sender.Enqueue(new byte[] { (byte)i }));
            for (int i = 0; i < 300; i++)
            {
                receiver.Receive(packets[random.Next(packets.Count)], p => delivered.Add(p[0]));
                if (random.Next(4) == 0) sender.Receive(receiver.AckFrame(), null);
            }
            foreach (var replay in sender.PendingFrames()) receiver.Receive(replay, p => delivered.Add(p[0]));
            sender.Receive(receiver.AckFrame(), null);
            Check(delivered.SequenceEqual(Enumerable.Range(0, 200).Select(i => (byte)i)), "Reconnect replay diverged, seed " + seed);
            Check(!sender.HasPending && sender.Acknowledged == 200, "Reconnect ack incomplete, seed " + seed);
        }
    }
}
