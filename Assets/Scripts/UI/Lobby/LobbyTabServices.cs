/// <summary>
/// 로비 캔버스가 탭에 넘겨주는 **캔버스 레벨 서비스 묶음**.
///
/// 왜 있나: 드래그 고스트처럼 탭 콘텐츠 위에 떠야 하는 물건은 탭 프리팹 안에 둘 수 없다.
/// 그렇다고 인스펙터로 탭 안쪽 필드를 채우면 그 배선이 **중첩 프리팹 오버라이드**로 남아,
/// 탭 프리팹을 열었을 때 필드가 비어 보이고 "밑에서 뭘 바꿨는지" 오버라이드 마커에 묻힌다.
///
/// 그래서 소유는 로비가 갖고, 넘기는 일은 <see cref="LobbyTabPanel.Initialize"/> 한 지점으로 모은다.
/// 탭 프리팹에는 배선이 남지 않는다.
///
/// 서비스가 늘면 여기에 필드를 추가한다 — 탭마다 다른 주입 경로가 생기지 않게.
/// </summary>
public sealed class LobbyTabServices
{
    public LobbyTabServices(DeckEditDragController _dragController)
    {
        DragController = _dragController;
    }

    /// <summary>덱 편집 드래그. 로비 캔버스의 DragLayer가 소유한다(미배선이면 null — 소비측이 판단).</summary>
    public DeckEditDragController DragController { get; }
}
