#if !DISABLE_SRDEBUGGER
using System.ComponentModel;
using UnityEngine;

// 에디터 SROptions+와 런타임 치트 패널이 공유하는 옵션 목록.
// 조작은 기존 실행 창구에 맡기고 여기서는 표시·입력만 연결한다.
public partial class SROptions
{
    [Category("카드"), DisplayName("모든 카드 해금")]
    public void UnlockAllCards() => OutgameDebugActions.UnlockAllCards();

    [Category("카드"), DisplayName("모든 카드 회수")]
    public void RevokeAllCards() => OutgameDebugActions.RevokeAllCards();

    [Category("카드"), DisplayName("모든 카드 최대 성장")]
    public void MaxCardGrowth() => OutgameDebugActions.MaxCardGrowth();

    [Category("카드"), DisplayName("카드 강화·한계돌파 초기화")]
    public void ResetCardGrowth() => OutgameDebugActions.ResetCardGrowth();

    [Category("카드"), DisplayName("소유 카드 로그")]
    public void LogOwnership() => OutgameDebugActions.LogOwnership();

    [Category("재화"), DisplayName("골드 +1,000")]
    public void GrantGold() => OutgameDebugActions.GrantGold();

    [Category("재화"), DisplayName("다이아 +1,000")]
    public void GrantDiamond() => OutgameDebugActions.GrantDiamond();

    [Category("재화"), DisplayName("에너지 +1,000")]
    public void GrantEnergy() => OutgameDebugActions.GrantEnergy();

    [Category("재화"), DisplayName("조각 +1,000")]
    public void GrantShard() => OutgameDebugActions.GrantShard();

    [Category("재화"), DisplayName("룰렛 티켓 +10")]
    public void GrantRouletteTicket() => OutgameDebugActions.GrantRouletteTicket();

    [Category("튜토리얼"), DisplayName("튜토리얼 완료")]
    public void SkipTutorial() => OutgameDebugActions.SkipTutorial();

    [Category("튜토리얼"), DisplayName("진행도 초기화")]
    public void ResetTutorial() => OutgameDebugActions.ResetTutorial();

    [Category("튜토리얼"), DisplayName("트리거 초기화")]
    public void ResetTriggeredTutorials() => OutgameDebugActions.ResetTriggeredTutorials();

    [Category("계정"), DisplayName("계정 최대 레벨")]
    public void FillAccountLevel() => OutgameDebugActions.FillAccountLevel();

    [Category("계정"), DisplayName("계정 레벨 초기화")]
    public void ResetAccountLevel() => OutgameDebugActions.ResetAccountLevel();

    [Category("랭크"), DisplayName("티어 올리기")]
    public void RaiseTier() => OutgameDebugActions.RaiseTier();

    [Category("랭크"), DisplayName("티어 내리기")]
    public void LowerTier() => OutgameDebugActions.LowerTier();

    [Category("랭크"), DisplayName("승급전 대기")]
    public void JumpToPromoStandby() => OutgameDebugActions.JumpToPromoStandby();

    [Category("랭크"), DisplayName("랭크 초기화")]
    public void ResetTier() => OutgameDebugActions.ResetTier();

    [Category("모험"), DisplayName("현재 정점 도전")]
    public void StartCurrentAdventureNode() => OutgameDebugActions.StartCurrentAdventureNode();

    [Category("미션"), DisplayName("미션 모두 완료 (서버)")]
    public void CompleteMissions() => OutgameDebugActions.CompleteMissions();

    [Category("미션"), DisplayName("일일 미션 초기화 (서버·테스트 전용)")]
    public void ResetDailyMissions() => OutgameDebugActions.ResetDailyMissions();

    [Category("카드"), DisplayName("소유 카드")]
    public string OwnedCards => $"{OwnershipManager.OwnedCount} / {CardCatalog.Count}";

    [Category("재화"), DisplayName("잔액")]
    public string CurrencyBalances => $"G {CurrencyManager.Gold} / D {CurrencyManager.Diamond} / E {CurrencyManager.Energy} / S {CurrencyManager.Shard} / T {CurrencyManager.GetBalance(ECurrencyType.RouletteTicket)}";

    [Category("튜토리얼"), DisplayName("기능 잠금 무시")]
    public bool IgnoreFeatureLocks
    {
        get => OutgameFeatureLock.ForceUnlockAllForDebug;
        set
        {
            if (value != OutgameFeatureLock.ForceUnlockAllForDebug) OutgameDebugActions.ToggleFeatureLock();
        }
    }

    [Category("튜토리얼"), DisplayName("챕터 (1부터)"), NumberRange(1, 999), Increment(1)]
    [SRDebugger.SRParamOf(nameof(RestartTutorialChapter))]
    public int TutorialChapter { get; set; } = 1;

    [Category("튜토리얼"), DisplayName("챕터 처음으로 (씬 재진입 시 적용)")]
    public void RestartTutorialChapter() => OutgameDebugActions.RestartTutorialFromChapter(TutorialChapter - 1);

    [Category("계정"), DisplayName("추가 경험치"), NumberRange(1, 1000000), Increment(500)]
    [SRDebugger.SRParamOf(nameof(AddAccountExperience))]
    public int AccountExperience { get; set; } = 500;

    [Category("계정"), DisplayName("경험치 추가")]
    public void AddAccountExperience() => OutgameDebugActions.AddAccountExp(AccountExperience);

    [Category("랭크"), DisplayName("현재 랭크")]
    public string CurrentRank => $"{RankManager.GetInfo().DisplayName} / 승급 대기: {RankManager.IsPromoPending}";

    [Category("연출"), DisplayName("도감 삽입 카드 수"), NumberRange(1, 100), Increment(1)]
    [SRDebugger.SRParamOf(nameof(PlayAlbumInsert))]
    public int AlbumInsertCount { get; set; } = 3;

    [Category("연출"), DisplayName("도감 삽입 예약")]
    public void PlayAlbumInsert() => OutgameDebugActions.ForceAlbumInsertSession(AlbumInsertCount);

    [Category("연출"), DisplayName("희귀 팩 연출")]
    public void OpenRareTestPack() => OpenTestPack(ECardGrade.Rare);

    [Category("연출"), DisplayName("신비 팩 연출")]
    public void OpenArcaneTestPack() => OpenTestPack(ECardGrade.Arcane);

    [Category("연출"), DisplayName("신화 팩 연출")]
    public void OpenMythicTestPack() => OpenTestPack(ECardGrade.Mythic);

    static void OpenTestPack(ECardGrade _grade)
    {
        SRDebug.Instance.HideDebugPanel();
        OutgameDebugActions.OpenRarityTestPack(_grade);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Category("서버"), DisplayName("연결 상태")]
    public string CloudStatus => $"{(ContentProfileConfig.Active.FirebaseEmulators.IsEnabled ? "EMU" : "LIVE")} / {PlayerSaveCloud.State} / rev {PlayerSaveCloud.Revision}";

    [Category("서버"), DisplayName("인증 상태")]
    public string CloudIdentity => $"{FirebaseAuthService.Instance.UserId} / {FirebaseAuthService.Instance.State}";

    [Category("서버"), DisplayName("Ping")]
    public void PingServer() => OutgameDebugActions.PingServer();

    [Category("서버"), DisplayName("Revision 증가")]
    public void BumpServerRevision() => OutgameDebugActions.BumpServerRevision();

    [Category("서버"), DisplayName("규칙 거부 진단")]
    public void ProbeRuleDenials() => OutgameDebugActions.ProbeRuleDenials();

    [Category("룰렛"), DisplayName("서버 추첨 사용 중")]
    public bool RouletteUsesServer => RouletteManager.IsServerBacked;

    [Category("룰렛"), DisplayName("로컬 추첨으로 전환 (재시작 시 복구)")]
    public void UseLocalRouletteSource() => OutgameDebugActions.UseLocalRouletteSource();

    [Category("전투"), DisplayName("적 필드 선택")]
    public bool KillEnemyField { get; set; }

    int m_killSlot;
    [Category("전투"), DisplayName("사망 대상 슬롯 (0부터)"), NumberRange(0, BattleField.SLOT_COUNT - 1), Increment(1)]
    public int KillCardSlot
    {
        get => m_killSlot;
        set => m_killSlot = Mathf.Clamp(value, 0, BattleField.SLOT_COUNT - 1);
    }

    [Category("전투"), DisplayName("선택한 카드")]
    public string KillTarget => BattleDebugKill.DescribeSlot(KillEnemyField, KillCardSlot);

    [Category("전투"), DisplayName("선택 카드 강제 사망 (싱글 전용)")]
    public void KillSelectedCard() => BattleDebugKill.KillSlot(KillEnemyField, KillCardSlot);
#endif

    [Category("VFX"), DisplayName("배속"), NumberRange(0.2, 3), Increment(0.1)]
    public float BattleSpeed
    {
        get => GameTiming.Speed;
        set => GameTiming.Speed = Mathf.Clamp(value, 0.2f, 3f);
    }

    [Category("VFX"), DisplayName("배속 1x")]
    public void ResetBattleSpeed() => GameTiming.Speed = 1f;

    [Category("VFX"), DisplayName("연결 상태")]
    public string VfxStatus => FindVfx() is VfxDebugWindow t_vfx ? t_vfx.Status : "VfxDebugWindow가 있는 테스트 씬에서 사용";

    [Category("VFX"), DisplayName("발동 슬롯 (내 필드)"), NumberRange(0, BattleField.SLOT_COUNT - 1), Increment(1)]
    public int VfxSourceSlot { get; set; }

    [Category("VFX"), DisplayName("대상 슬롯"), NumberRange(0, BattleField.SLOT_COUNT - 1), Increment(1)]
    public int VfxTargetSlot { get; set; }

    [Category("VFX"), DisplayName("힐·타격 표시량"), NumberRange(1, 9), Increment(1)]
    public int VfxHealAmount { get; set; } = 1;

    [Category("VFX"), DisplayName("라이브러리 효과")]
    public BattleVfxId VfxEffect { get; set; }

    [Category("VFX"), DisplayName("선택 효과 연결됨")]
    public bool VfxEffectIsWired => BattleVfx.TryGetEntry(VfxEffect, out _);

    [Category("VFX"), DisplayName("발동 위치에서 재생")]
    public void PlayVfxWorld() => RunVfx(t_vfx => t_vfx.PlayWorld(VfxEffect));

    [Category("VFX"), DisplayName("대상에 부착 재생")]
    public void PlayVfxAttached() => RunVfx(t_vfx => t_vfx.PlayAttached(VfxEffect));

    [Category("VFX"), DisplayName("힐 투사체: 내 필드 대상 슬롯")]
    public void PlaySingleHealBurst() => RunVfx(t_vfx => t_vfx.PlayHealBurst(true));

    [Category("VFX"), DisplayName("힐 투사체: 내 필드 전체")]
    public void PlayFullHealBurst() => RunVfx(t_vfx => t_vfx.PlayHealBurst(false));

    [Category("VFX"), DisplayName("대상 힐 연출")]
    public void PlayTargetHeal() => RunVfx(t_vfx => t_vfx.PlayHealEffect());

    [Category("VFX"), DisplayName("대상 타격 연출")]
    public void PlayTargetHit() => RunVfx(t_vfx => t_vfx.PlayHit());

    [Category("VFX"), DisplayName("힐 곡선 높이 (라이브러리 값)"), NumberRange(0, 3), Increment(0.1)]
    public float HealCurveHeight
    {
        get => BattleVfx.Library != null ? BattleVfx.Library.healCurveHeight : 0f;
        set { if (BattleVfx.Library != null) BattleVfx.Library.healCurveHeight = Mathf.Clamp(value, 0f, 3f); }
    }

    [Category("VFX"), DisplayName("힐 곡선 방향 교차 (라이브러리 값)")]
    public bool HealAlternateCurve
    {
        get => BattleVfx.Library != null && BattleVfx.Library.healAlternateCurve;
        set { if (BattleVfx.Library != null) BattleVfx.Library.healAlternateCurve = value; }
    }

    static VfxDebugWindow FindVfx() => Object.FindFirstObjectByType<VfxDebugWindow>();

    void RunVfx(System.Action<VfxDebugWindow> _action)
    {
        VfxDebugWindow t_vfx = FindVfx();
        if (t_vfx == null)
        {
            Debug.LogWarning("[SROptions] VfxDebugWindow가 있는 테스트 씬에서 실행하세요.");
            return;
        }
        t_vfx.SourceSlot = Mathf.Clamp(VfxSourceSlot, 0, BattleField.SLOT_COUNT - 1);
        t_vfx.TargetSlot = Mathf.Clamp(VfxTargetSlot, 0, BattleField.SLOT_COUNT - 1);
        t_vfx.HealAmount = Mathf.Clamp(VfxHealAmount, 1, 9);
        SRDebug.Instance.HideDebugPanel();
        _action(t_vfx);
    }
}
#endif
