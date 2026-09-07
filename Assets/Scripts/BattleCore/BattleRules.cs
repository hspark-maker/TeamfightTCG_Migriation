using System.Collections.Generic;

/// <summary>전투 규칙 술어의 단일 진실원. 규칙(BattleField/AttackProcessor)과 표현(CardView)이
/// **같은 함수**를 부르게 해서 "규칙은 되는데 UI는 막는" 종류의 이중 진실원을 원천 차단한다.
///
/// 여기 있는 판정은 전부 <see cref="CardInstance"/> 기준이다 — data.keywords만 보면
/// 시너지(synergyKeywords)·패시브(runtimeKeywords)가 런타임에 부여한 키워드를 놓친다.</summary>
public static class BattleRules
{
    /// <summary>유효 타깃이 좁혀진 이유. 좁아지지 않았으면 None.
    /// UI가 "왜 못 치는지"를 규칙에 되묻기 위한 값 — 뷰가 우선순위를 다시 판단하지 않게 한다.</summary>
    public enum TargetFilter
    {
        None,           // 전체 적이 유효
        ForcedTarget,   // 지정 타깃(튜토리얼 스크립트) 1장만 유효
        Taunt,          // 도발로 좁혀짐(실제로 제외된 적이 있을 때만)
    }

    /// <summary>이 카드가 도발인가. 마스터 데이터 + 시너지 + 런타임 부여를 모두 포함한다.
    /// 타겟 필터(BattleField.GetValidTargets)와 UI 강조/차단 안내(CardView)가 공유하는 유일한 정의.</summary>
    public static bool IsTaunt(CardInstance _card)
        => _card != null && _card.HasKeyword(CardKeyword.Taunt);

    /// <summary>"공격 가능한 적" 목록의 단일 진실원. 규칙(BattleField.GetValidTargets)·
    /// 표현(CardView.GetValidEnemyViews)·자동공격(생각시간 초과)이 전부 이 함수만 부른다.
    ///
    /// 우선순위 ① 지정 타깃(_forcedTarget) ② 도발 ③ 전체.
    /// ①이 우선 — 튜토리얼 스크립트가 규칙이다. _forcedTarget이 _enemies에 없으면
    /// (다른 진영/이미 사망) 무시하고 ②로 내려간다.
    ///
    /// **전역을 읽지 않는다.** 지정 타깃은 반드시 인자로 받는다 — 이전엔 여기서 직접
    /// 전역 강제 대상을 읽으면 호출부에 안 보이는 입력이 생기고,
    /// 그래서 AI 타깃 선정이 튜토리얼 스크립트에 끌려가는 버그가 났다.
    /// 전역을 해석하는 자리는 Unity 어댑터의 강제 대상 조회 한 곳뿐이다.
    ///
    /// 결정론: 정렬·난수 없음. 반환 순서는 항상 _enemies의 입력 순서 그대로다.</summary>
    public static List<CardInstance> ValidTargets(CardInstance _attacker, IReadOnlyList<CardInstance> _enemies,
                                                  CardInstance _forcedTarget)
        => ValidTargets(_attacker, _enemies, _forcedTarget, out _);

    /// <summary><see cref="ValidTargets(CardInstance, IReadOnlyList{CardInstance}, CardInstance)"/> + 좁혀진 이유.</summary>
    public static List<CardInstance> ValidTargets(CardInstance _attacker, IReadOnlyList<CardInstance> _enemies,
                                                  CardInstance _forcedTarget, out TargetFilter _filter)
    {
        _filter = TargetFilter.None;

        var t_all = new List<CardInstance>();
        if (_enemies == null) return t_all;
        for (int i = 0; i < _enemies.Count; i++)
            if (_enemies[i] != null) t_all.Add(_enemies[i]);

        // ① 지정 타깃이 이 공격자의 적 목록에 있으면 그 하나만 유효(도발보다 우선).
        if (_forcedTarget != null && t_all.Contains(_forcedTarget))
        {
            _filter = TargetFilter.ForcedTarget;
            return new List<CardInstance> { _forcedTarget };
        }

        // ② 도발이 있으면 도발 카드만. 전원 도발이면 좁혀진 게 없으므로 필터 이유는 None 유지
        //    (UI의 "그쪽 아님" 거절 안내가 헛발화하지 않게).
        var t_taunt = t_all.FindAll(IsTaunt);
        if (t_taunt.Count == 0) return t_all;
        if (t_taunt.Count < t_all.Count) _filter = TargetFilter.Taunt;
        return t_taunt;
    }

    /// <summary>이 공격이 규칙상 허용되는가(타깃 선정 백스톱). 뷰 필터를 우회한 입력을 규칙 쪽에서 거른다.
    /// 지정 타깃은 호출부가 명시한다 — 편한 진입점은 <see cref="BattleField.CanAttack"/>.</summary>
    public static bool CanAttack(CardInstance _attacker, CardInstance _target,
                                 IReadOnlyList<CardInstance> _enemies, CardInstance _forcedTarget)
        => _target != null && ValidTargets(_attacker, _enemies, _forcedTarget).Contains(_target);
}
