using TMPro;
using UnityEngine;

/// 로비 설정 판의 계정 레벨 표시(레벨·게이지·경험치 수치).
///
/// AccountLevelManager.OnChanged를 구독하는 이유는 LobbyProfileButton과 같다: 이 판은 풀의 uiRoot에서
/// 로비 위를 덮으므로 판이 닫혀도 아래 탭의 OnEnable이 오지 않는다 — 통지가 유일한 갱신 신호다.
///
/// 곡선을 못 읽었으면 아무것도 쓰지 않고 저작값을 그대로 둔다. 빈 칸으로 만들면 "레벨 0"처럼 읽힌다.
public class AccountLevelView : MonoBehaviour
{
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

    /// <summary>지금 값으로 다시 그린다. 레벨이 지난번과 다르면 차오름 + 펀치를 한 번 얹는다.</summary>
    public void Refresh()
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
        AccountLevelManager.OnChanged += this.Refresh;
        this.Refresh();
    }

    void OnDisable()
    {
        AccountLevelManager.OnChanged -= this.Refresh;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_shownLevel = 0;
}
