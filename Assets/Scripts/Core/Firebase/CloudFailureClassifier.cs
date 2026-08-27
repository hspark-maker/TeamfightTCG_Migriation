using System;
using Firebase.Firestore;
using Firebase.Functions;

/// <summary>클라우드 예외를 유저 표면이 분기할 수 있는 세 갈래로 접는다.</summary>
public static class CloudFailureClassifier
{
    /// <summary>예외의 근본 원인을 벗겨 재시도성·거절·사용불가 중 하나로 판정한다.</summary>
    internal static ECloudFailureKind Classify(Exception _exception)
    {
        if (_exception == null) return ECloudFailureKind.Unusable;

        Exception t_root = _exception.GetBaseException();

        if (t_root is FunctionsException t_functionsException)
            return ClassifyFunctions(t_functionsException.ErrorCode);

        if (t_root is FirestoreException t_firestoreException)
            return ClassifyFirestore(t_firestoreException.ErrorCode);

        if (t_root is TimeoutException ||
            t_root is System.Net.Http.HttpRequestException ||
            t_root is System.IO.IOException ||
            t_root is OperationCanceledException)
            return ECloudFailureKind.Transient;

        // 나머지는 배선·직렬화 오류(프로그래머 실수)다 — 재시도로 덮으면 원인이 영영 안 드러난다.
        return ECloudFailureKind.Unusable;
    }

    /// <summary>로그에 남길 오류 코드 문자열. 한 갈래로 접힌 원인들을 사후에 가르는 유일한 단서다.</summary>
    internal static string Describe(Exception _exception)
    {
        if (_exception == null) return "none";

        Exception t_root = _exception.GetBaseException();

        if (t_root is FunctionsException t_functionsException)
            return $"functions/{t_functionsException.ErrorCode}";

        if (t_root is FirestoreException t_firestoreException)
            return $"firestore/{t_firestoreException.ErrorCode}";

        return $"client/{t_root.GetType().Name}";
    }

    /// <summary>Transient로 접혔지만 실제로는 서버 함수 안의 미처리 예외일 공산이 큰 실패인지.</summary>
    // firebase-functions v7은 함수 안에서 잡히지 않은 예외를 전부 internal로 내보낸다 — 갈래를 옮기면
    // 진짜 일시 장애까지 재시작을 요구하게 되므로, 갈래는 그대로 두고 로그에서만 가른다.
    internal static bool IsUnhandledServerFault(Exception _exception)
    {
        return _exception?.GetBaseException() is FunctionsException t_functionsException &&
               t_functionsException.ErrorCode == FunctionsErrorCode.Internal;
    }

    // 두 enum은 지금 우연히 같은 gRPC 순서지만 그건 계약이 아니다 — 숫자 캐스트로 합치지 않는다.
    static ECloudFailureKind ClassifyFunctions(FunctionsErrorCode _code)
    {
        switch (_code)
        {
            case FunctionsErrorCode.Unavailable:
            case FunctionsErrorCode.DeadlineExceeded:
            case FunctionsErrorCode.Aborted:
            case FunctionsErrorCode.ResourceExhausted:
            case FunctionsErrorCode.Internal:
            case FunctionsErrorCode.Unknown:
            case FunctionsErrorCode.Cancelled:
                return ECloudFailureKind.Transient;

            // 도메인 거절 축으로 **예약한** 두 코드다. 서버(functions/src)가 인프라 판정에 쓰지 않는 코드만 고른 것이고,
            // R5 이후의 "재화 부족 · 이미 수령" 같은 정상 거절은 반드시 이 둘로 던져야 한다.
            // failed-precondition·out-of-range는 saveDocument.ts가 스키마 드리프트·문서 없음에 이미 쓰고 있어 예약할 수 없다.
            case FunctionsErrorCode.PermissionDenied:
            case FunctionsErrorCode.AlreadyExists:
                return ECloudFailureKind.Rejected;

            // NotFound는 함수 미배포·리전 오타의 404다 — 재시도성으로 접으면 배너만 영구히 뜨고 원인을 아무도 못 찾는다.
            // FailedPrecondition·OutOfRange·InvalidArgument·DataLoss는 스키마 드리프트·미지 env·요청 배선 오류라
            // 이 클라이언트로는 다음 명령도 같은 답이다.
            case FunctionsErrorCode.FailedPrecondition:
            case FunctionsErrorCode.OutOfRange:
            case FunctionsErrorCode.InvalidArgument:
            case FunctionsErrorCode.DataLoss:
            case FunctionsErrorCode.Unauthenticated:
            case FunctionsErrorCode.Unimplemented:
            case FunctionsErrorCode.NotFound:
            default:
                return ECloudFailureKind.Unusable;
        }
    }

    static ECloudFailureKind ClassifyFirestore(FirestoreError _code)
    {
        switch (_code)
        {
            case FirestoreError.Unavailable:
            case FirestoreError.DeadlineExceeded:
            case FirestoreError.Aborted:
            case FirestoreError.ResourceExhausted:
            case FirestoreError.Internal:
            case FirestoreError.Unknown:
            case FirestoreError.Cancelled:
                return ECloudFailureKind.Transient;

            // 클라가 문서를 직접 쓰는 경로라 여기서 나오는 거절은 전부 룰 거부다 — 표면은 Unusable과 같은 차단이지만
            // 갈래는 남겨 둔다(로그의 code 문자열이 룰인지 배선인지를 가른다).
            case FirestoreError.PermissionDenied:
            case FirestoreError.AlreadyExists:
                return ECloudFailureKind.Rejected;

            case FirestoreError.FailedPrecondition:
            case FirestoreError.OutOfRange:
            case FirestoreError.InvalidArgument:
            case FirestoreError.DataLoss:
            case FirestoreError.Unauthenticated:
            case FirestoreError.Unimplemented:
            case FirestoreError.NotFound:
            default:
                return ECloudFailureKind.Unusable;
        }
    }
}
