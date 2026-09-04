using System;
using System.Collections.Generic;

namespace TeamfightTCG.BattleCore
{
    public enum BattleEventKind : byte
    {
        Damage = 1,
        Death = 2,
        Swap = 3,
        Spawn = 4,
        SynergyFired = 5,
        TurnChanged = 6,
        MatchEnded = 7,
        Heal = 8,
        ShieldChanged = 9,
        ShieldBroken = 10,
        Revive = 11,
    }

    [Flags]
    public enum BattleEventFlags : byte
    {
        None = 0,
        Counter = 1 << 0,
        Splash = 1 << 1,
        Enhanced = 1 << 2,
        Deferred = 1 << 3,
        Visible = 1 << 4,
    }

    /// <summary>리졸버 출력. Unity 없이 직렬화할 수 있는 값만 가진다.</summary>
    public readonly struct BattleEvent
    {
        public readonly BattleEventKind Kind;
        public readonly int OwnerIndex;
        public readonly int SlotIndex;
        public readonly int SourceOwnerIndex;
        public readonly int SourceSlotIndex;
        public readonly int Value;
        public readonly BattleEventFlags Flags;

        public BattleEvent(BattleEventKind _kind, int _ownerIndex, int _slotIndex, int _value = 0,
            int _sourceOwnerIndex = -1, int _sourceSlotIndex = -1,
            BattleEventFlags _flags = BattleEventFlags.None)
        {
            Kind = _kind;
            OwnerIndex = _ownerIndex;
            SlotIndex = _slotIndex;
            SourceOwnerIndex = _sourceOwnerIndex;
            SourceSlotIndex = _sourceSlotIndex;
            Value = _value;
            Flags = _flags;
        }
    }

    /// <summary>규칙 코드와 프레젠테이션 사이의 이벤트 수집 seam.</summary>
    public static class BattleEventStream
    {
        [ThreadStatic] static CaptureScope current;

        public static event Action<BattleEvent> Published;

        /// <summary>현재 캡처 스코프. null이면 캡처 밖(= 규칙과 연출이 같은 프레임).
        /// 프레젠테이션 지연 큐가 "이 배치가 어느 공격 것인가"를 가르는 데 쓴다.</summary>
        public static CaptureScope Current => current;

        public static CaptureScope BeginCapture()
        {
            if (current != null) throw new InvalidOperationException("BattleEvent 캡처는 중첩할 수 없다.");
            current = new CaptureScope();
            return current;
        }

        public static void Emit(BattleEvent _event)
        {
            if (current != null) current.Add(_event);
            else Published?.Invoke(_event);
        }

        public sealed class CaptureScope : IDisposable
        {
            readonly List<BattleEvent> events = new List<BattleEvent>();
            bool disposed;

            public IReadOnlyList<BattleEvent> Events => events;
            internal void Add(BattleEvent _event) => events.Add(_event);
            public BattleEvent[] ToArray() => events.ToArray();

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (ReferenceEquals(current, this)) current = null;
            }
        }
    }
}
