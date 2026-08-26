// 종료 콜백 안에서 쓰기를 끝낼 수 있는 저장 매체(로컬 파일처럼 네트워크를 타지 않는 매체)
public interface IBlockingWriteRepository : IRepository
{
    ESaveWriteResult SaveBlocking(string _key, string _value);
}
