using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 프로필 편집 팝업(아바타·프레임·감정표현·닉네임). 풀(UIPoolManager)이 수명을 쥐고 로비 위에 덮인다.
//
// 즉시반영이 아니라 커밋 방식이다 — 고른 것은 드래프트(m_draft*)에만 남고 "저장"을 눌러야
// ProfileManager.Apply로 넘어간다. 닫기는 확인 없이 드래프트를 버린다(세이브는 손대지 않으므로 안전).
public class ProfileEditPanel : PooledUIBase
{
    const int TAB_AVATAR = 0;
    const int TAB_FRAME  = 1;
    const int TAB_EMOTE  = 2;

    // 표시값은 ProfileManager에서 읽고, 호출 화면의 복귀 동작만 전달받는다.
    public override void Initialization(UIData _data) => this.data = _data;
    public override void Show() => this.Open();
    public override void Hide()
    {
        // 풀 정리 등 외부 숨김에서는 호출 화면을 다시 열지 않는다.
        this.data = null;
        if (this.nicknameInput != null) this.nicknameInput.DeactivateInputField();
        this.SetVisible(false);
    }

    [Header("미리보기")]
    [Tooltip("판·얼굴·링 한 덩어리. 팝업 안에서만 드래프트를 즉시 반영한다.")]
    [SerializeField] ProfileAvatarView previewView;
    [SerializeField] TMP_InputField nicknameInput;
    [Tooltip("연필 버튼. 누르기 전까지 닉네임 입력은 라벨처럼 잠겨 있다.")]
    [SerializeField] Button pencilButton;

    [Header("탭")]
    [SerializeField] TabButtonView avatarTab;
    [SerializeField] TabButtonView frameTab;
    [SerializeField] TabButtonView emoteTab;
    [SerializeField] GameObject avatarPanel;
    [SerializeField] GameObject framePanel;
    [SerializeField] GameObject emotePanel;

    [Header("아바타·프레임 그리드")]
    [SerializeField] ScrollRect avatarScroll;
    [SerializeField] Transform avatarContent;
    [SerializeField] ScrollRect frameScroll;
    [SerializeField] Transform frameContent;
    [Tooltip("아바타·프레임이 함께 쓰는 칸 프리팹.")]
    [SerializeField] ProfileItemCell cellPrefab;

    [Header("감정표현")]
    [SerializeField] ScrollRect emoteScroll;
    [Tooltip("장착 줄(SLOT_COUNT칸)이 들어갈 자리. 누르면 그 슬롯이 '고르는 자리'가 된다.")]
    [SerializeField] Transform equippedEmoteContent;
    [Tooltip("풀 전량이 깔리는 자리. 누르면 고른 슬롯에 끼워진다.")]
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

    // 임시 선택. 저장 전까지 ProfileManager에는 아무것도 넘어가지 않는다.
    string m_draftAvatarId;
    string m_draftFrameId;
    string m_draftNickname;
    // 지금 고르고 있는 장착 칸. 풀에서 누른 감정표현이 이 자리에 들어간다.
    int m_selectedEmoteSlot;

    /// <summary>드래프트를 현재 프로필로 리셋하고 팝업을 띄운다.</summary>
    public void Open()
    {
        this.m_draftAvatarId = ProfileManager.AvatarId;
        this.m_draftFrameId = ProfileManager.FrameId;
        this.m_draftNickname = ProfileManager.Nickname;
        this.m_draftEmoteIds.Clear();
        for (int t_i = 0; t_i < ProfileManager.EmoteIds.Count; t_i++)
            this.m_draftEmoteIds.Add(ProfileManager.EmoteIds[t_i]);
        this.m_selectedEmoteSlot = 0;

        if (!this.m_built) this.Build();

        this.SetTab(TAB_AVATAR);           // 열 때마다 아바타 탭부터 — 이전 세션의 탭이 남지 않게.
        this.RefreshSelection();
        this.RefreshPreview();
        this.RefreshNicknameField();
        if (this.saveButton != null) this.saveButton.interactable = false;
        this.SetVisible(true);
    }

    /// <summary>드래프트를 버리고 닫는다. 확인 팝업은 두지 않는다.</summary>
    public void Close()
    {
        if (!this.isShow) return;
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
            this.nicknameInput.characterLimit = ProfileManager.NICKNAME_MAX_LENGTH;
            this.nicknameInput.onValueChanged.RemoveAllListeners();
            this.nicknameInput.onValueChanged.AddListener(this.OnNicknameChanged);
        }
    }

    void OnDisable()
    {
        this.data = null;
        // 소프트키보드가 팝업 밖까지 살아남지 않게 — 팝업이 풀에서 꺼지는 경로는 Close를 거치지 않는다.
        if (this.nicknameInput != null)
        {
            this.nicknameInput.onValueChanged.RemoveAllListeners();
            this.nicknameInput.DeactivateInputField();
        }

        // 안전망 — Close를 거치지 않고 꺼지면 공용 딤이 남는다.
        ScreenDim.Hide(this);
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    // 세 그리드를 한 번에 세운다. 설정이 아직 없으면(초기화 배선 전) 그 축만 조용히 비운다 — 씬이 죽지 않게.
    void Build()
    {
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

    // 장착 칸을 눌렀다 — 고르는 자리만 옮긴다. 드래프트는 그대로라 저장 버튼도 움직이지 않는다.
    void OnEquippedEmoteClicked(int _slot)
    {
        this.m_selectedEmoteSlot = Mathf.Clamp(_slot, 0, EmoteCatalog.SLOT_COUNT - 1);
        this.RefreshEmotes();
    }

    // 풀에서 골랐다 — 지금 고르는 자리에 끼운다.
    void OnEmoteClicked(int _id)
    {
        EmoteCatalog t_catalog = ProfileManager.EmoteCatalog;
        if (t_catalog == null || !t_catalog.TryGet(_id, out _)) return;
        if (this.m_selectedEmoteSlot < 0 || this.m_selectedEmoteSlot >= this.m_draftEmoteIds.Count) return;

        this.m_draftEmoteIds[this.m_selectedEmoteSlot] = _id;
        this.RefreshEmotes();
        this.RefreshSaveButton();
    }

    void OnNicknameChanged(string _value)
    {
        this.m_draftNickname = _value;
        this.RefreshSaveButton();
    }

    // 연필을 누르기 전까지 입력칸은 라벨처럼 잠겨 있다 — 팝업을 열자마자 키보드가 올라오지 않게.
    void BeginNicknameEdit()
    {
        if (this.nicknameInput == null) return;
        this.nicknameInput.interactable = true;
        this.nicknameInput.ActivateInputField();
    }

    // 드래프트를 실제 프로필로 커밋한다. 영속·통지는 ProfileManager가 처리한다.
    void Save()
    {
        // 이름이 막히면 네 축을 통째로 보류하고 팝업을 열어 둔 채 안내한다 — 고른 아바타·감정표현을 잃지 않고
        // 이름만 고쳐 다시 저장할 수 있게.
        if (ProfileManager.IsNicknameBlocked(this.m_draftNickname))
        {
            NicknameRejectNotice.Show();
            return;
        }

        ProfileManager.Apply(this.m_draftNickname, this.m_draftAvatarId, this.m_draftFrameId, this.m_draftEmoteIds);
        this.Close();
    }

    // 공용 탭 컨트롤러가 없어 여기서 직접 토글한다(DeckTabController와 같은 관용구).
    void SetTab(int _tab)
    {
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

    // 장착 줄과 풀의 표시를 드래프트 기준으로 다시 그린다. 풀의 선택 배지는 "고른 슬롯에 끼워진 것"을
    // 가리키므로 슬롯을 옮기면 배지도 따라 옮겨간다.
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
            t_cell.SetSelected(t_i == this.m_selectedEmoteSlot);
        }

        int t_selectedId = this.m_selectedEmoteSlot >= 0 && this.m_selectedEmoteSlot < this.m_draftEmoteIds.Count
            ? this.m_draftEmoteIds[this.m_selectedEmoteSlot]
            : 0;
        for (int t_i = 0; t_i < this.m_emoteCells.Count; t_i++)
        {
            EmoteItemCell t_cell = this.m_emoteCells[t_i];
            if (t_cell != null) t_cell.SetSelected(t_cell.Key == t_selectedId);
        }
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
