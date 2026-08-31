using Cysharp.Threading.Tasks;

/// <summary>서버 검증 명령을 호출하는 접점. 구현은 Firebase Functions를 탄다.</summary>
public interface ICallableService
{
    /// <summary>명령을 호출하고 응답을 지정한 타입으로 역직렬화한다. 타임아웃 0이면 기본 예산을 쓴다.</summary>
    UniTask<TResponse> InvokeAsync<TResponse>(
        string _commandName, object _request, int _timeoutMilliseconds = 0) where TResponse : class;
}
