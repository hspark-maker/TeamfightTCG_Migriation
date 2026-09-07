using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 로비 설정 판의 프로필 요약(닉네임·랭크 티어명·계정 레벨)과 이름 편집.
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
    [Tooltip("닉네임 자리. 미배선이면 그 축만 건너뛴다.")]
    [SerializeField] TMP_Text nicknameText;

    [Tooltip("랭크 티어명 자리(\"브론즈 1\"). 미배선이면 그 축만 건너뛴다.")]
    [SerializeField] TMP_Text tierNameText;

    [Tooltip("랭크 티어 배지 자리. 미배선이면 그 축만 건너뛴다.\n" +
             "등급 표를 못 읽었거나 배지가 미저작이면 저작된 그림을 그대로 둔다.")]
    [SerializeField] Image rankBadgeImage;

    [Header("이름 편집")]
    [Tooltip("연필 버튼. 누르면 닉네임 라벨이 입력칸으로 바뀐다. 미배선이면 편집 길만 없다.")]
    [SerializeField] Button editNameButton;

    [Tooltip("닉네임 라벨과 같은 자리에 겹쳐 둔 입력칸. 편집 중에만 켜진다.")]
    [SerializeField] TMP_InputField nameInput;

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

    // 닉네임 라벨이 입력칸으로 바뀌어 있는 동안 참이다.
    bool m_editing;

    /// <summary>지금 값으로 다시 그린다.</summary>
    public void Refresh()
    {
        this.RefreshIdentity();
        this.RefreshLevel();
    }

    void RefreshIdentity()
    {
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

    // 닉네임 라벨을 입력칸으로 바꾸고 키보드를 올린다.
    void BeginNameEdit()
    {
        if (this.nameInput == null || this.m_editing) return;

        this.m_editing = true;

        this.nameInput.SetTextWithoutNotify(ProfileManager.Nickname);   // 세팅이 onValueChanged로 되튀지 않게
        this.SetEditing(true);
        this.nameInput.ActivateInputField();
    }

    // 편집을 끝내는 유일한 경로다. 엔터·소프트키보드 완료뿐 아니라 포커스를 잃거나 판이 닫히는 이탈도
    // 여기로 오며, 쓰던 이름을 버리지 않고 그대로 확정한다.
    void CommitNameEdit(string _value)
    {
        if (!this.m_editing) return;

        this.m_editing = false;

        if (this.nameInput != null) this.nameInput.DeactivateInputField();
        this.SetEditing(false);

        // 빈 이름은 개명으로 읽지 않는다 — 다 지운 채 나가면 정제기가 기본 닉네임으로 갈아치우므로,
        // 실수로 지우고 나간 경우에 지금 이름을 잃지 않도록 아예 저장을 건너뛴다.
        if (!string.IsNullOrWhiteSpace(_value))
        {
            ProfileManager.Apply(_value, ProfileManager.AvatarId, ProfileManager.FrameId);
        }

        // Apply는 정제한 값이 지금과 같으면 통지 없이 돌아온다 — 잘려 나간 글자가 라벨에 반영되도록 직접 그린다.
        // 레벨 축은 건드리지 않는다(진행 중인 레벨업 차오름을 잘라 먹지 않게).
        this.RefreshIdentity();
    }

    // 통지가 값을 실어 주지 않는 이탈(판이 닫히는 경우)에서 입력칸에 남아 있는 글자로 확정한다.
    void CommitNameEditFromInput() => this.CommitNameEdit(this.nameInput != null ? this.nameInput.text : null);

    void SetEditing(bool _editing)
    {
        if (this.nameInput != null) this.nameInput.gameObject.SetActive(_editing);
        if (this.nicknameText != null) this.nicknameText.gameObject.SetActive(!_editing);
    }

    void OnEnable()
    {
        ProfileManager.OnChanged      += this.Refresh;
        RankManager.OnChanged         += this.Refresh;
        AccountLevelManager.OnChanged += this.Refresh;

        Rewire(this.editNameButton, this.BeginNameEdit);

        if (this.nameInput != null)
        {
            this.nameInput.characterLimit = ProfileManager.NICKNAME_MAX_LENGTH;

            // 재활성마다 중복 등록 방지(ProfileEditPanel과 같은 관용구).
            this.nameInput.onSubmit.RemoveAllListeners();
            this.nameInput.onSubmit.AddListener(this.CommitNameEdit);

            // 이탈 경로도 같은 확정으로 묶는다. ESC와 안드로이드 백키는 onSubmit도 onDeselect도 거치지 않고
            // TMP의 DeactivateInputField로만 빠져나가는데, 그 경로가 부르는 것이 onEndEdit이다 —
            // 여기 걸지 않으면 나간 뒤에도 입력칸이 뜬 채 라벨이 숨겨진다.
            // 세 통지가 겹쳐 들어와도 m_editing 문지기가 첫 한 번만 통과시킨다.
            this.nameInput.onEndEdit.RemoveAllListeners();
            this.nameInput.onEndEdit.AddListener(this.CommitNameEdit);
            this.nameInput.onDeselect.RemoveAllListeners();
            this.nameInput.onDeselect.AddListener(this.CommitNameEdit);
        }

        this.SetEditing(false);   // 판은 언제나 라벨 상태로 열린다.
        this.Refresh();
    }

    void OnDisable()
    {
        ProfileManager.OnChanged      -= this.Refresh;
        RankManager.OnChanged         -= this.Refresh;
        AccountLevelManager.OnChanged -= this.Refresh;

        this.CommitNameEditFromInput();

        if (this.nameInput != null)
        {
            this.nameInput.onSubmit.RemoveAllListeners();
            this.nameInput.onEndEdit.RemoveAllListeners();
            this.nameInput.onDeselect.RemoveAllListeners();
        }
    }

    static void Rewire(Button _button, UnityEngine.Events.UnityAction _action)
    {
        if (_button == null) return;

        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener(_action);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_shownLevel = 0;
}
