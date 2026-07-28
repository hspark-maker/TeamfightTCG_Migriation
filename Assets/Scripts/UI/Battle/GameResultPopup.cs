using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 결과 팝업의 등장 연출 진행자.
// 흐름: 암막 → 패널 팝 → 타이틀 낙하 → 보상 라인 등장 → 코인 분출·수렴(도착마다 수치가 롤링) → 안내문.
//
// 보상은 전투가 끝나는 순간 TurnRunner→RewardService가 이미 지급·영속화했다.
// 여기서는 확정된 값을 보여주기만 한다 — 재계산도, 재지급도 없다.
// 배선이 비어 있는 단계는 조용히 건너뛴다(연출이 진행을 막지 않게).
public class GameResultPopup : MonoBehaviour
{
    [Header("배선")]
    [SerializeField] RectTransform panel;
    [SerializeField] Button mainMenuButton;       // 전체화면 터치 영역(연출 중엔 스킵, 끝난 뒤엔 메인 이동)
    [SerializeField] string mainMenuScene = "LobbyScene";
    [SerializeField] CanvasGroup dimGroup;        // 암막(옵션)
    [SerializeField] RectTransform titleRect;     // 승리/패배 문구(옵션)
    [SerializeField] TMP_Text rewardGoldText;     // 지급된 골드 표시용(표시 전용)
    [SerializeField] CoinBurstEffect coinBurst;   // 코인 분출·수렴(옵션)
    [SerializeField] CanvasGroup hintGroup;       // "터치하면 메인 화면으로" 안내(옵션)

    [Header("타이밍")]
    [SerializeField] float dimDuration = 0.2f;
    [SerializeField] float enterDuration = 0.45f;
    [SerializeField] float titleDuration = 0.35f;
    [SerializeField] float rewardRevealDuration = 0.3f; // 패널 등장 뒤 보상 라인이 팝하는 시간.
    [SerializeField] float goldRollDuration = 0.15f;    // 코인 한 장이 닿을 때 수치가 굴러가는 시간.
    [SerializeField] float hintFadeDuration = 0.25f;

    [Header("연출 값")]
    [SerializeField] float titleDrop = 120f;      // 타이틀이 이만큼 위에서 떨어진다.
    [SerializeField] float goldPunch = 0.3f;      // 코인이 닿을 때 수치가 튀는 세기.

    Sequence revealSeq;    // 진행 중 등장 연출. 재진입 시 통째로 Kill해 좀비 시퀀스 누적 방지.
    Tween    goldRollTween; // 수치 롤링(시퀀스 밖에서 도는 별도 트윈 — 스킵 시 직접 걷는다).
    Tween    goldPunchTween; // 코인이 닿을 때의 수치 펀치.

    long m_totalGold;     // 이번 표시의 총 보상. 코인이 다 닿으면 이 값에 정확히 안착한다.
    long m_shownGold;     // 현재 텍스트에 찍힌 값(롤링 시작점).
    bool m_revealDone;    // 연출 완료 여부. 진행 중 터치는 스킵, 완료 후 터치는 메인 이동.

    Vector2 m_titleHome;  // 타이틀 원위치(낙하 기준). 첫 표시 때 한 번만 잡는다.
    bool m_titleHomeCaptured;

    void Awake()
    {
        this.panel.localScale = Vector3.zero;
        this.mainMenuButton?.onClick.AddListener(HandleTouch);
    }

    void OnDisable()
    {
        // 연출 중 꺼지면 트윈만 남는다 — 여기서 정리.
        KillTweens();
    }

    /// <summary>
    /// 결과 팝업 노출. _rewardGold는 이미 지급·영속화된 값을 그대로 표시만 한다.
    /// </summary>
    public void Show(long _rewardGold)
    {
        gameObject.SetActive(true);

        KillTweens();

        this.m_totalGold  = _rewardGold > 0 ? _rewardGold : 0;
        this.m_revealDone = false;

        ResetVisual();

        this.revealSeq = DOTween.Sequence().SetLink(gameObject);

        if (this.dimGroup != null)
            this.revealSeq.Append(this.dimGroup.DOFade(1f, this.dimDuration));

        this.revealSeq.Append(this.panel.DOScale(1f, this.enterDuration).SetEase(Ease.OutBack));

        if (this.titleRect != null)
        {
            this.revealSeq.Append(this.titleRect.DOAnchorPos(this.m_titleHome, this.titleDuration).SetEase(Ease.OutBack));
            this.revealSeq.Join(this.titleRect.DOScale(1f, this.titleDuration).SetEase(Ease.OutBack));
        }

        if (this.rewardGoldText != null)
            this.revealSeq.Append(this.rewardGoldText.transform.DOScale(1f, this.rewardRevealDuration).SetEase(Ease.OutBack));

        // 코인이 튀어 수치로 빨려들고, 닿을 때마다 그만큼 숫자가 굴러 오른다.
        if (this.coinBurst != null && this.m_totalGold > 0)
            this.revealSeq.Append(this.coinBurst.BuildBurst(HandleCoinArrived));

        if (this.hintGroup != null)
            this.revealSeq.Append(this.hintGroup.DOFade(1f, this.hintFadeDuration));

        // 스킵(Complete)으로 와도 여기를 지난다 — 수치는 항상 총액으로 확정된다.
        this.revealSeq.OnComplete(() =>
        {
            this.goldRollTween?.Kill();
            this.goldRollTween = null;
            this.goldPunchTween?.Kill(true);
            this.goldPunchTween = null;
            RenderGold(this.m_totalGold);
            this.m_revealDone = true;
        });
    }

    // 코인 한 장이 수치에 닿았다 — 그 몫만큼 숫자를 굴리고 살짝 튀긴다.
    void HandleCoinArrived(int _arrived, int _total)
    {
        if (this.rewardGoldText == null) return;

        // 마지막 장은 나눗셈 오차 없이 총액 그대로 — 표시액이 지급액과 어긋나지 않게.
        long t_goal = _arrived >= _total
            ? this.m_totalGold
            : (long)(this.m_totalGold * (double)_arrived / _total);

        long t_start = this.m_shownGold;

        this.goldRollTween?.Kill();
        this.goldRollTween = DOVirtual.Float(0f, 1f, this.goldRollDuration,
                                             _t => RenderGold(t_start + (long)((t_goal - t_start) * _t)))
                                      .SetLink(gameObject)
                                      .OnComplete(() => RenderGold(t_goal));

        // 이전 펀치는 완료시켜 죽인다(Kill(true)) — 스케일이 중간값에 눌린 채 남지 않게.
        this.goldPunchTween?.Kill(true);
        this.goldPunchTween = this.rewardGoldText.transform
                                  .DOPunchScale(Vector3.one * this.goldPunch, this.goldRollDuration, 1, 0.6f)
                                  .SetLink(gameObject);
    }

    void RenderGold(long _value)
    {
        this.m_shownGold = _value;
        if (this.rewardGoldText != null) this.rewardGoldText.text = $"+{_value:N0}";
    }

    // 연출 시작 상태로 되돌린다(재진입 대비).
    void ResetVisual()
    {
        this.panel.localScale = Vector3.zero;

        if (this.dimGroup != null) this.dimGroup.alpha = 0f;
        if (this.hintGroup != null) this.hintGroup.alpha = 0f;

        if (this.titleRect != null)
        {
            if (!this.m_titleHomeCaptured)
            {
                this.m_titleHome = this.titleRect.anchoredPosition;
                this.m_titleHomeCaptured = true;
            }
            this.titleRect.anchoredPosition = this.m_titleHome + new Vector2(0f, this.titleDrop);
            this.titleRect.localScale = Vector3.one * 0.6f;
        }

        if (this.rewardGoldText != null)
        {
            // 라벨('골드')·코인 아이콘은 프리팹의 정적 요소, 여기선 획득 수치만 채운다.
            this.rewardGoldText.transform.localScale = Vector3.zero;
            // 코인이 실어 나를 값이면 0에서 출발, 아니면 곧장 총액을 보여준다.
            bool t_willRoll = this.coinBurst != null && this.m_totalGold > 0;
            RenderGold(t_willRoll ? 0 : this.m_totalGold);
        }
    }

    // 전체화면 터치. 연출 중이면 스킵, 끝난 뒤면 메인 화면으로.
    void HandleTouch()
    {
        if (!this.m_revealDone)
        {
            if (this.revealSeq != null && this.revealSeq.IsActive()) this.revealSeq.Complete(true);
            else this.m_revealDone = true;   // 시퀀스가 이미 사라진 예외 상황 — 다음 터치가 먹히게.
            return;
        }

        BattleCleanup.LoadScene(this.mainMenuScene);
    }

    void KillTweens()
    {
        this.revealSeq?.Kill();
        this.revealSeq = null;
        this.goldRollTween?.Kill();
        this.goldRollTween = null;
        this.goldPunchTween?.Kill();
        this.goldPunchTween = null;
    }
}
