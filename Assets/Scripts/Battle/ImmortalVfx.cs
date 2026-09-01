using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>불사(Immortal) 키워드 연출의 **순서와 수명**만 소유한다.
///
/// 두 국면이 있다.
/// ① <b>대기</b> — 부활을 아직 안 쓴 카드 위에 표식(<see cref="BattleVfxId.ImmortalAura"/>)이 계속 떠 있다.
///    다른 전투 연출과 달리 1회성이 아니라서 핸들을 여기서 들고 있다가 직접 끈다.
/// ② <b>발동</b> — 평소와 같은 사망 연출 → 카드가 아래에서 위로 디졸브 →
///    <see cref="BattleVfxId.ImmortalRevive"/> 등장. 규칙(부활·체력 복구)은 이미 끝난 뒤라 여기서는 표시만 한다.
///
/// 프리팹·정렬은 BattleVfxLibrary, 시간은 BattleTimingConfig, 디졸브 재료는 카드 프리팹 저작이다.
/// 여기엔 어느 것도 두지 않는다(BrandVolleyVfx·HealVfx와 같은 규약).
///
/// <b>순수 연출 — 상태/RNG 무접촉.</b> 부활 여부의 진실원은 <c>CardInstance.reviveUsed</c> 하나이고
/// 여기서는 그 값을 읽어 표식을 켜고 끌 뿐이다.</summary>
public static class ImmortalVfx
{
    // 작아진 카드를 원래 크기로 펴는 시간. 디졸브 시작과 겹쳐 돌아 별도 정적을 만들지 않는다.
    const float SCALE_BACK_DURATION = 0.18f;

    // 카드 뷰당 대기 표식 1개. 뷰는 풀에서 재사용되므로 **뷰를 키로 잡는다** —
    // CardInstance를 키로 잡으면 같은 슬롯에 다른 카드가 와도 옛 표식이 남는다.
    static readonly Dictionary<CardView, VfxHandle> s_auras = new Dictionary<CardView, VfxHandle>();

    /// <summary>대기 표식을 켜고 끈다. 카드 렌더 시점마다 불려도 안전하다(같은 상태면 아무것도 안 한다).</summary>
    public static void SetAura(CardView _view, bool _on)
    {
        if (_view == null) return;

        bool t_has = s_auras.TryGetValue(_view, out VfxHandle t_handle) && t_handle.Valid && t_handle.Go != null;
        if (_on == t_has) return;

        if (!_on)
        {
            Retire(_view);
            return;
        }

        // 카드에 붙여 따라다니게 한다 — 슬롯 기준으로 두면 카드가 공격하러 나갔을 때 표식만 남는다.
        VfxHandle t_spawned = BattleVfx.SpawnAttachedPersistent(BattleVfxId.ImmortalAura, _view.transform,
                                                                _view.IsEnemySide, _view.VfxSortingLayerId);
        if (!t_spawned.Valid) return;   // 미배선 = 표식 없음(규칙은 그대로 돈다)

        s_auras[_view] = t_spawned;
    }

    /// <summary>부활 연출. 호출 시점에 체력은 이미 복구돼 있다 — 여기서는 죽는 그림을 한 번 보여 주고
    /// 되살아나는 그림으로 잇는다. 기다리는 쪽(연출 큐)이 다음 마디를 이 뒤로 미룬다.</summary>
    public static async UniTask PlayRevive(CardInstance _card)
    {
        CardView t_view = CardView.GetView(_card);
        if (t_view == null) return;

        // 표식은 발동과 동시에 사라진다 — 부활을 다 쓴 카드에 대기 표식이 남으면 한 번 더 살아날 것처럼 읽힌다.
        Retire(t_view);

        // ① 평소와 같은 사망 연출(팝 → 축소하며 알파 0까지 페이드). 길이의 진실원은 DeathDuration 하나다.
        //    자세만 되돌리지 않는다 — 작아진 크기를 아래 ②'가 트윈으로 펴야 스냅이 안 보인다.
        await t_view.PlayDeathAnim(_keepEndPose: true);

        // ②' 디졸브는 **원래 크기**에서 돈다 — 작아진 카드 위에서 훑으면 그림이 안 읽힌다.
        //    즉시 되돌리면 "갑자기 커짐"이라 짧게 트윈으로 편다(디졸브와 겹쳐 진행된다).
        t_view.RestoreSlotPose(SCALE_BACK_DURATION);

        // ② 아래에서 위로 훑는 디졸브. 재료·방향값은 카드 프리팹에 저작돼 있고 여기선 진행만 시킨다.
        //    **기다리지 않고 시작한다** — ③이 디졸브가 끝나기 전에 터져야 해서다.
        UniTask t_dissolve = t_view.PlayImmortalDissolve();

        // ③ 되살아나는 순간. **디졸브 시작과 함께** 터진다 — 녹기 시작하는 그 순간이 발동 지점이라
        //    둘이 붙어야 "터지면서 녹는다"로 읽힌다. ImmortalReviveLead 는 시작 기준 지연이고 0이면 동시다.
        float t_wait = Mathf.Max(0f, GameTiming.Battle.ImmortalReviveLead);
        if (t_wait > 0f)
            await UniTask.Delay((int)(t_wait * 1000)).SuppressCancellationThrow();

        BattleVfx.Play(BattleVfxId.ImmortalRevive, t_view.SlotPosition, t_view.VfxSortingLayerId);

        await t_dissolve;

        float t_hold = GameTiming.Battle.ImmortalReviveHold;
        if (t_hold > 0f)
            await UniTask.Delay((int)(t_hold * 1000)).SuppressCancellationThrow();

        await t_view.RestoreFromImmortalDissolve();
    }

    /// <summary>전투 종료 정리. 표식은 수명이 없어 스스로 반납하지 않는다 —
    /// 여기서 놓지 않으면 다음 판 첫 프레임에 지난 판의 표식이 남는다(LegacyCrownVfx.Clear와 같은 이유).</summary>
    public static void Clear()
    {
        foreach (KeyValuePair<CardView, VfxHandle> t_pair in s_auras)
        {
            VfxHandle t_handle = t_pair.Value;
            if (t_handle.Valid && t_handle.Go != null) t_handle.Go.SetActive(false);
            t_handle.Release();
        }
        s_auras.Clear();
    }

    static void Retire(CardView _view)
    {
        if (!s_auras.TryGetValue(_view, out VfxHandle t_handle)) return;

        if (t_handle.Valid && t_handle.Go != null) t_handle.Go.SetActive(false);
        t_handle.Release();
        s_auras.Remove(_view);
    }
}
