using Cysharp.Threading.Tasks;

/// <summary>종료 시점의 세이브를 로컬에 동기 기록하고 다음 부팅에 소비하는 매체.
/// 매체가 네트워크라 종료 콜백 안에서 쓰기를 끝낼 수 없을 때만 필요하다.</summary>
public interface ISaveJournalRepository
{
    ESaveWriteResult WriteJournalBlocking(string _payload);

    UniTask<ESaveWriteResult> ConsumeJournalAsync();
}
