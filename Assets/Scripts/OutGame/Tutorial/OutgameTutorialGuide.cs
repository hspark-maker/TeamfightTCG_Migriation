// "지금 안내가 시키고 있는 일"을 묻는 단일 창구.
// 두 러너(온보딩·트리거)를 합쳐서 보는 이유: 같은 안내가 챕터에서 트리거로 옮겨 다니는데,
// 묻는 쪽(성장 비용·도감 화면·결과 화면)은 그것이 어느 러너의 것인지 알 필요가 없다.
// 안내 구간은 언제나 하나뿐이라(둘이 겹치면 서로를 가로챈다) 먼저 답하는 쪽이 곧 답이다.
public static class OutgameTutorialGuide
{
    public static bool IsCurrentAction(EOutgameTutorialAction _action)
        => OutgameTutorialRunner.IsCurrentAction(_action)
        || TriggeredTutorialRunner.IsCurrentAction(_action);

    /// <summary>지금 안내가 지목한 카드. 도감처럼 같은 종류의 자리가 여럿인 화면이 "어느 칸인가"를 여기서 받는다.
    /// 저작이 비었으면 false — 그때는 화면이 스스로 고른다(안내가 멈추지 않게).</summary>
    public static bool TryGetAnchorCard(out CardData _card)
    {
        _card = TryGetCurrentStep(out var t_step) ? t_step.AnchorCard : null;

        return _card != null;
    }

    /// <summary>지금 서 있는 스텝이 값을 대신 내주는 자리인가(저작 freeOfCharge).</summary>
    public static bool IsCurrentStepFree()
        => TryGetCurrentStep(out var t_step) && t_step.FreeOfCharge;

    /// <summary>지금 서 있는 스텝. 두 러너 중 도는 쪽의 것이다(둘 다 안 돌면 false).
    /// 무료 한 방의 소진 표식처럼 "그 스텝 하나"를 식별해야 하는 쪽도 이 참조를 그대로 쓴다.</summary>
    public static bool TryGetCurrentStep(out TutorialStepDef _step)
        => OutgameTutorialRunner.TryGetCurrentStep(out _step)
        || TriggeredTutorialRunner.TryGetCurrentStep(out _step);
}
