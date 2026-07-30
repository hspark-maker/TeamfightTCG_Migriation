using UnityEngine.Video;

/// <summary>등장 컷씬 자격 판정의 단일 진실원. 호출부(연출/뷰)는 stage를 직접 비교하지 않고 Resolve만 쓴다.
/// 순수 판정 + 1회성 래치만 다룬다 — 게임 상태(체력/키워드/슬롯)나 MatchRandom은 절대 건드리지 않는다(결정론).</summary>
public static class CardCinematicRules
{
    /// <summary>컷씬을 틀 최소 진화 단계. 등급 시스템 확정되면 여기만 바꾼다.</summary>
    public const int MIN_STAGE = 1;

    /// <summary>이 등장이 컷씬 자격이 되면 클립, 아니면 null. 판정은 여기 한 곳뿐 — 호출부가 stage를 직접 비교하지 말 것.
    /// 자격이 될 때 CardInstance.cinematicShown 래치를 세워, 스왑 복귀 등 재등장에서는 null을 돌려준다(중복 재생 방지).</summary>
    public static VideoClip Resolve(CardInstance _card)
    {
        if (_card == null || _card.data == null) return null;
        if (_card.cinematicShown) return null;                  // 이미 한 번 본 인스턴스
        if (_card.evolutionStage < MIN_STAGE) return null;      // 등급 미달

        VideoClip t_clip = _card.data.appearCinematic;
        if (t_clip == null) return null;                        // 클립 미배정 → 래치도 세우지 않음(나중 배정 시 정상 동작)

        _card.cinematicShown = true;
        return t_clip;
    }

    /// <summary>시네마 공격 연출(1vs1 클로즈업)을 받을 최소 진화 단계.</summary>
    public const int CINEMA_ATTACK_STAGE = 3;

    /// <summary>이 공격이 시네마 연출 대상인가. **3단계 카드의 첫 공격 1회만** —
    /// 등장 컷씬으로 들어온 카드가 처음 치는 순간을 클로즈업으로 보여주고, 이후 공격은 일반 연출로 돌아간다.
    ///
    /// 판정 즉시 래치를 소비하므로 한 공격에 한 번만 호출해야 한다(AttackSequence.PlayCore 한 곳).
    /// 입력은 stage(마스터데이터 파생, 양 클라 동일)와 인스턴스 래치뿐이고 RNG·게임상태를 건드리지 않는다 →
    /// 같은 순서로 공격이 일어나는 두 클라에서 같은 결과가 나온다(연출만 갈릴 뿐 규칙 타임라인은 ResolveHits 공용).</summary>
    public static bool TryConsumeCinemaAttack(CardInstance _attacker)
    {
        if (_attacker == null) return false;
        if (_attacker.evolutionStage < CINEMA_ATTACK_STAGE) return false;
        if (_attacker.cinemaAttackUsed) return false;

        _attacker.cinemaAttackUsed = true;
        return true;
    }
}
