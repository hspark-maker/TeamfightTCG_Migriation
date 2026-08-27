/// <summary>클라우드 호출 실패를 유저 표면이 이해할 수 있는 세 갈래로 접은 값.</summary>
public enum ECloudFailureKind
{
    /// <summary>같은 요청을 그대로 다시 보내면 성공할 수 있다.</summary>
    Transient,

    /// <summary>서버가 요청을 판정해서 거절했다. 재시도해도 같은 답이 온다.</summary>
    Rejected,

    /// <summary>세션이 그대로는 진행 불가다. 배선·배포·인증이 어긋난 상태.</summary>
    Unusable,
}
