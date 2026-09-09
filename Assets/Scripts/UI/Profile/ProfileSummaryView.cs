using Cysharp.Threading.Tasks;
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
    [Tooltip("현재 프로필의 아바타와 프레임을 표시하는 뷰.")]
    [SerializeField] ProfileAvatarView avatarView;

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

    [Tooltip("닉네임 입력란 위의 금칙어·길이 안내.")]
    [SerializeField] TMP_Text nameValidationText;

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
    int m_nameEditGeneration;
    int m_lengthRejectedFrame = -1;

    const string BLOCKED_NAME_MESSAGE = "사용할 수 없는 이름입니다";
    static readonly string LENGTH_MESSAGE = $"닉네임은 최대 {ProfileManager.NICKNAME_MAX_LENGTH}자입니다";

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

    // 닉네임 라벨을 입력칸으로 바꾸고 키보드를 올린다.
    void BeginNameEdit()
    {
        if (this.nameInput == null || this.m_editing) return;

        this.m_editing = true;
        this.m_nameEditGeneration++;
        this.ClearNameValidation();

        this.nameInput.SetTextWithoutNotify(ProfileManager.Nickname);   // 세팅이 onValueChanged로 되튀지 않게
        this.SetEditing(true);
        this.nameInput.ActivateInputField();
    }

    // TMP 리스너(onSubmit·onEndEdit·onDeselect)가 거는 자리다. 판이 살아 있는 확정이라 거절을 알릴 수 있다.
    void CommitNameEditFromField(string _value) => this.CommitNameEdit(_value, true);

    // 편집을 끝내는 유일한 경로다. 엔터·소프트키보드 완료뿐 아니라 포커스를 잃거나 판이 닫히는 이탈도
    // 여기로 오며, 쓰던 이름을 버리지 않고 그대로 확정한다.
    //
    // _canPrompt 는 거절을 알릴 수 있는 자리인지를 가른다. 판이 닫히는 이탈에서는 안내를 띄워도 그 뒤에
    // 고쳐 쓸 화면이 남지 않으므로, 조용히 저장만 건너뛰고 예전 이름을 지킨다.
    void CommitNameEdit(string _value, bool _canPrompt)
    {
        if (!this.m_editing) return;

        bool t_blocked = ProfileManager.IsNicknameBlocked(_value);

        // 쓸 수 없는 이름이면 적던 글자를 남긴 채 입력란 위에 안내하고, 다음 프레임에 포커스를 복원한다.
        // 문지기를 여기서도 내리는 이유: 엔터 한 번에 onSubmit → onEndEdit → onDeselect 가 잇달아 들어오는데,
        // 열어 두면 같은 거절로 안내가 세 번 다시 세워진다.
        if (_canPrompt && t_blocked)
        {
            this.m_editing = false;
            this.ShowNameValidation(BLOCKED_NAME_MESSAGE);
            this.ResumeNameEditAsync(this.m_nameEditGeneration).Forget();
            return;
        }

        this.m_editing = false;
        this.m_nameEditGeneration++;
        this.ClearNameValidation();

        if (this.nameInput != null) this.nameInput.DeactivateInputField();
        this.SetEditing(false);

        // 빈 이름은 개명으로 읽지 않는다 — 다 지운 채 나가면 정제기가 기본 닉네임으로 갈아치우므로,
        // 실수로 지우고 나간 경우에 지금 이름을 잃지 않도록 아예 저장을 건너뛴다.
        // 막힌 이름도 같은 이유로 건너뛴다(판이 닫히는 이탈에서만 여기까지 온다).
        if (!string.IsNullOrWhiteSpace(_value) && !t_blocked)
        {
            ProfileManager.Apply(_value, ProfileManager.AvatarId, ProfileManager.FrameId, ProfileManager.EmoteIds);
        }

        // Apply는 정제한 값이 지금과 같으면 통지 없이 돌아온다 — 잘려 나간 글자가 라벨에 반영되도록 직접 그린다.
        // 레벨 축은 건드리지 않는다(진행 중인 레벨업 차오름을 잘라 먹지 않게).
        this.RefreshIdentity();
    }

    // TMP의 submit/endEdit/deselect 연속 처리가 끝난 뒤 입력 포커스를 복원한다.
    async UniTaskVoid ResumeNameEditAsync(int _generation)
    {
        await UniTask.NextFrame();

        if (this == null || this.nameInput == null || !this.isActiveAndEnabled ||
            _generation != this.m_nameEditGeneration) return;

        this.m_editing = true;
        this.SetEditing(true);
        this.nameInput.ActivateInputField();
    }

    char ValidateNameCharacter(string _text, int _position, char _character)
    {
        // TMP는 선택 영역을 제거한 문자열을 넘긴다. 모바일 키보드는 전체 문자열을 순서대로 검증한다.
        if (_text.Length < ProfileManager.NICKNAME_MAX_LENGTH || _character == '\n' || _character == '\r')
            return _character;

        this.m_lengthRejectedFrame = Time.frameCount;
        this.ShowNameValidation(LENGTH_MESSAGE);
        return '\0';
    }

    void OnNameValueChanged(string _value)
    {
        // 직접 text를 대입하는 경로도 제한을 지킨다. 일반 입력·붙여넣기는 위의 검증에서 잘린다.
        if (_value.Length > ProfileManager.NICKNAME_MAX_LENGTH)
        {
            this.nameInput.SetTextWithoutNotify(_value.Substring(0, ProfileManager.NICKNAME_MAX_LENGTH));
            this.m_lengthRejectedFrame = Time.frameCount;
            this.ShowNameValidation(LENGTH_MESSAGE);
            return;
        }

        // 모바일은 검증 루프 뒤에 변경 이벤트를 보내므로 같은 입력에서 띄운 초과 안내를 보존한다.
        if (this.m_lengthRejectedFrame != Time.frameCount) this.ClearNameValidation();
    }

    void ShowNameValidation(string _message)
    {
        if (this.nameValidationText == null) return;
        this.nameValidationText.text = _message;
        this.nameValidationText.gameObject.SetActive(true);
    }

    void ClearNameValidation()
    {
        this.m_lengthRejectedFrame = -1;
        if (this.nameValidationText == null) return;
        this.nameValidationText.text = string.Empty;
        this.nameValidationText.gameObject.SetActive(false);
    }

    // 통지가 값을 실어 주지 않는 이탈(판이 닫히는 경우)에서 입력칸에 남아 있는 글자로 확정한다.
    void CommitNameEditFromInput() => this.CommitNameEdit(this.nameInput != null ? this.nameInput.text : null, false);

    void SetEditing(bool _editing)
    {
        if (this.nameInput != null) this.nameInput.gameObject.SetActive(_editing);
        if (this.nicknameText != null) this.nicknameText.gameObject.SetActive(!_editing);
    }

    void OnEnable()
    {
        this.m_nameEditGeneration++;
        this.m_editing = false;
        this.ClearNameValidation();
        ProfileManager.OnChanged      += this.Refresh;
        RankManager.OnChanged         += this.Refresh;
        AccountLevelManager.OnChanged += this.Refresh;

        Rewire(this.editNameButton, this.BeginNameEdit);

        if (this.nameInput != null)
        {
            // 기본 제한은 모바일 키보드가 초과 시도를 전달하지 않으므로 사용자 검증으로 제한한다.
            this.nameInput.characterLimit = 0;
            this.nameInput.onValidateInput += this.ValidateNameCharacter;
            this.nameInput.onValueChanged.AddListener(this.OnNameValueChanged);

            // 재활성마다 중복 등록 방지(ProfileEditPanel과 같은 관용구).
            this.nameInput.onSubmit.RemoveAllListeners();
            this.nameInput.onSubmit.AddListener(this.CommitNameEditFromField);

            // 이탈 경로도 같은 확정으로 묶는다. ESC와 안드로이드 백키는 onSubmit도 onDeselect도 거치지 않고
            // TMP의 DeactivateInputField로만 빠져나가는데, 그 경로가 부르는 것이 onEndEdit이다 —
            // 여기 걸지 않으면 나간 뒤에도 입력칸이 뜬 채 라벨이 숨겨진다.
            // 세 통지가 겹쳐 들어와도 m_editing 문지기가 첫 한 번만 통과시킨다.
            this.nameInput.onEndEdit.RemoveAllListeners();
            this.nameInput.onEndEdit.AddListener(this.CommitNameEditFromField);
            this.nameInput.onDeselect.RemoveAllListeners();
            this.nameInput.onDeselect.AddListener(this.CommitNameEditFromField);
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
        this.m_nameEditGeneration++;
        this.m_editing = false;
        this.ClearNameValidation();

        if (this.nameInput != null)
        {
            this.nameInput.onValidateInput -= this.ValidateNameCharacter;
            this.nameInput.onValueChanged.RemoveListener(this.OnNameValueChanged);
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
