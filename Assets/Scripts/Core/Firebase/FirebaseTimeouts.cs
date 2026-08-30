public static class FirebaseTimeouts
{
    public const int AuthAndReadMilliseconds = 5000;

    // Firebase 네이티브 SDK의 첫 적재(CheckAndFixDependencies)는 왕복이 아니라 프로세스 1회성 비용이라
    // 위의 왕복 예산으로 재면 안 된다 — 에디터 첫 Play에서 5초를 넘겨 부트가 통째로 실패했다.
    // 에디터/기기 첫 부트에서만 쓰이고, 한 번 데워지면 이후 인증은 다시 AuthAndRead 예산으로 잰다.
    public const int SdkColdStartMilliseconds = 40000;
    public const int TransactionMilliseconds = 10000;
    public const int RestRequestSeconds = 10;

    // SDK 내장 HttpClient.Timeout(70초)보다 반드시 짧아야 한다 — 그보다 길면 유저가 70초를 응답 없이 기다린다.
    public const int CallableMilliseconds = 15000;
}
