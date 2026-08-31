using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 세이브를 건드리는 서버 호출의 유일한 창구.
// 도메인 코드가 ICallableService를 직접 잡으면 "봉인 → 응답 채택 → 해제" 순서를 각자 구현하게 되고,
// 한 곳이라도 채택을 빠뜨리면 문서 revision이 어긋나 그 다음 업로드가 세션을 끊는다. R5~R8이 전부 여기로 들어온다.
internal static class ServerSaveCommands
{
    // 서버가 영수증 번호를 읽는 필드 이름(functions/src/save/receiptId.ts)과 같아야 한다.
    const string TX_ID_FIELD = "txId";

    static ICallableService s_service;
    static UniTaskCompletionSource s_inFlight;

    /// <summary>Firebase 모듈이 서비스를 꽂는다(해제 시 null).</summary>
    internal static void SetService(ICallableService _service)
    {
        s_service = _service;
    }

    /// <summary>세이브를 쓰는 서버 호출. 업로드를 봉인하고 응답의 revision·슬롯을 채택한 뒤 봉인을 푼다.
    /// <paramref name="_pending"/> 를 넘기면 서버 잔액을 채택하기 직전에 낙관 델타를 걷는다.</summary>
    internal static async UniTask<TResponse> InvokeAsync<TResponse>(
        string _commandName, object _request, CurrencyPendingTicket _pending = null)
        where TResponse : ServerCommandResult
    {
        try
        {
            return await RunAsync<TResponse>(_commandName, _request, _pending);
        }
        finally
        {
            // 게이트를 잡기 전에 던지는 갈래(서비스 미배선·상태 불가·페이로드 오류)는 아래 finally에 닿지 않는다.
            // 걷지 못한 델타는 세션이 끝날 때까지 표시 잔액을 어긋나게 두므로 회수를 가장 바깥에 둔다.
            _pending?.Settle();
        }
    }

    static async UniTask<TResponse> RunAsync<TResponse>(
        string _commandName, object _request, CurrencyPendingTicket _pending)
        where TResponse : ServerCommandResult
    {
        ICallableService t_service = RequireService(_commandName);

        // 게이트를 잡기 **전**에 짓는다 — s_inFlight를 세운 뒤 여기서 던지면 finally가 없어 게이트가
        // 영영 안 풀리고, 이후 모든 명령이 예외도 없이 무한 대기한다.
        //
        // 영수증 번호는 호출 하나당 하나다. 화면·세션 단위로 재사용하면 인자가 다른 요청이 첫 응답을
        // 그대로 받는다 — 서버 대조는 명령 이름만 가르고 인자는 가르지 않는다.
        Dictionary<string, object> t_payload = CallablePayload.ToPrimitiveMap(_request);
        t_payload[TX_ID_FIELD] = $"{_commandName}:{Guid.NewGuid():N}";

        // 명령을 직렬화한다 — 겹치면 나중 명령의 SuspendUploadsAsync가 앞선 명령의 기준선을 덮어써,
        // 통화 중에 생긴 로컬 변경이 "이미 서버에 있다"고 잘못 기록되고 영영 업로드되지 않는다.
        // 아래 재시도의 영수증 재생도 이 직렬화에 기댄다: 두 시도 사이에 다른 세이브 쓰기가 끼면
        // 서버가 revision 어긋남을 보고 failed-precondition으로 세션을 되돌린다.
        while (s_inFlight != null)
            await s_inFlight.Task;

        if (!PlayerSaveCloud.CanRunServerCommand)
            throw new InvalidOperationException(
                $"Server command '{_commandName}' is not allowed while the save cloud is {PlayerSaveCloud.State}.");

        UniTaskCompletionSource t_gate = new UniTaskCompletionSource();
        s_inFlight = t_gate;

        await PlayerSaveCloud.SuspendUploadsAsync();
        try
        {
            TResponse t_result = await SendAsync<TResponse>(t_service, _commandName, t_payload);
            if (t_result == null)
                throw new InvalidOperationException($"Server command '{_commandName}' returned nothing.");

            // 지갑이 먼저다 — 단조 판정이라 절대 던지지 않으므로, 뒤의 세이브 채택이 세션을 접어도 잔액은 이미 맞다.
            // 걷기와 채택 사이에 아무 줄도 두지 않는다 — 채택이 먼저 서면 낙관 델타가 서버 잔액 위에 한 번 더 얹혀 이중 계상된다.
            // 그리고 걷기는 무음이어야 한다: 걷은 직후의 잔액은 "변동 전"이라, 그것이 화면에 닿으면 숫자가 되돌아갔다 다시 간다.
            if (t_result.Wallet != null)
            {
                _pending?.Settle(_notify: false);
                WalletCloud.Adopt(t_result.Wallet);
            }
            else
            {
                // 뒤따를 채택이 없으니 이 걷기가 화면을 되돌리는 유일한 통지다.
                _pending?.Settle();
            }

            // revision 0/누락 = 이 명령은 세이브를 쓰지 않았다. 그대로 채택에 넘기면 "정확히 +1" 단언이
            // 지갑만 쓴 명령을 RemoteAhead로 읽어 전 세션을 끊는다.
            if (t_result.Revision > 0)
                PlayerSaveCloud.AdoptServerResult(t_result.Revision, t_result.UpdatedSlots);

            return t_result;
        }
        catch (ServerAdoptionException)
        {
            // 채택이 이미 세션을 접었다 — 실패 표면을 두 번 칠하지 않고 도메인에게만 알린다.
            throw;
        }
        catch (Exception t_exception)
        {
            // 거절은 세션이 아니라 이 호출의 결과다 — 전용 타입으로 갈아 던져야 호출부가 catch(Exception) 한 줄로
            // 삼키는 게 눈에 보인다. 표면을 지는 주체가 호출한 도메인이라는 계약의 집행 지점.
            if (PlayerSaveCloud.ReportServerCommandFailure(t_exception) == ECloudFailureKind.Rejected)
                throw new ServerCommandRejectedException(_commandName, t_exception);

            throw;
        }
        finally
        {
            PlayerSaveCloud.ResumeUploads();
            s_inFlight = null;
            t_gate.TrySetResult();
        }
    }

    /// <summary>초기화 채택 중의 서버 호출. 게이트가 서기 전이라 봉인·채택이 없고,
    /// 응답 대신 호출부가 문서를 다시 읽어 정상 채택 경로로 합류한다.</summary>
    // Loading을 단언하는 이유는 오용 차단이다 — 게이트가 선 뒤에 이 창구로 세이브를 쓰면
    // 업로드 봉인도 revision 채택도 건너뛰어 다음 저장이 세션을 끊는다.
    internal static async UniTask<TResponse> InvokeBootAsync<TResponse>(string _commandName, object _request)
        where TResponse : class
    {
        if (PlayerSaveCloud.State != EPlayerSaveCloudState.Loading)
            throw new InvalidOperationException(
                $"Boot command '{_commandName}' is not allowed while the save cloud is {PlayerSaveCloud.State}.");

        return await RequireService(_commandName).InvokeAsync<TResponse>(_commandName, _request);
    }

    /// <summary>세이브를 바꾸지 않는 서버 호출. 봉인·채택 없이 서비스만 태운다.</summary>
    // 클라우드 상태를 묻지도, 직렬화 대기열에 서지도 않는다 — 채택이 실패한 상태를 진단하는 것이 이 경로의 용도다.
    internal static async UniTask<TResponse> InvokeReadOnlyAsync<TResponse>(string _commandName, object _request)
        where TResponse : class
    {
        return await RequireService(_commandName).InvokeAsync<TResponse>(_commandName, _request);
    }

    // 같은 txId로 딱 한 번 다시 태운다. 요청은 서버에 닿았는데 응답만 잃은 갈래가 있고, 그때 서버는
    // 영수증에 기록된 첫 응답을 그대로 돌려준다(재추첨·재차감 없음). 이 안전망이 성립하는 이유가
    // 위에서 payload에 얹은 txId다 — 그것을 빼면 재시도가 곧장 이중 과금이 된다.
    //
    // 대가는 봉인 시간이다: 한 시도가 CallableMilliseconds(15초)에 더해 재인증 1회를 쓸 수 있어
    // 최악이면 유저가 1분 가까이 응답을 기다린다. 그래도 재시도를 없애는 쪽이 이중 과금이라 받는다.
    static async UniTask<TResponse> SendAsync<TResponse>(
        ICallableService _service, string _commandName, Dictionary<string, object> _payload)
        where TResponse : class
    {
        try
        {
            return await _service.InvokeAsync<TResponse>(_commandName, _payload);
        }
        catch (Exception t_exception) when (CloudFailureClassifier.IsLostResponse(t_exception))
        {
            // 여기서 PlayerSaveCloud에 실패를 알리지 않는다 — 알리면 상태가 Offline으로 넘어가고
            // 실패 카운터가 올라, 곧 성공할 재시도가 장애 흔적을 남긴다.
            Debug.LogWarning(
                $"[ServerSaveCommands] '{_commandName}' lost its response — retrying once with the same txId.");

            return await _service.InvokeAsync<TResponse>(_commandName, _payload);
        }
    }

    static ICallableService RequireService(string _commandName)
    {
        ICallableService t_service = s_service;
        if (t_service == null)
            throw new InvalidOperationException($"Callable service is not available for '{_commandName}'.");

        return t_service;
    }
}

/// <summary>서버 응답을 로컬 세이브에 채택하지 못했다. 이 예외가 나온 시점에 세션은 이미 접혔다.</summary>
internal sealed class ServerAdoptionException : Exception
{
    internal ServerAdoptionException(string _message) : base(_message) { }
}

/// <summary>서버가 요청을 판정해 거절했다. 세션은 멀쩡하므로 유저 표면을 그리는 책임은 호출한 도메인에 있다.</summary>
internal sealed class ServerCommandRejectedException : Exception
{
    /// <summary>거절당한 명령 이름.</summary>
    internal string CommandName { get; }

    /// <summary>서버가 붙인 거절 사유 코드(rejectDomain 의 reason). 못 읽으면 빈 문자열이다.</summary>
    internal string Reason { get; }

    internal ServerCommandRejectedException(string _commandName, Exception _inner)
        : base($"Server command '{_commandName}' was rejected: {_inner.GetBaseException().Message}", _inner)
    {
        this.CommandName = _commandName;
        this.Reason      = ParseReason(_inner.GetBaseException().Message);
    }

    // 사유 코드는 서버가 message 앞머리에 "Reason: 설명" 으로 싣는다 — details 로 보내면
    // Unity SDK 의 FunctionsErrorParser 가 status 와 message 만 남기고 버린다.
    static string ParseReason(string _message)
    {
        if (string.IsNullOrEmpty(_message)) return string.Empty;

        int t_colon = _message.IndexOf(':');
        if (t_colon <= 0) return string.Empty;

        string t_head = _message.Substring(0, t_colon).Trim();

        // 사유 코드는 공백 없는 한 낱말이다. 접두어가 없는 옛 메시지를 사유로 오독하지 않으려는 관문이다.
        return t_head.Length > 0 && t_head.IndexOf(' ') < 0 ? t_head : string.Empty;
    }
}
