/// <summary>부트가 멈춘 사유. 재시도로 풀릴 수 있는 대기 상태(EGameBootState.BlockedRetryable)만 여기 온다 —
/// 표시 문구는 화면(LoadingCoverView)이 고른다. 값은 끝에만 덧붙인다(로그·직렬화 호환).</summary>
public enum ESaveBootBlockReason
{
    None = 0,

    // 서버에 도달하지 못했다(기내모드·타임아웃·일시적 단절)
    Network,

    // 로그인이 아직 서지 않았다(의존성 초기화 실패 포함)
    Auth,

    // 서버 문서가 스키마·해시 검증을 통과하지 못했다
    ServerData,

    // 다른 기기가 먼저 저장해 서버 문서가 앞서 있다
    Conflict,

    // 저장 매체 쓰기 자체가 실패했다
    Storage,
}

/// <summary>저장소 결과를 차단 사유로 접는 단일 창구. 접는 규칙이 흩어지면 화면마다 다른 문구가 뜬다.</summary>
public static class SaveBootBlock
{
    /// <summary>부팅 read(PrimeAsync) 결과를 차단 사유로 접는다.</summary>
    public static ESaveBootBlockReason ReasonOf(ESaveSourcePrimeResult _prime)
    {
        switch (_prime)
        {
            case ESaveSourcePrimeResult.Ok:
            case ESaveSourcePrimeResult.NotFound:      return ESaveBootBlockReason.None;
            case ESaveSourcePrimeResult.Unauthenticated: return ESaveBootBlockReason.Auth;
            case ESaveSourcePrimeResult.Invalid:       return ESaveBootBlockReason.ServerData;
            default:                                   return ESaveBootBlockReason.Network;
        }
    }

    /// <summary>쓰기 1회 결과를 차단 사유로 접는다.</summary>
    public static ESaveBootBlockReason ReasonOf(ESaveWriteResult _write)
    {
        switch (_write)
        {
            case ESaveWriteResult.Success:  return ESaveBootBlockReason.None;
            case ESaveWriteResult.Offline:  return ESaveBootBlockReason.Network;
            case ESaveWriteResult.Conflict: return ESaveBootBlockReason.Conflict;
            default:                        return ESaveBootBlockReason.Storage;
        }
    }
}
