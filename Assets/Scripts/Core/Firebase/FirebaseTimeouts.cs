public static class FirebaseTimeouts
{
    public const int AuthAndReadMilliseconds = 5000;
    public const int TransactionMilliseconds = 10000;
    public const int RestRequestSeconds = 10;

    // SDK 내장 HttpClient.Timeout(70초)보다 반드시 짧아야 한다 — 그보다 길면 유저가 70초를 응답 없이 기다린다.
    public const int CallableMilliseconds = 15000;
}
