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
}
