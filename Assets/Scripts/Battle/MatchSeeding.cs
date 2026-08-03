/// <summary>이 판의 MatchRandom 시드를 거는 **정책의 단일 진실원**.
///
/// 규칙은 하나다: 멀티는 건드리지 않고(SyncInitialDecks의 commit-reveal이 시드한다),
/// 튜토리얼이면 고정 시드, 아니면 로컬 랜덤.
///
/// 이 정책이 GameInitializer와 TurnRunner 두 곳에 복사돼 있었다 — 튜토리얼 시드 규칙을
/// 한쪽만 고치면 우회 진입 경로가 다른 시드를 내는 구조였다. 호출 시점만 두 종류이고
/// 정책은 여기 한 곳이다.</summary>
public static class MatchSeeding
{
    /// <summary>새 판 시작 시점의 시드(정상 경로). 필드 초기화 **직전**에 불러야 한다 —
    /// 덱 셔플이 MatchRandom을 소비하므로 그보다 늦으면 셔플이 시드 밖 난수로 샌다.
    /// 이미 시드돼 있어도 새 판이므로 새로 건다.</summary>
    public static void SeedForNewMatch()
    {
        if (DeckConfig.IsMultiplayer)
        {
            // 정상 경로에선 도달 불가. 조용히 넘기면 미시드로 진행하는 사고를 숨기므로 로그를 남긴다.
            UnityEngine.Debug.LogError("[Seed] 멀티 경로에서 SeedForNewMatch 호출 — commit-reveal과 충돌한다.");
            return;
        }
        Apply();
    }

    /// <summary>우회 진입(TurnRunner.StartBattle 단독 호출 등)용 폴백.
    /// 이미 시드됐으면 손대지 않는다 — 덮어쓰면 셔플로 이미 전진한 스트림이 리셋돼
    /// 시드 하나가 두 시퀀스를 낸다.</summary>
    public static void EnsureSeeded()
    {
        if (DeckConfig.IsMultiplayer || MatchRandom.IsSeeded) return;
        Apply();
    }

    static void Apply()
    {
        if (TutorialConfig.IsActive) MatchRandom.Seed(TutorialConfig.FixedSeed);
        else                         MatchRandom.SeedRandomLocal();
    }
}
