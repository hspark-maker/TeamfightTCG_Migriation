using System;
using System.Collections.Generic;
using TeamfightTCG.BattleCore;
using UnityEngine;

/// <summary>Execute(규칙) 안에서 발생하지만 접촉 프레임에 보여야 하는 표시를 잠시 붙잡아 두는 곳.
///
/// resolve/present 뒤집기 이후 규칙은 공격 모션보다 먼저 끝난다. 피해·회복·보호막은 <see cref="BattleEvent"/>로
/// 값이 되어 넘어가지만, 시너지 발동 배너/배지/엠블럼처럼 Unity 오브젝트와 SO를 직접 잡는 표시는 값으로 접히지
/// 않는다. 그것들만 여기 담아 두었다가 AttackSequence가 접촉 프레임에 <see cref="Drain"/>으로 푼다.
///
/// **이건 규칙이 아니다.** 여기 들어가는 것은 게임 상태·RNG를 건드리지 않는 순수 표시여야 한다.
/// 상태를 바꾸는 코드를 여기 담으면 결정론이 연출 타이밍에 다시 묶인다 — 그게 P4가 없앤 문제다.
///
/// 배치 구분은 <see cref="BattleEventStream.Current"/>(캡처 스코프)로 한다. 새 공격의 캡처가 시작됐는데
/// 이전 배치가 남아 있으면 그건 Drain 되지 못한 것이므로 버린다 — 다음 공격 연출로 새지 않게.
public static class BattlePresentationQueue
{
    static readonly List<Action> pending = new List<Action>();
    static object batchOwner;

    /// <summary>지금 표시를 미뤄야 하는가(= 규칙 캡처 중인가).</summary>
    public static bool IsDeferring => BattleEventStream.Current != null;

    /// <summary>캡처 중이면 접촉 프레임까지 미루고, 아니면 즉시 재생한다.
    /// 호출부는 어느 쪽인지 몰라도 된다 — 캡처 밖 경로(오프닝 배치·턴 시작 등)는 기존 동작 그대로다.</summary>
    public static void Run(Action _present)
    {
        if (_present == null) return;

        object t_scope = BattleEventStream.Current;
        if (t_scope == null) { _present(); return; }

        // 스코프가 바뀌었는데 남은 게 있다 = 지난 배치가 재생되지 못했다. 이월시키지 않는다.
        if (!ReferenceEquals(batchOwner, t_scope))
        {
            pending.Clear();
            batchOwner = t_scope;
        }
        pending.Add(_present);
    }

    /// <summary>밀어둔 표시를 순서대로 재생하고 비운다. 호출 지점은 AttackSequence.ResolveHits 하나뿐.
    /// 하나가 던져도 나머지는 재생한다 — 표시 하나 때문에 공격 연출 전체가 끊기면 안 된다.</summary>
    public static void Drain()
    {
        if (pending.Count == 0) { batchOwner = null; return; }

        Action[] t_batch = pending.ToArray();
        pending.Clear();
        batchOwner = null;

        for (int i = 0; i < t_batch.Length; i++)
        {
            try { t_batch[i]?.Invoke(); }
            catch (Exception t_ex) { Debug.LogException(t_ex); }
        }
    }

    /// <summary>전투 정리용. 씬을 벗어날 때 남은 표시가 다음 전투로 새지 않게 한다.</summary>
    public static void Clear()
    {
        pending.Clear();
        batchOwner = null;
    }
}
