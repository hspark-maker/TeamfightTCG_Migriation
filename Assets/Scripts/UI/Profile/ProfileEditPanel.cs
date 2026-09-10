using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// 프로필 편집 팝업(아바타·프레임·감정표현·닉네임). 풀(UIPoolManager)이 수명을 쥐고 로비 위에 덮인다.
//
// 편집 중에는 드래프트만 바꾸고 저장·닫기·외부 숨김 시 ProfileManager.Apply로 한 번 확정한다.
public class ProfileEditPanel : PooledUIBase, IPointerClickHandler
{
    const int TAB_AVATAR = 0;
    const int TAB_FRAME  = 1;
    const int TAB_EMOTE  = 2;

    // 표시값은 ProfileManager에서 읽고, 호출 화면의 복귀 동작만 전달받는다.
    public override void Initialization(UIData _data) => this.data = _data;
    public override void Show() => this.Open();
    public override void Hide()
    {
        this.CancelEmoteDrag();
        // 풀 정리 등 외부 숨김에서는 호출 화면을 다시 열지 않는다.
        this.data = null;
        this.isShow = false;
        this.CommitSession();
        if (this.nicknameInput != null) this.nicknameInput.DeactivateInputField();
        this.SetVisible(false);
    }

    [Header("미리보기")]
    [Tooltip("판·얼굴·링 한 덩어리. 팝업 안에서만 드래프트를 즉시 반영한다.")]
    [SerializeField] ProfileAvatarView previewView;
    [SerializeField] TMP_InputField nicknameInput;
    [Tooltip("연필 버튼. 누르기 전까지 닉네임 입력은 라벨처럼 잠겨 있다.")]
    [SerializeField] Button pencilButton;
    [SerializeField] TMP_Text nameValidationText;

    [Header("탭")]
    [SerializeField] TabButtonView avatarTab;
    [SerializeField] TabButtonView frameTab;
    [SerializeField] TabButtonView emoteTab;
    [SerializeField] GameObject avatarPanel;
    [SerializeField] GameObject framePanel;
    [SerializeField] GameObject emotePanel;
    [SerializeField] GameObject profileViewRoot;
    [SerializeField] GameObject equippedEmotePanel;

    [Header("아바타·프레임 그리드")]
    [SerializeField] ScrollRect avatarScroll;
    [SerializeField] Transform avatarContent;
    [SerializeField] ScrollRect frameScroll;
    [SerializeField] Transform frameContent;
    [Tooltip("아바타·프레임이 함께 쓰는 칸 프리팹.")]
    [SerializeField] ProfileItemCell cellPrefab;

    [Header("감정표현")]
    [SerializeField] ScrollRect emoteScroll;
    [Tooltip("장착 줄(SLOT_COUNT칸). 평상시 탭은 해제, 목록 선택 후 탭은 교체.")]
    [SerializeField] Transform equippedEmoteContent;
    [Tooltip("이모티콘 목록. 첫 빈칸에 장착하고, 가득 차면 교체할 슬롯을 고른다.")]
    [SerializeField] Transform emoteContent;
    [Tooltip("장착 줄과 풀이 함께 쓰는 칸 프리팹.")]
    [SerializeField] EmoteItemCell emoteCellPrefab;

    [Header("확정")]
    [SerializeField] Button saveButton;
    [SerializeField] Button closeButton;
    [Tooltip("바깥 암막 클릭 판정. 닫기와 같은 동작이다.")]
    [SerializeField] Button dimButton;

    [Header("연출")]
    [Tooltip("panel에는 팝업 창을 배선한다 — contents를 물리면 전체화면 딤까지 함께 커진다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("공용 ScreenDim(Full)에 요청할 암막 짙기.")]
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.72f;

    readonly List<ProfileItemCell> m_avatarCells = new List<ProfileItemCell>();
    readonly List<ProfileItemCell> m_frameCells = new List<ProfileItemCell>();
    readonly List<EmoteItemCell> m_equippedEmoteCells = new List<EmoteItemCell>();
    readonly List<EmoteItemCell> m_emoteCells = new List<EmoteItemCell>();
    readonly List<int> m_draftEmoteIds = new List<int>(EmoteCatalog.SLOT_COUNT);

    // 칸 생성 여부. 목록은 런타임 불변이라 최초 1회만 만들고 이후엔 선택 표시만 갱신한다.
    bool m_built;
    EmoteEditDragController m_emoteDrag;
    int m_currentTab;
    bool m_sessionOpen;
    bool m_editingNickname;
    int m_nameEditGeneration;
    int m_lengthRejectedFrame = -1;

    const string BLOCKED_NAME_MESSAGE = "사용할 수 없는 이름입니다";
    const string EMPTY_NAME_MESSAGE = "닉네임을 입력해 주세요";
    static readonly string LENGTH_MESSAGE = $"닉네임은 최대 {ProfileManager.NICKNAME_MAX_LENGTH}자입니다";

    // 임시 선택. 저장 전까지 ProfileManager에는 아무것도 넘어가지 않는다.
    string m_draftAvatarId;
    string m_draftFrameId;
    string m_draftNickname;
    // 만석일 때 목록에서 고른 교체 후보. 0이면 평상시 편집.
    int m_pendingEmoteId;
    bool m_dragSwapVisual;

    /// <summary>드래프트를 현재 프로필로 리셋하고 팝업을 띄운다.</summary>
    public void Open()
    {
        if (this.m_sessionOpen) return;
        this.m_draftAvatarId = ProfileManager.AvatarId;
        this.m_draftFrameId = ProfileManager.FrameId;
        this.m_draftNickname = ProfileManager.Nickname;
        this.m_draftEmoteIds.Clear();
        for (int t_i = 0; t_i < ProfileManager.EmoteIds.Count; t_i++)
            this.m_draftEmoteIds.Add(ProfileManager.EmoteIds[t_i]);
        while (this.m_draftEmoteIds.Count < EmoteCatalog.SLOT_COUNT) this.m_draftEmoteIds.Add(0);
        this.m_pendingEmoteId = 0;

        if (!this.m_built) this.Build();

        this.SetTab(TAB_AVATAR);           // 열 때마다 아바타 탭부터 — 이전 세션의 탭이 남지 않게.
        this.RefreshSelection();
        this.RefreshPreview();
        this.RefreshNicknameField();
        if (this.saveButton != null) this.saveButton.interactable = false;
        this.m_sessionOpen = true;
        this.SetVisible(true);
    }

    /// <summary>드래프트를 저장하고 닫은 뒤 호출 화면으로 복귀한다.</summary>
    public void Close()
    {
        if (!this.m_sessionOpen) return;
        var t_onHide = this.data?.onHide;
        this.Hide();
        t_onHide?.Invoke();
    }

    void OnEnable()
    {
        // 재활성마다 중복 등록 방지.
        Rewire(this.saveButton, this.Save);
        Rewire(this.closeButton, this.Close);
        Rewire(this.dimButton, this.Close);
        Rewire(this.pencilButton, this.BeginNicknameEdit);

        // 탭 버튼은 겉모습만 담당하는 뷰다 — 클릭은 같은 오브젝트의 Button에서 코드로 문다(LobbyTabController와 같은 관용구).
        Rewire(FindButton(this.avatarTab), this.ShowAvatarTab);
        Rewire(FindButton(this.frameTab), this.ShowFrameTab);
        Rewire(FindButton(this.emoteTab), this.ShowEmoteTab);

        if (this.nicknameInput != null)
        {
            // 기본 상한 대신 검증기로 제한해 초과 입력 시에도 안내한다.
            this.nicknameInput.characterLimit = 0;
            this.nicknameInput.onValidateInput -= this.ValidateNameCharacter;
            this.nicknameInput.onValidateInput += this.ValidateNameCharacter;
            this.nicknameInput.onSubmit.RemoveListener(this.EndNicknameEdit);
            this.nicknameInput.onSubmit.AddListener(this.EndNicknameEdit);
            this.nicknameInput.onEndEdit.RemoveListener(this.EndNicknameEdit);
            this.nicknameInput.onEndEdit.AddListener(this.EndNicknameEdit);
            this.nicknameInput.onDeselect.RemoveListener(this.EndNicknameEdit);
            this.nicknameInput.onDeselect.AddListener(this.EndNicknameEdit);
            this.nicknameInput.onValueChanged.RemoveAllListeners();
            this.nicknameInput.onValueChanged.AddListener(this.OnNicknameChanged);
        }
    }

    void OnDisable()
    {
        this.CancelEmoteDrag();
        this.data = null;
        this.isShow = false;
        this.CommitSession();
        // 소프트키보드가 팝업 밖까지 살아남지 않게 — 팝업이 풀에서 꺼지는 경로는 Close를 거치지 않는다.
        if (this.nicknameInput != null)
        {
            this.nicknameInput.onValueChanged.RemoveListener(this.OnNicknameChanged);
            this.nicknameInput.onValidateInput -= this.ValidateNameCharacter;
            this.nicknameInput.onSubmit.RemoveListener(this.EndNicknameEdit);
            this.nicknameInput.onEndEdit.RemoveListener(this.EndNicknameEdit);
            this.nicknameInput.onDeselect.RemoveListener(this.EndNicknameEdit);
            this.nicknameInput.DeactivateInputField();
        }

        // 안전망 — Close를 거치지 않고 꺼지면 공용 딤이 남는다.
        ScreenDim.Hide(this);
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    // 세 그리드를 한 번에 세운다. 설정이 아직 없으면(초기화 배선 전) 그 축만 조용히 비운다 — 씬이 죽지 않게.
    void Build()
    {
        this.CancelEmoteDrag();
        if (this.m_emoteDrag == null)
            this.m_emoteDrag = this.GetComponent<EmoteEditDragController>() ?? this.gameObject.AddComponent<EmoteEditDragController>();
        this.m_emoteDrag.Initialize((RectTransform)this.ResolveTarget().transform, this.m_equippedEmoteCells, this.OnEmoteDropped, this.OnEmoteDragEnded);
        this.m_avatarCells.Clear();
        this.m_frameCells.Clear();
        this.m_equippedEmoteCells.Clear();
        this.m_emoteCells.Clear();
        this.ClearContent(this.avatarContent, this.cellPrefab != null ? this.cellPrefab.gameObject : null);
        this.ClearContent(this.frameContent, this.cellPrefab != null ? this.cellPrefab.gameObject : null);
        this.ClearContent(this.equippedEmoteContent, this.emoteCellPrefab != null ? this.emoteCellPrefab.gameObject : null);
        this.ClearContent(this.emoteContent, this.emoteCellPrefab != null ? this.emoteCellPrefab.gameObject : null);

        ProfileConfig t_config = ProfileManager.Config;
        if (t_config != null && this.cellPrefab != null)
        {
            var t_avatars = t_config.Avatars;
            for (int t_i = 0; t_i < t_avatars.Count; t_i++)
            {
                var t_entry = t_avatars[t_i];
                if (t_entry == null) continue;
                ProfileItemCell t_cell = this.CreateProfileCell(this.avatarContent);
                if (t_cell == null) continue;
                // 아바타 칸은 아바타 그림만 보여준다 — 프레임과 겹친 실제 조합은 위쪽 미리보기가 맡는다.
                t_cell.Bind(t_entry.id, t_config.LookOf(t_entry.id, null), EProfileAxis.Avatar,
                    ProfileManager.IsAvatarOwned(t_entry.id), this.OnAvatarClicked);
                this.m_avatarCells.Add(t_cell);
            }

            var t_frames = t_config.Frames;
            for (int t_i = 0; t_i < t_frames.Count; t_i++)
            {
                var t_entry = t_frames[t_i];
                if (t_entry == null) continue;
                ProfileItemCell t_cell = this.CreateProfileCell(this.frameContent);
                if (t_cell == null) continue;
                // 프레임 칸은 링만 보여준다.
                t_cell.Bind(t_entry.id, t_config.LookOf(null, t_entry.id), EProfileAxis.Frame,
                    ProfileManager.IsFrameOwned(t_entry.id), this.OnFrameClicked);
                this.m_frameCells.Add(t_cell);
            }
        }

        EmoteCatalog t_catalog = ProfileManager.EmoteCatalog;
        if (t_catalog != null && this.emoteCellPrefab != null)
        {
            // 장착 줄은 칸 수가 고정이라 빈 채로 먼저 세우고 내용은 RefreshEmotes가 채운다.
            for (int t_i = 0; t_i < EmoteCatalog.SLOT_COUNT; t_i++)
            {
                EmoteItemCell t_cell = this.CreateEmoteCell(this.equippedEmoteContent);
                if (t_cell != null) this.m_equippedEmoteCells.Add(t_cell);
            }

            // 풀은 저작 순서대로 깔되 id 미저작(0) 칸은 건너뛴다 — 눌러도 장착할 수 없는 칸이라.
            for (int t_i = 0; t_i < t_catalog.Count; t_i++)
            {
                EmoteEntry t_entry = t_catalog.PoolAt(t_i);
                if (t_entry == null || t_entry.id <= 0) continue;
                EmoteItemCell t_cell = this.CreateEmoteCell(this.emoteContent);
                if (t_cell == null) continue;
                t_cell.BindEmote(t_entry.id, t_entry, ProfileManager.IsEmoteOwned(t_entry.id), this.OnEmoteClicked);
                this.m_emoteCells.Add(t_cell);
            }
        }

        // 이전 세션의 스크롤 위치가 남아 첫 화면이 중간부터 보이는 것을 막는다.
        ResetScroll(this.avatarScroll);
        ResetScroll(this.frameScroll);
        ResetScroll(this.emoteScroll);
        // 하나도 못 만들었으면 다음 열기에서 다시 시도한다 — 빈 팝업으로 세션 내내 고착되지 않게.
        this.m_built = this.m_avatarCells.Count > 0 || this.m_frameCells.Count > 0 || this.m_emoteCells.Count > 0;
    }

    ProfileItemCell CreateProfileCell(Transform _content)
    {
        if (_content == null || this.cellPrefab == null) return null;
        ProfileItemCell t_cell = Instantiate(this.cellPrefab, _content);
        t_cell.gameObject.SetActive(true);
        return t_cell;
    }

    EmoteItemCell CreateEmoteCell(Transform _content)
    {
        if (_content == null || this.emoteCellPrefab == null) return null;
        EmoteItemCell t_cell = Instantiate(this.emoteCellPrefab, _content);
        t_cell.ConfigureDrag(this.OnEmoteDragRequested);
        t_cell.gameObject.SetActive(true);
        return t_cell;
    }

    // 목업으로 저작된 칸을 걷는다. 칸 프리팹이 Content 안의 템플릿으로 배선된 저작도 허용해야 하므로
    // 원본은 지우지 않고 숨기기만 한다(지우면 다음 Build가 칸 0개).
    void ClearContent(Transform _content, GameObject _template)
    {
        if (_content == null) return;
        for (int t_i = _content.childCount - 1; t_i >= 0; t_i--)
        {
            GameObject t_child = _content.GetChild(t_i).gameObject;
            t_child.SetActive(false);
            if (t_child != _template) Destroy(t_child);
        }
    }

    void OnAvatarClicked(string _id)
    {
        this.m_draftAvatarId = _id;
        SetSelectedIn(this.m_avatarCells, _id);
        this.RefreshPreview();
        this.RefreshSaveButton();
    }

    void OnFrameClicked(string _id)
    {
        this.m_draftFrameId = _id;
        SetSelectedIn(this.m_frameCells, _id);
        this.RefreshPreview();
        this.RefreshSaveButton();
    }

    // 덱 편집과 동일하게 평상시 해제, 후보를 고른 뒤에는 교체한다.
    void OnEquippedEmoteClicked(int _slot)
    {
        if (!this.m_sessionOpen || _slot < 0 || _slot >= this.m_draftEmoteIds.Count) return;
        if (this.m_emoteDrag != null && this.m_emoteDrag.IsDragging) return;
        if (this.m_pendingEmoteId > 0)
        {
            int t_id = this.m_pendingEmoteId;
            this.CancelEmotePick(true);
            if (_slot < this.m_equippedEmoteCells.Count && this.m_equippedEmoteCells[_slot] != null)
            {
                EmoteItemCell t_cell = this.m_equippedEmoteCells[_slot];
                RectTransform t_list = this.emoteScroll != null && this.emoteScroll.viewport != null
                    ? this.emoteScroll.viewport : this.emoteContent as RectTransform;
                Vector2 t_size = this.m_emoteCells.Count > 0 ? this.m_emoteCells[0].Rect.rect.size : t_cell.Rect.rect.size;
                this.m_emoteDrag?.FlyOut(t_cell.Sprite, t_cell.Rect, t_list, t_size);
            }
            this.AssignEmote(_slot, t_id);
            if (_slot < this.m_equippedEmoteCells.Count) this.m_equippedEmoteCells[_slot]?.PlayEquipPunch();
            return;
        }
        if (this.m_draftEmoteIds[_slot] == 0) return;
        this.m_draftEmoteIds[_slot] = 0;
        this.RefreshEmotes();
        this.RefreshSaveButton();
    }

    // 첫 빈칸 자동 장착. 만석이면 슬롯 선택을 기다린다.
    void OnEmoteClicked(int _id)
    {
        if (!this.m_sessionOpen || this.m_draftEmoteIds.Contains(_id) || !ProfileManager.IsEmoteOwned(_id)) return;
        if (this.m_emoteDrag != null && this.m_emoteDrag.IsDragging) return;
        if (this.m_pendingEmoteId == _id) { this.CancelEmotePick(); return; }
        int t_empty = this.m_draftEmoteIds.IndexOf(0);
        if (t_empty >= 0) { this.AssignEmote(t_empty, _id); return; }
        this.m_pendingEmoteId = _id;
        this.RefreshEmoteFocus();
    }

    void AssignEmote(int _slot, int _id)
    {
        EmoteCatalog t_catalog = ProfileManager.EmoteCatalog;
        if (t_catalog == null || !t_catalog.TryGet(_id, out _) || !ProfileManager.IsEmoteOwned(_id)) return;
        if (_slot < 0 || _slot >= this.m_draftEmoteIds.Count) return;

        if (this.m_draftEmoteIds.Contains(_id)) return;
        this.m_draftEmoteIds[_slot] = _id;
        this.CancelEmotePick(true);
        this.RefreshEmotes();
        this.RefreshSaveButton();
    }

    void OnEmoteDragRequested(EmoteItemCell _cell, UnityEngine.EventSystems.PointerEventData _pointer)
    {
        if (!this.m_sessionOpen || this.m_currentTab != TAB_EMOTE) return;
        if (_cell == null || _cell.IsSlot || this.m_draftEmoteIds.Contains(_cell.EmoteId) || !ProfileManager.IsEmoteOwned(_cell.EmoteId)) return;
        this.CancelEmotePick(true);
        this.m_emoteDrag.Begin(_cell, _pointer, this.emoteScroll);
        this.m_dragSwapVisual = this.m_emoteDrag.IsDragging && !this.m_draftEmoteIds.Contains(0);
        this.RefreshEmoteFocus();
    }

    void OnEmoteDropped(int _slot, int _id, int _sourceSlot)
    {
        if (!this.m_sessionOpen || this.m_currentTab != TAB_EMOTE) return;
        if (_sourceSlot >= 0) return;
        this.AssignEmote(_slot, _id);
    }

    void CancelEmoteDrag()
    {
        if (this.m_emoteDrag != null) this.m_emoteDrag.Cancel();
        this.m_dragSwapVisual = false;
        this.CancelEmotePick(true);
        foreach (EmoteItemCell t_cell in this.m_equippedEmoteCells) if (t_cell != null) t_cell.CancelGesture();
        foreach (EmoteItemCell t_cell in this.m_emoteCells) if (t_cell != null) t_cell.CancelGesture();
    }

    void OnEmoteDragEnded()
    {
        this.m_dragSwapVisual = false;
        this.RefreshEmoteFocus(true);
    }

    void CancelEmotePick(bool _instant = false)
    {
        this.m_pendingEmoteId = 0;
        this.RefreshEmoteFocus(_instant);
    }

    void RefreshEmoteFocus(bool _instant = false)
    {
        bool t_picking = this.m_pendingEmoteId > 0;
        foreach (EmoteItemCell t_cell in this.m_equippedEmoteCells)
            if (t_cell != null) t_cell.SetSwapTarget(t_picking || this.m_dragSwapVisual, _instant);
        foreach (EmoteItemCell t_cell in this.m_emoteCells)
            if (t_cell != null) t_cell.SetFocus(t_picking, t_cell.Key == this.m_pendingEmoteId);
    }

    public void OnPointerClick(PointerEventData _pointer)
    {
        if (_pointer != null && _pointer.button != PointerEventData.InputButton.Left) return;
        if (this.m_currentTab == TAB_EMOTE) this.CancelEmotePick();
    }

    void OnApplicationFocus(bool _focused) { if (!_focused) this.CancelEmoteDrag(); }

    void OnNicknameChanged(string _value)
    {
        if (!this.m_sessionOpen) return;
        if (_value.Length > ProfileManager.NICKNAME_MAX_LENGTH)
        {
            _value = _value.Substring(0, ProfileManager.NICKNAME_MAX_LENGTH);
            this.nicknameInput.SetTextWithoutNotify(_value);
            this.m_lengthRejectedFrame = Time.frameCount;
            this.ShowNameValidation(LENGTH_MESSAGE);
        }
        this.m_draftNickname = _value;
        if (this.m_lengthRejectedFrame != Time.frameCount) this.RefreshNameValidation();
        this.RefreshSaveButton();
    }

    void BeginNicknameEdit()
    {
        if (!this.m_sessionOpen || this.nicknameInput == null || this.m_editingNickname) return;
        this.m_editingNickname = true;
        this.m_nameEditGeneration++;
        this.ClearNameValidation();
        this.nicknameInput.interactable = true;
        this.nicknameInput.ActivateInputField();
    }

    void EndNicknameEdit(string _value)
    {
        if (!this.m_sessionOpen || !this.m_editingNickname) return;
        // 기존 계정의 긴 이름을 편집 없이 확정하면 그대로 보존한다.
        if (_value != this.m_draftNickname) this.OnNicknameChanged(_value);
        this.m_editingNickname = false;
        if (string.IsNullOrWhiteSpace(this.m_draftNickname) || ProfileManager.IsNicknameBlocked(this.m_draftNickname))
        {
            this.RefreshNameValidation();
            this.ResumeNicknameEditAsync(this.m_nameEditGeneration).Forget();
            return;
        }
        this.m_nameEditGeneration++;
        this.LockNicknameInputAsync(this.m_nameEditGeneration).Forget();
    }

    async UniTaskVoid LockNicknameInputAsync(int _generation)
    {
        // OnDeselect 안에서 interactable을 내리면 선택 해제가 재진입한다.
        // 현재 선택 전환이 끝난 뒤 잠그되, 그 사이 시작한 편집이나 종료한 세션은 건드리지 않는다.
        await UniTask.NextFrame();
        if (this == null || !this.isActiveAndEnabled || !this.m_sessionOpen ||
            this.nicknameInput == null || this.m_editingNickname ||
            _generation != this.m_nameEditGeneration) return;
        this.nicknameInput.interactable = false;
    }

    async UniTaskVoid ResumeNicknameEditAsync(int _generation)
    {
        await UniTask.NextFrame();
        if (this == null || !this.isActiveAndEnabled || !this.m_sessionOpen ||
            this.nicknameInput == null || _generation != this.m_nameEditGeneration) return;
        this.m_editingNickname = true;
        this.nicknameInput.interactable = true;
        this.nicknameInput.ActivateInputField();
    }

    char ValidateNameCharacter(string _text, int _position, char _character)
    {
        if (_text.Length < ProfileManager.NICKNAME_MAX_LENGTH || _character == '\n' || _character == '\r')
            return _character;
        this.m_lengthRejectedFrame = Time.frameCount;
        this.ShowNameValidation(LENGTH_MESSAGE);
        return '\0';
    }

    void RefreshNameValidation()
    {
        if (string.IsNullOrWhiteSpace(this.m_draftNickname)) this.ShowNameValidation(EMPTY_NAME_MESSAGE);
        else if (ProfileManager.IsNicknameBlocked(this.m_draftNickname)) this.ShowNameValidation(BLOCKED_NAME_MESSAGE);
        else this.ClearNameValidation();
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

    void Save() => this.Close();

    // 모든 종료 경로가 이 문지기를 지난다. Apply 통지의 재진입 전에 세션을 닫는다.
    void CommitSession()
    {
        if (!this.m_sessionOpen) return;
        this.m_sessionOpen = false;
        this.m_editingNickname = false;
        this.m_nameEditGeneration++;
        if (this.nicknameInput != null) this.m_draftNickname = this.nicknameInput.text;
        string t_nickname = string.IsNullOrWhiteSpace(this.m_draftNickname)
            || ProfileManager.IsNicknameBlocked(this.m_draftNickname)
            ? ProfileManager.Nickname : this.m_draftNickname;
        ProfileManager.Apply(t_nickname, this.m_draftAvatarId, this.m_draftFrameId, this.m_draftEmoteIds);
    }

    // 공용 탭 컨트롤러가 없어 여기서 직접 토글한다(DeckTabController와 같은 관용구).
    void SetTab(int _tab)
    {
        this.CancelEmoteDrag();
        this.m_currentTab = _tab;
        if (this.profileViewRoot != null) this.profileViewRoot.SetActive(_tab != TAB_EMOTE);
        if (this.equippedEmotePanel != null) this.equippedEmotePanel.SetActive(_tab == TAB_EMOTE);
        if (this.avatarPanel != null) this.avatarPanel.SetActive(_tab == TAB_AVATAR);
        if (this.framePanel != null) this.framePanel.SetActive(_tab == TAB_FRAME);
        if (this.emotePanel != null) this.emotePanel.SetActive(_tab == TAB_EMOTE);
        if (this.avatarTab != null) this.avatarTab.SetSelected(_tab == TAB_AVATAR);
        if (this.frameTab != null) this.frameTab.SetSelected(_tab == TAB_FRAME);
        if (this.emoteTab != null) this.emoteTab.SetSelected(_tab == TAB_EMOTE);
    }

    void ShowAvatarTab() => this.SetTab(TAB_AVATAR);
    void ShowFrameTab() => this.SetTab(TAB_FRAME);
    void ShowEmoteTab() => this.SetTab(TAB_EMOTE);

    void RefreshSelection()
    {
        SetSelectedIn(this.m_avatarCells, this.m_draftAvatarId);
        SetSelectedIn(this.m_frameCells, this.m_draftFrameId);
        this.RefreshEmotes();
    }

    // 장착 상태를 드래프트에서 재생성한 뒤 교체 후보 강조를 적용한다.
    void RefreshEmotes()
    {
        EmoteCatalog t_catalog = ProfileManager.EmoteCatalog;
        for (int t_i = 0; t_i < this.m_equippedEmoteCells.Count; t_i++)
        {
            int t_id = t_i < this.m_draftEmoteIds.Count ? this.m_draftEmoteIds[t_i] : 0;
            EmoteEntry t_entry = null;
            if (t_catalog != null) t_catalog.TryGet(t_id, out t_entry);

            EmoteItemCell t_cell = this.m_equippedEmoteCells[t_i];
            if (t_cell == null) continue;
            t_cell.BindSlot(t_i, t_entry, this.OnEquippedEmoteClicked);
        }

        for (int t_i = 0; t_i < this.m_emoteCells.Count; t_i++)
        {
            EmoteItemCell t_cell = this.m_emoteCells[t_i];
            if (t_cell == null) continue;
            t_cell.SetEquipped(this.m_draftEmoteIds.Contains(t_cell.Key));
        }
        this.RefreshEmoteFocus(true);
    }

    // 미리보기 줄만 즉시 반영한다 — 팝업 밖(로비 버튼 등)은 저장 전까지 예전 값 그대로다.
    void RefreshPreview()
    {
        ProfileConfig t_config = ProfileManager.Config;
        if (t_config != null && this.previewView != null)
            this.previewView.Render(t_config.LookOf(this.m_draftAvatarId, this.m_draftFrameId));
    }

    void RefreshNicknameField()
    {
        this.m_editingNickname = false;
        this.m_nameEditGeneration++;
        this.ClearNameValidation();
        if (this.nicknameInput == null) return;
        this.nicknameInput.SetTextWithoutNotify(this.m_draftNickname);   // 세팅이 onValueChanged로 되튀지 않게
        this.nicknameInput.interactable = false;                          // 연필을 누르기 전까지는 라벨
    }

    void RefreshSaveButton()
    {
        if (this.saveButton != null) this.saveButton.interactable = this.IsDirty;
    }

    // 넷 중 하나라도 현재 프로필과 다르면 저장할 것이 있다.
    bool IsDirty =>
        this.m_draftAvatarId != ProfileManager.AvatarId
        || this.m_draftFrameId != ProfileManager.FrameId
        || this.m_draftNickname != ProfileManager.Nickname
        || !LoadoutsEqual(this.m_draftEmoteIds, ProfileManager.EmoteIds);

    void SetVisible(bool _visible)
    {
        // 암막은 공용 ScreenDim(Full)이 그린다 — 팝업마다 딤 한 장씩 들고 있지 않는다.
        if (_visible) ScreenDim.Show(this, this.dimAlpha, true, this.transition.OpenDuration);
        else ScreenDim.Hide(this);

        this.isShow = _visible;   // 풀 계약(PooledUIBase.isShow).
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    // 토글 대상은 풀 관용구대로 contents다(SettingsPanel·SimpleYNPopup과 같음). 미배선이면 자기 자신.
    GameObject ResolveTarget() => this.contents != null ? this.contents : this.gameObject;

    static void Rewire(Button _button, UnityEngine.Events.UnityAction _action)
    {
        if (_button == null) return;
        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener(_action);
    }

    static Button FindButton(TabButtonView _tab) => _tab != null ? _tab.GetComponent<Button>() : null;

    static void SetSelectedIn(List<ProfileItemCell> _cells, string _id)
    {
        for (int t_i = 0; t_i < _cells.Count; t_i++)
        {
            ProfileItemCell t_cell = _cells[t_i];
            if (t_cell != null) t_cell.SetSelected(t_cell.Id == _id);
        }
    }

    // 장착은 자리까지 같아야 같은 것이다 — 순서만 바꾼 편집도 저장 버튼이 살아나야 한다.
    static bool LoadoutsEqual(IReadOnlyList<int> _left, IReadOnlyList<int> _right)
    {
        if (_left == null || _right == null || _left.Count != _right.Count) return false;
        for (int t_i = 0; t_i < _left.Count; t_i++)
            if (_left[t_i] != _right[t_i]) return false;
        return true;
    }

    static void ResetScroll(ScrollRect _scroll)
    {
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
    }
}
