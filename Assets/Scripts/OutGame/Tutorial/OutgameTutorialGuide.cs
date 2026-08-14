// "지금 안내가 시키고 있는 일"을 묻는 단일 창구.
// 두 러너(온보딩·트리거)를 합쳐서 보는 이유: 같은 안내가 챕터에서 트리거로 옮겨 다니는데,
// 묻는 쪽(성장 비용·결과 화면)은 그것이 어느 러너의 것인지 알 필요가 없다.
// 안내 구간은 언제나 하나뿐이라(둘이 겹치면 서로를 가로챈다) OR로 충분하다.
public static class OutgameTutorialGuide
{
    public static bool IsCurrentAction(EOutgameTutorialAction _action)
        => OutgameTutorialRunner.IsCurrentAction(_action)
        || TriggeredTutorialRunner.IsCurrentAction(_action);
}
