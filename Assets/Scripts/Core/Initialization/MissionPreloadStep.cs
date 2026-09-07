using Cysharp.Threading.Tasks;

/// <summary>미션 봉투를 초기화 때 한 번 당겨 둔다 — 화면을 열 때가 아니라.
///
/// 열 때 왕복하면 미션 화면은 매번 빈 목록으로 떴다가 응답이 와야 채워진다. 여기서 미리 받아 두면
/// 화면은 <see cref="MissionManager"/> 캐시로 즉시 그린다.
///
/// **초기화를 기다리게 하지 않는다** — 던져만 두고 다음 스텝으로 넘어간다(required=false 저작과 짝).
/// 미션은 부가 기능이라 왕복 한 번이 로비 진입을 늦출 이유가 없고, 응답이 늦게 와도
/// <see cref="MissionManager.OnChanged"/> 가 열려 있는 화면을 다시 그린다.
/// 실패도 <see cref="MissionCommands.RefreshAsync"/> 안에서 삼켜져 경고 한 줄로 끝난다.
///
/// 인증·환경이 선 뒤여야 하므로 세이브 채택 스텝 뒤에 저작한다.
/// </summary>
public sealed class MissionPreloadStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        MissionCommands.RefreshAsync().Forget();
        return UniTask.CompletedTask;
    }
}
