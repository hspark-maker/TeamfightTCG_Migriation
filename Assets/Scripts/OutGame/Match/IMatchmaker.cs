using System.Threading;
using Cysharp.Threading.Tasks;

// 상대를 찾아 오는 창구. 페이크 → 실제 Photon 매칭 교체는 이 구현 하나를 갈아끼우는 것으로 끝난다.
public interface IMatchmaker
{
    // 취소(유저 취소·씬 파괴)·실패면 null. 예외를 던지지 않는 것이 계약이다.
    UniTask<MatchOpponent?> FindOpponentAsync(CancellationToken _ct);
}
