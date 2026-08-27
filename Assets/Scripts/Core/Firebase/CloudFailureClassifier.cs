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

            case FunctionsErrorCode.PermissionDenied:
            case FunctionsErrorCode.FailedPrecondition:
            case FunctionsErrorCode.InvalidArgument:
            case FunctionsErrorCode.OutOfRange:
            case FunctionsErrorCode.AlreadyExists:
            case FunctionsErrorCode.DataLoss:
                return ECloudFailureKind.Rejected;

            // NotFound는 함수 미배포·리전 오타의 404다 — 재시도성으로 접으면 배너만 영구히 뜨고 원인을 아무도 못 찾는다.
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

            case FirestoreError.PermissionDenied:
            case FirestoreError.FailedPrecondition:
            case FirestoreError.InvalidArgument:
            case FirestoreError.OutOfRange:
            case FirestoreError.AlreadyExists:
            case FirestoreError.DataLoss:
                return ECloudFailureKind.Rejected;

            case FirestoreError.Unauthenticated:
            case FirestoreError.Unimplemented:
            case FirestoreError.NotFound:
            default:
                return ECloudFailureKind.Unusable;
        }
    }
}
