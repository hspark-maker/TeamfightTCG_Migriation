using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Functions;

/// <summary>Firebase Functions로 서버 검증 명령을 호출한다. 인증 대기·타임아웃·메인 스레드 복귀를 여기서 책임진다.</summary>
internal sealed class FunctionsCallableService : ICallableService
{
    // FirebaseFunctions.DefaultInstance는 us-central1을 가리킨다 — 리전을 지정하지 않으면 배포된 함수를 못 찾고 404가 난다.
    const string FUNCTIONS_REGION = "asia-northeast3";

    readonly string m_emulatorOrigin;
    FirebaseFunctions m_functions;

    /// <summary>빈 origin이면 배포된 함수를, 값이 있으면 그 에뮬레이터를 향한다.</summary>
    internal FunctionsCallableService(string _emulatorOrigin)
    {
        m_emulatorOrigin = string.IsNullOrEmpty(_emulatorOrigin) ? string.Empty : _emulatorOrigin;
    }

    /// <summary>명령을 호출하고 응답을 지정한 타입으로 역직렬화한다. 타임아웃 0이면 기본 예산을 쓴다.</summary>
    public async UniTask<TResponse> InvokeAsync<TResponse>(
        string _commandName, object _request, int _timeoutMilliseconds = 0) where TResponse : class
    {
        if (string.IsNullOrWhiteSpace(_commandName))
            throw new ArgumentException("Callable command name is empty.", nameof(_commandName));

        await WaitForAuthenticationAsync();

        Dictionary<string, object> t_payload = CallablePayload.ToPrimitiveMap(_request);
        int t_timeout = _timeoutMilliseconds > 0 ? _timeoutMilliseconds : FirebaseTimeouts.CallableMilliseconds;
        bool t_reauthenticated = false;

        while (true)
        {
            try
            {
                object t_data = await CallOnceAsync(_commandName, t_payload, t_timeout);

                // 완료 컨텍스트가 HttpClient 스레드다 — 호출부가 세이브·UI를 만지므로 반환 전에 반드시 돌아온다.
                await UniTask.SwitchToMainThread();
                return CallablePayload.ToResponse<TResponse>(t_data);
            }
            catch (Exception t_exception) when (!t_reauthenticated && IsUnauthenticated(t_exception))
            {
                // 일시적인 토큰 갱신 실패가 곧장 세션 차단 모달이 되지 않도록 딱 1회만 재인증하고 다시 태운다.
                t_reauthenticated = true;
                await UniTask.SwitchToMainThread();
                await FirebaseAuthService.Instance.InitializeAsync();
            }
        }
    }

    /// <summary>캐시한 Functions 인스턴스를 비운다. 다음 호출에서 다시 해석된다.</summary>
    internal void Shutdown()
    {
        m_functions = null;
    }

    static async UniTask WaitForAuthenticationAsync()
    {
        if (FirebaseAuthService.Instance.IsCurrentUserActive) return;

        // DelayType.Realtime이 아니면 씬 로드 정지 구간이 첫 프레임 델타에 통째로 실려 예산이 한 프레임에 소진된다(PlayerSaveCloud 실측).
        // 시간이 다 돼도 던지지 않고 그대로 호출한다 — ping은 미인증이어도 답하도록 설계돼 있어 "인증이 원인"인지 진단하려면
        // 호출이 나가야 하고, 쓰기 명령은 서버가 unauthenticated로 정확히 거절하므로 클라가 미리 끊으면 원인만 흐려진다.
        await UniTask.WhenAny(
            FirebaseAuthService.Instance.InitializeAsync(),
            UniTask.Delay(FirebaseTimeouts.AuthAndReadMilliseconds, DelayType.Realtime));
    }

    static bool IsUnauthenticated(Exception _exception)
    {
        return _exception.GetBaseException() is FunctionsException t_functionsException &&
               t_functionsException.ErrorCode == FunctionsErrorCode.Unauthenticated;
    }

    async UniTask<object> CallOnceAsync(string _commandName, Dictionary<string, object> _payload, int _timeoutMilliseconds)
    {
        HttpsCallableReference t_reference = ResolveCallable(_commandName);
        Task<HttpsCallableResult> t_callTask = t_reference.CallAsync(_payload);

        bool t_hasResult;
        HttpsCallableResult t_result;
        try
        {
            // SDK 내장 HttpClient 타임아웃(70초)까지 기다리면 유저 표면이 죽는다 — 여기서 먼저 끊는다.
            // 세션 수명에도 묶는다 — 안 묶으면 에디터 정리가 Firestore TerminateAsync 에서 이 왕복을
            // 기다리다 못 끝내고, gRPC 네이티브 스레드가 남아 Unity가 "Reloading Domain"에서 멈춘다.
            (t_hasResult, t_result) = await UniTask.WhenAny(
                t_callTask.AsUniTask(),
                UniTask.Delay(_timeoutMilliseconds, DelayType.Realtime))
                .AttachExternalCancellation(FirebaseManager.Lifetime);
        }
        catch (OperationCanceledException)
        {
            Abandon(t_callTask);
            throw;
        }

        if (!t_hasResult)
        {
            Abandon(t_callTask);
            throw new TimeoutException($"Callable '{_commandName}' timed out.");
        }

        return t_result?.Data;
    }

    /// <summary>CallAsync가 CancellationToken을 받지 않아 버려진 호출을 취소할 수단이 없다 — 취소 대신
    /// 관측만 붙여 나중에 faulted로 끝나도 UnobservedTaskException으로 새지 않게 한다.</summary>
    static void Abandon(Task _callTask)
        => _callTask.ContinueWith(
            _abandoned => { _ = _abandoned.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

    HttpsCallableReference ResolveCallable(string _commandName)
    {
        // FirebaseManager.Initialize가 의존성 픽스를 기다리지 않고 넘어가므로 그 시점의 DefaultInstance는 보장되지 않는다.
        // 그래서 생성자·모듈 초기화가 아니라 첫 호출까지 해석을 미룬다.
        if (m_functions == null)
            m_functions = FirebaseFunctions.GetInstance(FirebaseApp.DefaultInstance, FUNCTIONS_REGION);

        // GetInstance가 돌려주는 인스턴스는 프로세스 전역 캐시라 다른 경로가 origin을 덮을 수 있다 — 매 호출 다시 적용한다.
        // GetHttpsCallable이 그 자리에서 URL을 굳히므로 반드시 그보다 먼저여야 한다.
        if (!string.IsNullOrEmpty(m_emulatorOrigin))
            m_functions.UseFunctionsEmulator(m_emulatorOrigin);

        return m_functions.GetHttpsCallable(_commandName);
    }
}
