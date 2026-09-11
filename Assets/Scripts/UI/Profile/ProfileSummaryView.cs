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

    [Tooltip("레벨이 오른 뒤 처음 열었을 때 게이지가 0에서 차오르는 시간.")]
    [SerializeField] float levelUpFillDuration = 0.25f;

    [Tooltip("레벨이 오른 뒤 처음 열었을 때 레벨 수치가 튀는 세기.")]
    [SerializeField] float levelUpPunch = UiPunch.DEFAULT_SCALE;

    // 마지막으로 화면에 세운 레벨. 판을 다시 열어도 같은 레벨업을 두 번 축하하지 않게 세션 동안 든다
    // (세이브가 아니다 — 앱을 다시 켜면 조용히 현재 레벨로 선다).
    static int s_shownLevel;

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

        if (this.levelText != null) this.levelText.text = string.Format(this.levelFormat, t_info.Level);
        if (this.expText != null) this.expText.text = string.Format(this.expFormat, t_info.ExpInLevel, t_info.ExpToNext);

        bool t_leveledUp = s_shownLevel > 0 && t_info.Level != s_shownLevel;
        s_shownLevel = t_info.Level;

        if (this.gauge == null) return;

        if (!t_leveledUp)
        {
            this.gauge.SetRatio(t_info.LevelProgress);
            return;
        }

        // 오른 사실을 드러내는 자리다 — 새 레벨의 게이지가 0에서 차오르고 수치가 한 번 튄다.
        this.gauge.SetRatio(0f);
        this.gauge.TweenTo(t_info.LevelProgress, this.levelUpFillDuration);
        if (this.levelText != null) UiPunch.Play(this.levelText.transform, this.levelUpPunch);
    }

    void OnEnable()
    {
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
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_shownLevel = 0;
}
