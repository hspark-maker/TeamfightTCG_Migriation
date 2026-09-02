using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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

    // [5] 사망 단계 전용 배치. 히트 배치와 같은 규칙(스코프 단위 · 이월 없음)이고 푸는 시점만 다르다.
    static readonly List<Action> pendingDeath = new List<Action>();
    static object deathBatchOwner;

    // [6] 처치 단계 전용 배치. 위 둘과 달리 **캡처 스코프를 안 본다** — 담는 쪽이 Execute 안이 아니라
    // [4] 공격 후 콜백(이미 캡처가 끝난 자리)이기 때문이다. 대신 담기는 자리와 푸는 자리가 같은
    // ResolveHits 한 호출 안이라(④ → ⑥) 스코프 없이도 배치가 섞이지 않는다.
    // 기다릴 수 있어야 해서 Action이 아니라 Func<UniTask>다 — 처형 글로우처럼 길이를 가진 표시가 들어온다.
    static readonly List<Func<UniTask>> pendingKill = new List<Func<UniTask>>();

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

    /// <summary>[5] 사망 단계까지 미룬다 — 죽는 카드에서 나가는 표시(유산 왕관 비행 등) 전용.
    ///
    /// <see cref="Run"/>과 나누는 이유: 치사 트리거(Lethal)는 규칙상 RemoveDead 안에서 돌아야 하는데
    /// 그건 공격 모션이 시작되기도 전이다. 그 표시를 접촉 프레임(<see cref="Drain"/>)에 풀면
    /// **죽는 그림보다 먼저** 뜬다. 연출 5단계 고정 순서(공격 전 → 공격 중 → 피격 → 공격 후 → 사망)에서
    /// 사망 표시는 마지막이라 여기 담고 <see cref="DrainDeaths"/>가 사망 연출과 같은 박에 푼다.
    ///
    /// 캡처 밖(디버그 강제 사망·턴 밖 정리)이면 즉시 재생 — <see cref="Run"/>과 같은 규약이다.</summary>
    public static void RunOnDeath(Action _present)
    {
        if (_present == null) return;

        object t_scope = BattleEventStream.Current;
        if (t_scope == null) { _present(); return; }

        if (!ReferenceEquals(deathBatchOwner, t_scope))
        {
            pendingDeath.Clear();
            deathBatchOwner = t_scope;
        }
        pendingDeath.Add(_present);
    }

    /// <summary>사망 배치를 순서대로 재생하고 비운다. 호출 지점은 AttackSequence.ResolveHits 하나뿐
    /// (사망 연출 직전). 예외 처리는 <see cref="Drain"/>과 같다.</summary>
    public static void DrainDeaths()
    {
        if (pendingDeath.Count == 0) { deathBatchOwner = null; return; }

        Action[] t_batch = pendingDeath.ToArray();
        pendingDeath.Clear();
        deathBatchOwner = null;

        for (int i = 0; i < t_batch.Length; i++)
        {
            try { t_batch[i]?.Invoke(); }
            catch (Exception t_ex) { Debug.LogException(t_ex); }
        }
    }

    /// <summary>[6] 처치 단계까지 미룬다 — "죽였다"는 사실이 조건인 표시(처형 발동, 표식 처치 보상 배너)만.
    ///
    /// 사망(⑤)과 나누는 이유: 쓰러지는 것은 맞은 쪽 그림이고, 처치는 **때린 쪽** 그림이다.
    /// 한 박에 겹치면 누가 죽었는지와 누가 무엇을 얻었는지가 같이 뭉갠다.
    ///
    /// 조건 판정은 담는 쪽이 한다 — 여기 담겼다는 것 자체가 이미 "처치했다"는 뜻이다.</summary>
    public static void RunOnKill(Func<UniTask> _present)
    {
        if (_present == null) return;
        pendingKill.Add(_present);
    }

    /// <summary>기다릴 것이 없는 처치 표시(배너·배지 pop)용 편의 오버로드.</summary>
    public static void RunOnKill(Action _present)
    {
        if (_present == null) return;
        pendingKill.Add(() => { _present(); return UniTask.CompletedTask; });
    }

    /// <summary>처치 배치를 순서대로 **기다리며** 재생하고 비운다. 호출 지점은 AttackSequence.ResolveHits 하나뿐
    /// (사망 연출 뒤). 하나가 던져도 나머지는 재생한다.</summary>
    public static async UniTask DrainKillsAsync()
    {
        if (pendingKill.Count == 0) return;

        Func<UniTask>[] t_batch = pendingKill.ToArray();
        pendingKill.Clear();

        for (int i = 0; i < t_batch.Length; i++)
        {
            try { await t_batch[i](); }
            catch (Exception t_ex) { Debug.LogException(t_ex); }
        }
    }

    /// <summary>처치 배치를 재생하지 않고 버린다. 결정타로 판이 끝나는 프레임에서 쓴다 —
    /// 그 뒤엔 재공격도 다음 공격도 없어 풀 자리가 영영 오지 않는다.</summary>
    public static void DiscardKills() => pendingKill.Clear();

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
        pendingDeath.Clear();
        deathBatchOwner = null;
        pendingKill.Clear();
    }
}
