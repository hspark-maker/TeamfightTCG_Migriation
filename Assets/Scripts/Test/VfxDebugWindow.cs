using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// SROptions의 VFX 실행 대상. 테스트 씬의 필드·라이브러리 배선을 유지한다.
public class VfxDebugWindow : MonoBehaviour
{
    [Header("Field Views (비우면 씬에서 자동 탐색)")]
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;

    [Header("라이브러리 (비우면 이미 주입된 것 사용)")]
    // 테스트 씬은 GameInitializer/DataLibrary 초기화이 없을 수 있다 → 그때 여기 값으로 직접 주입.
    // 이미 주입돼 있으면 건드리지 않는다.
    [SerializeField] BattleVfxLibrary library;

    public int SourceSlot { get; set; }
    public int TargetSlot { get; set; }
    public int HealAmount { get; set; } = 1;

    public string Status => $"Library: {(BattleVfx.Library != null ? BattleVfx.Library.name : "없음")} / P: {Name(playerFieldView)} / E: {Name(enemyFieldView)}";

    void Start()
    {
        if (this.playerFieldView == null || this.enemyFieldView == null) AutoFindFields();
        if (!BattleVfx.HasLibrary && this.library != null) BattleVfx.SetLibrary(this.library);
    }

    /// <summary>필드 뷰 미배선 시 씬에서 찾는다. owner 0=플레이어 기준으로 갈라 담고,
    /// 판정 불가(카드 미바인딩)면 찾은 순서대로 채운다 — 테스트 도구라 최선 추정으로 충분하다.</summary>
    void AutoFindFields()
    {
        var t_found = FindObjectsByType<BattleFieldView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t_fv in t_found)
        {
            bool t_isPlayer = t_fv.Field != null
                ? t_fv.Field.OwnerIndex == TurnState.LocalOwnerIndex
                : this.playerFieldView == null;

            if (t_isPlayer && this.playerFieldView == null) this.playerFieldView = t_fv;
            else if (this.enemyFieldView == null)           this.enemyFieldView  = t_fv;
        }
    }

    static string Name(BattleFieldView _fv) => _fv != null ? _fv.name : "null";

    CardView Source() => Slot(this.playerFieldView, SourceSlot);
    CardView Target() => Slot(this.enemyFieldView,  TargetSlot);

    static CardView Slot(BattleFieldView _fv, int _index)
    {
        if (_fv == null || _index < 0 || _index >= BattleField.SLOT_COUNT) return null;
        return _fv.GetSlotView(_index);
    }

    public void PlayWorld(BattleVfxId _id)
    {
        if (Source() != null) BattleVfx.Play(_id, Source().BottomCenter, Source().VfxSortingLayerId);
    }

    public void PlayAttached(BattleVfxId _id)
    {
        if (Target() != null) BattleVfx.PlayAttached(_id, Target().transform, Target().IsEnemySide, Target().VfxSortingLayerId);
    }

    public void PlayHealEffect()
    {
        if (Target() != null) Target().PlayHealEffect(HealAmount);
    }

    public void PlayHit()
    {
        if (Target() != null) Target().PlayHitAnim(0.15f, HealAmount, Source()).Forget();
    }

    /// <summary>힐 투사체 버스트. 대상은 **내 필드** 카드들(힐러 = 아군 회복이라 실제 경로와 같은 구도).
    /// _single이면 Target 슬롯 번호에 해당하는 내 필드 카드 하나만.</summary>
    public void PlayHealBurst(bool _single)
    {
        CardView t_src = Source();
        if (t_src == null) return;

        var t_targets = new List<(CardView view, CardInstance card, int amount)>();
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            if (_single && i != TargetSlot) continue;
            if (i == SourceSlot && !_single) continue;   // 힐러 자신은 대상 아님(인게임 규칙과 동일)

            CardView t_view = Slot(this.playerFieldView, i);
            if (t_view != null && t_view.BoundCard != null)
                t_targets.Add((t_view, t_view.BoundCard, HealAmount));
        }

        HealVfx.PlayHealBurst(t_src, t_targets);
    }

}
