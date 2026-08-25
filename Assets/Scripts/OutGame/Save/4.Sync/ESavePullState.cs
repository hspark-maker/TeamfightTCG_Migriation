internal enum ESavePullState
{
    Disabled,
    WaitingAuth,
    Pulling,
    Validating,
    Classified,
    RemoteMissing,
    Failed,
    TimedOut
}

internal enum ESaveReconcileDecision
{
    None,
    RemoteMissing,
    InSync,
    LocalAhead,
    RemoteAhead,
    Diverged,
    NoBaseConflict,
    FutureSchema,
    InvalidRemote
}
