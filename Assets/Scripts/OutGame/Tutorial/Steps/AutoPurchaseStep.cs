using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>입력 없이 팩을 구매해 개봉 씬으로 넘기는 스텝. 개봉 연출이 끝나면 지정 씬으로 돌아온다.</summary>
[CreateAssetMenu(fileName = "Step_AutoPurchase", menuName = "Card Battle/Outgame Tutorial/Step/Auto Purchase")]
public class AutoPurchaseStep : OutgameTutorialStep
{
    // 개봉 연출 씬. 저작 데이터가 아니라 시스템 고정 경로라 SO 필드가 아닌 상수로 둔다.
    const string PackOpenScene = "CardPack";

    [Tooltip("자동으로 구매할 카드팩")]
    [SerializeField] CardPackData pack;

    [Tooltip("중복 카드 1장당 환급 골드")]
    [SerializeField] long duplicateRefundGold;

    [Tooltip("개봉 연출 후 돌아올 씬 이름")]
    [SerializeField] string nextScene;

    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Auto;
    public override bool LeavesScene => true;

    public override bool Enter(OutgameTutorialStepContext _context)
    {
        // 불변식: 커밋이 실행보다 앞선다. 구매 직후 강제종료 시 "소유는 생겼는데 진행도는 0"이 되어
        // 레거시 마이그레이션이 온보딩을 영구 스킵시키는 구멍을 원천 봉쇄한다. 순서를 바꾸지 말 것.
        _context.CommitAdvance();

        var t_opened = CardPackOpener.TryPurchase(pack, duplicateRefundGold);
        if (t_opened == null || !t_opened.Success)
        {
            // 실패는 차감 없이 반환되므로(TryPurchase 보장) 커밋만 되돌리면 원상복구된다 — 다음 부트에 재시도.
            _context.Rollback();

            string t_result = t_opened != null ? t_opened.Result.ToString() : "null";
            string t_pack   = pack != null ? pack.PackId : "null";
            Debug.LogWarning($"[AutoPurchaseStep] 스텝 {_context.Index} 자동 구매 실패(pack={t_pack}, result={t_result}) — 씬 전환 없이 유지.");
            return false;
        }

        // 마지막 스텝이 자동 구매인 저작도 완료로 닫는다(진행도가 시퀀스 끝에 멈춰 재개 불가가 되지 않게).
        _context.CompleteIfLast();

        // 전투 진입은 BattleEntry 스텝(로비 PlayBtn)이 담당 → 캐리어의 튜토리얼 시작은 항상 false.
        PackHandoff.Set(t_opened, pack, nextScene, false);
        SceneManager.LoadScene(PackOpenScene);
        return false;
    }
}
