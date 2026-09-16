using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 로비 설정 판의 프로필 요약(닉네임·랭크 티어명·계정 레벨) 표시.
///
/// 세 통지를 함께 구독하는 이유는 하나다: 이 판은 풀의 uiRoot에서 로비 위를 덮으므로
/// 판이 닫혀도 아래 탭의 OnEnable이 오지 않는다 — 통지가 유일한 갱신 신호다.
///
/// 배지는 스프라이트만 갈아끼운다 — 승급 연출과 별 줄은 로비 RankHud 몫이다.
/// 그 RankHud를 여기에 대신 붙이지 않는 이유는 MatchProfileView와 같다 —
/// 그쪽은 OnEnable에서 자기 정적 인스턴스를 잡아, 이 판이 떠 있는 동안 로비의 승급 연출이 대상을 잃는다.
///
/// 등급 표나 레벨 곡선을 못 읽었으면 그 축은 아무것도 쓰지 않고 저작값을 그대로 둔다.
/// 빈 칸으로 만들면 "언랭크"나 "레벨 0"처럼 읽힌다.
public class ProfileSummaryView : MonoBehaviour
{
    [Header("신원")]
    [Tooltip("현재 프로필의 아바타와 프레임을 표시하는 뷰.")]
    [SerializeField] ProfileAvatarView avatarView;

    [Tooltip("닉네임 자리. 미배선이면 그 축만 건너뛴다.")]
    [SerializeField] TMP_Text nicknameText;

    [Tooltip("랭크 티어명 자리(\"브론즈 1\"). 미배선이면 그 축만 건너뛴다.")]
    [SerializeField] TMP_Text tierNameText;

    [Tooltip("랭크 티어 배지 자리. 미배선이면 그 축만 건너뛴다.\n" +
             "등급 표를 못 읽었거나 배지가 미저작이면 저작된 그림을 그대로 둔다.")]
    [SerializeField] Image rankBadgeImage;

    [Header("계정 레벨")]
    [Tooltip("레벨 수치. {0}=레벨.")]
    [SerializeField] TMP_Text levelText;
    [SerializeField] string levelFormat = "Lv.{0}";

    [Tooltip("레벨 안 경험치. {0}=이 레벨에서 쌓은 양, {1}=이 레벨을 채우는 총량.")]
    [SerializeField] TMP_Text expText;
    [SerializeField] string expFormat = "{0}  /  {1}";

    [Tooltip("레벨 안 진행을 그리는 게이지. 구현체를 가리지 않는다(현재는 BarProgressGauge).")]
    [SerializeField] RankProgressGauge gauge;

    [Tooltip("경험치가 증가할 때 레벨 한 구간을 채우는 시간.")]
    [SerializeField] float levelUpFillDuration = 0.25f;

    [Tooltip("레벨 경계를 통과할 때 레벨 수치가 튀는 세기.")]
    [SerializeField] float levelUpPunch = UiPunch.DEFAULT_SCALE;

    // 숨겨진 동안의 보상은 보상 팝업이 보여 준다. 이 뷰는 열린 동안 도착한 증가만 연출한다.
    bool m_hasShownExp;
    long m_shownExp;
    long m_targetExp;
    string m_userId;
    Coroutine m_expAnimation;
    Tween m_levelPunch;

    /// <summary>지금 값으로 다시 그린다.</summary>
    public void Refresh()
    {
        this.RefreshIdentity();
        this.RefreshLevel();
    }

    void RefreshIdentity()
    {
        if (this.avatarView != null) this.avatarView.Render(ProfileManager.CurrentLook);
        if (this.nicknameText != null) this.nicknameText.text = ProfileManager.Nickname;

        if (!RankManager.IsConfigured) return;

        RankInfo t_rank = RankManager.GetInfo();

        if (this.tierNameText != null) this.tierNameText.text = t_rank.DisplayName;
        if (this.rankBadgeImage != null && t_rank.Badge != null) this.rankBadgeImage.sprite = t_rank.Badge;
    }

    void RefreshLevel()
    {
        if (!AccountLevelManager.IsConfigured) return;

        AccountLevelInfo t_info = AccountLevelManager.GetInfo();
        string t_userId = FirebaseAuthService.Instance.UserId;
        if (!this.m_hasShownExp || this.m_userId != t_userId || !PlayerSaveCloud.IsGateComplete ||
            t_info.Exp < this.m_targetExp || !this.isActiveAndEnabled)
        {
            this.StopLevelAnimation();
            this.m_userId = t_userId;
            this.m_hasShownExp = true;
            this.m_shownExp = this.m_targetExp = t_info.Exp;
            this.RenderLevel(t_info);
            return;
        }

        this.m_targetExp = t_info.Exp;
        if (this.m_expAnimation == null && this.m_shownExp < this.m_targetExp)
            this.m_expAnimation = this.StartCoroutine(this.AnimateExperience());
    }

    IEnumerator AnimateExperience()
    {
        // 첫 프레임을 넘긴 뒤 시작해 즉시 완료되는 경우에도 코루틴 핸들이 남지 않게 한다.
        yield return null;
        while (this.m_shownExp < this.m_targetExp)
        {
            AccountLevelInfo t_startInfo = AccountLevelManager.GetInfoAt(this.m_shownExp);
            long t_startExp = this.m_shownExp;
            long t_endExp = t_startInfo.IsMaxLevel
                ? this.m_targetExp
                : System.Math.Min(this.m_targetExp, t_startInfo.NextRequiredExp);
            int t_remainingLevels = AccountLevelManager.GetInfoAt(this.m_targetExp).Level - t_startInfo.Level;
            float t_duration = Mathf.Clamp(this.levelUpFillDuration, 0.01f, 1.2f) /
                               Mathf.Max(1f, t_remainingLevels * 0.25f);
            float t_elapsed = 0f;
            while (t_elapsed < t_duration)
            {
                t_elapsed += Time.unscaledDeltaTime;
                double t_ratio = Mathf.Clamp01(t_elapsed / t_duration);
                t_ratio = 1d - (1d - t_ratio) * (1d - t_ratio);
                this.m_shownExp = t_ratio >= 1d ? t_endExp
                    : t_startExp + (long)((t_endExp - t_startExp) * t_ratio);
                // 경계 프레임은 이전 레벨의 100%를 먼저 보여 준다.
                this.RenderLevel(new AccountLevelInfo(t_startInfo.Level, this.m_shownExp,
                    t_startInfo.LevelRequiredExp, t_startInfo.NextRequiredExp, t_startInfo.IsMaxLevel));
                yield return null;
            }

            AccountLevelInfo t_current = AccountLevelManager.GetInfoAt(this.m_shownExp);
            this.RenderLevel(t_current);
            if (t_current.Level > t_startInfo.Level && this.levelText != null)
                this.m_levelPunch = UiPunch.Play(this.levelText.transform, this.levelUpPunch)
                    .SetUpdate(true).OnKill(() => this.m_levelPunch = null);
        }
        this.m_expAnimation = null;
    }

    void RenderLevel(AccountLevelInfo _info)
    {
        if (this.levelText != null) this.levelText.text = string.Format(this.levelFormat, _info.Level);
        if (this.expText != null) this.expText.text = _info.IsMaxLevel
            ? "MAX" : string.Format(this.expFormat, _info.ExpInLevel, _info.ExpToNext);
        if (this.gauge != null) this.gauge.SetRatio(_info.LevelProgress);
    }

    void StopLevelAnimation()
    {
        if (this.m_expAnimation != null) this.StopCoroutine(this.m_expAnimation);
        this.m_expAnimation = null;
        this.m_levelPunch?.Kill(true);
        this.m_levelPunch = null;
        if (this.gauge != null) this.gauge.Stop();
    }

    void OnEnable()
    {
        this.m_hasShownExp = false;
        ProfileManager.OnChanged      += this.Refresh;
        RankManager.OnChanged         += this.Refresh;
        AccountLevelManager.OnChanged += this.Refresh;
        this.Refresh();
    }

    void OnDisable()
    {
        ProfileManager.OnChanged      -= this.Refresh;
        RankManager.OnChanged         -= this.Refresh;
        AccountLevelManager.OnChanged -= this.Refresh;
        this.StopLevelAnimation();
        this.m_hasShownExp = false;
    }
}
