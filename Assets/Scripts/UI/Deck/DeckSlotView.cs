using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 덱 목록의 한 칸(DeckCard.prefab에 부착). 칸 전체가 버튼이다.
// 덱 칸, "신규 생성" 칸, 아직 저장되지 않은 "생성 중" 칸을 한 프리팹으로 겸한다 — 모드 분기는 자식 표시 토글뿐이고,
// 별도 모드 상태 필드는 두지 않는다(Bind 진입점 자체가 모드라 상태·표시 불일치가 성립하지 않는다).
public class DeckSlotView : MonoBehaviour
{
    [SerializeField] Button     clickButton;   // 루트 Button(칸 전체)

    [Tooltip("nameText를 담는 알약 껍데기. 활성은 코드가 소유한다 — 저작에서 꺼 두고 Bind 진입점이 켠다.\n"
           + "미배선이어도 목록은 그대로 돈다(알약 없이 글자만 뜨는 프리팹이 있다).")]
    [SerializeField] GameObject namePill;      // Tile/NamePill — nameText의 배경 알약
    [SerializeField] TMP_Text   nameText;      // NamePill/Name — 라벨 자리(덱 칸은 showDeckName에 달렸다)
    [SerializeField] TMP_Text   numberText;    // Tile/Number  — 덱 칸 전용
    [SerializeField] Image      previewImage;  // Tile/Preview — 덱 칸 전용
    [SerializeField] GameObject plusObject;    // Tile/Plus    — 신규 생성 칸 전용
    [SerializeField] GameObject bannerObject;  // Tile/Banner  — 덱 칸 전용 장식
    [SerializeField] Button     deleteButton;  // Tile/DeleteButton — 덱 칸 전용, 편집 모드에서만 노출
    [SerializeField] GameObject selectedFrame; // Tile/SelectedFrame — 선택 표시(매치 가로 리스트 전용, 로비 목록은 미배선)

    [Tooltip("선택했을 때 켜는 밑판. 테두리(selectedFrame)와 별개 축이라 한쪽만 저작해도 된다.")]
    [SerializeField] GameObject selectedBackground;

    [Tooltip("생성 중 칸이 선택 표시로 쓰는 밑판. 저장된 덱을 고른 것과 색이 갈려야 해서 따로 저작한다.\n"
           + "미배선이면 생성 중 칸도 selectedBackground를 그대로 쓴다.")]
    [SerializeField] GameObject newDeckBackground;   // Tile/NewDeckBg

    [Header("라벨")]
    [Tooltip("덱 칸에 덱 이름을 보일 것인가.\n"
           + "끄면 번호만 나온다(로비 2열 목록의 기본). 켜는 쪽은 이름을 받을 자리가 저작된 프리팹뿐이다.")]
    [SerializeField] bool showDeckName;

    [SerializeField] string createLabel = "신규 생성";
    [SerializeField] string fullLabel   = "가득 참";

    [Tooltip("아직 저장되지 않은 신규 덱 칸에 뜨는 문구. 저장이 확정되면 그 칸은 사라지고 실제 덱 칸이 대신 선다.")]
    [SerializeField] string draftLabel  = "생성 중";

    [Tooltip("만석이라 생성 칸이 삭제 토글로 동작할 때의 문구. 토글이 켜진 동안은 deleteDoneLabel 로 바뀐다.")]
    [SerializeField] string deleteLabel     = "덱 삭제";
    [SerializeField] string deleteDoneLabel = "완료";

    [Header("선택 시 글자색 (선택)")]
    [Tooltip("켜면 선택 여부에 따라 번호 색을 갈아끼운다. 끄면 프리팹 저작 색을 그대로 둔다.\n"
           + "알약 안 이름은 대상이 아니다 — 그 자리는 밑판 색과 짝으로 저작해야 읽힌다.")]
    [SerializeField] bool  tintTextOnSelect;
    [SerializeField] Color normalTextColor   = Color.white;
    [SerializeField] Color selectedTextColor = Color.white;

    // 클릭 시 돌려줄 저장 슬롯 인덱스. 화면 표시 번호(numberText)와 절대 같은 값이 아니다.
    int m_slotIndex = -1;
    Action<int> m_onClick;

    // 생성 칸은 저장 좌표를 들지 않는다 — 큐 삽입 위치는 저장이 확정되는 순간에만 생긴다.
    Action m_onCreate;

    // 선택 밑판으로 어느 쪽을 켤지. Bind 진입점이 정하고 SetSelected가 읽는다 —
    // 활성 토글은 목록이 나중에 걸기 때문에, 어느 밑판인지는 Bind 시점에 굳혀 두어야 한다.
    bool m_useNewDeckBackground;

    // 삭제 콜백. 덱 칸 모드에서만 채워지고, 이 값이 곧 "삭제 가능한 칸인가"의 판정이다.
    Action<int> m_onDelete;

    // 덱 칸 모드. _displayNumber는 화면 표시용 순번(1-base), _slotIndex는 DeckSaveManager 좌표.
    public void BindDeck(int _slotIndex, int _displayNumber, string _deckName, Sprite _preview, Action<int> _onClick, Action<int> _onDelete)
    {
        m_slotIndex = _slotIndex;
        m_onClick   = _onClick;
        m_onCreate  = null;
        m_onDelete  = _onDelete;
        Wire();

        // 편집 모드 진입 전까지는 숨김이 기본 — 켜는 책임은 SetEditMode 한 곳에만 둔다.
        SetEditMode(false);
        m_useNewDeckBackground = false;
        SetSelected(false);   // 재빌드로 칸이 재사용될 때 이전 선택 표시가 남지 않게

        if (plusObject   != null) plusObject.SetActive(false);
        if (bannerObject != null) bannerObject.SetActive(true);

        if (numberText != null)
        {
            numberText.gameObject.SetActive(true);
            numberText.text = _displayNumber.ToString();
        }

        if (namePill != null) namePill.SetActive(showDeckName);

        if (nameText != null)
        {
            nameText.enabled = showDeckName;
            if (showDeckName) nameText.text = _deckName ?? string.Empty;
        }

        if (previewImage != null)
        {
            previewImage.gameObject.SetActive(true);
            // null이면 대입하지 않는다 — 프리팹 기본 스프라이트를 남기는 게 폴백 사양이다.
            // (null을 대입하면 흰 사각형이 된다)
            if (_preview != null) previewImage.sprite = _preview;
        }

        SetInteractable(true);
    }

    // 신규 생성 칸 모드. _enabled가 false면 6슬롯 만석 → 자리는 지키되 비활성.
    public void BindCreate(bool _enabled, Action _onClick)
    {
        m_slotIndex = -1;
        m_onClick   = null;
        // 만석이면 콜백 자체를 붙이지 않는다 — interactable=false와 별개의 이중 가드.
        m_onCreate  = _enabled ? _onClick : null;
        m_onDelete  = null;   // 생성 칸은 지울 대상이 없다 → SetEditMode도 무시하게 된다
        Wire();

        SetEditMode(false);
        m_useNewDeckBackground = false;
        SetSelected(false);

        if (plusObject != null)
        {
            plusObject.SetActive(true);
            // 삭제 토글 모드(BindDeleteToggle)가 ✕로 돌려놓았을 수 있다 — 같은 저작 칸이 두 모드를 오간다.
            plusObject.transform.localRotation = Quaternion.identity;
        }
        if (bannerObject != null) bannerObject.SetActive(false);
        if (numberText   != null) numberText.gameObject.SetActive(false);
        if (previewImage != null) previewImage.gameObject.SetActive(false);
        if (nameText != null)
        {
            // 같은 프리팹이 덱 칸 모드에서 이름을 꺼둘 수 있다 — 켜는 책임을 이 진입점이 직접 진다.
            nameText.enabled = true;
            nameText.text    = _enabled ? createLabel : fullLabel;
        }

        SetInteractable(_enabled);
    }

    /// <summary>만석일 때 생성 칸이 맡는 삭제 토글 모드. 누르면 _onClick 이 목록의 삭제 버튼을 여닫는다.
    /// 지울 대상이 없는 칸이므로 삭제 콜백은 붙지 않고, 클릭 경로는 생성 칸과 같은 자리를 쓴다.</summary>
    public void BindDeleteToggle(bool _on, Action _onClick)
    {
        m_slotIndex = -1;
        m_onClick   = null;
        m_onCreate  = _onClick;   // 이 칸의 유일한 동작 — OnClicked 가 이 슬롯을 먼저 본다
        m_onDelete  = null;
        Wire();

        SetEditMode(false);
        m_useNewDeckBackground = false;
        SetSelected(false);

        if (plusObject != null)
        {
            plusObject.SetActive(true);
            // 별도 아트 없이 ⊕ 를 45° 돌려 ✕ 로 읽히게 한다 — 생성이 아니라 삭제 축임을 이 칸 하나로 알린다.
            plusObject.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }
        if (bannerObject != null) bannerObject.SetActive(false);
        if (numberText   != null) numberText.gameObject.SetActive(false);
        if (previewImage != null) previewImage.gameObject.SetActive(false);
        if (nameText != null)
        {
            nameText.enabled = true;
            nameText.text    = _on ? deleteDoneLabel : deleteLabel;
        }

        SetInteractable(true);
    }

    // 아직 저장되지 않은 신규 덱 칸. 저장 좌표가 없으니 클릭·삭제 콜백을 붙이지 않는다 —
    // 이 칸이 목록에 있다는 사실 자체가 "지금 신규 덱을 만드는 중"이라는 뜻이다.
    // 번호는 붙이지 않는다. 순번은 저장이 확정되는 순간에 정해지므로, 그전에 보여 주면 틀린 값을 약속하는 셈이다.
    public void BindDraft()
    {
        m_slotIndex = -1;
        m_onClick   = null;
        m_onCreate  = null;
        m_onDelete  = null;
        Wire();

        SetEditMode(false);
        m_useNewDeckBackground = true;
        SetSelected(false);   // 선택 표시는 목록이 Build 끝에서 켠다

        if (plusObject   != null) plusObject.SetActive(false);
        if (bannerObject != null) bannerObject.SetActive(true);
        if (previewImage != null) previewImage.gameObject.SetActive(false);

        if (numberText != null) numberText.gameObject.SetActive(false);

        if (namePill != null) namePill.SetActive(true);

        if (nameText != null)
        {
            // 덱 칸 모드가 이름을 꺼둘 수 있다(showDeckName) — 켜는 책임을 이 진입점이 직접 진다.
            nameText.enabled = true;
            nameText.text    = draftLabel;
        }

        // interactable을 내리지 않는다 — ColorTint가 칸 전체를 어둡게 칠해 잠긴 칸으로 읽힌다.
        // 콜백이 비어 있어 눌러도 아무 일도 일어나지 않는다.
        SetInteractable(true);
    }

    // 편집 모드 토글. 목록을 재빌드하지 않고 이 칸의 삭제 버튼만 켜고 끈다.
    // 삭제 콜백이 없는 칸(신규 생성 칸)은 _on이 true여도 켜지 않는다 — 모드 판정을 콜백 유무로 통일한다.
    public void SetEditMode(bool _on)
    {
        if (deleteButton == null) return;   // 프리팹 미배선이어도 목록 동작은 그대로

        deleteButton.gameObject.SetActive(_on && m_onDelete != null);
    }

    // 선택 표시 토글. 목록을 재빌드하지 않고 이 칸의 테두리·밑판·글자색만 갈아끼운다
    // (리스트가 이전 선택을 끄고 새 선택을 켜는 방식 — 재빌드는 스크롤 위치를 잃는다).
    public void SetSelected(bool _on)
    {
        if (selectedFrame != null) selectedFrame.SetActive(_on);

        // 밑판은 둘 중 하나만 뜬다. 생성 중 밑판이 미배선이면 저장된 덱과 같은 밑판으로 되돌아간다.
        bool t_useNew = m_useNewDeckBackground && newDeckBackground != null;

        if (selectedBackground != null) selectedBackground.SetActive(_on && !t_useNew);
        if (newDeckBackground  != null) newDeckBackground.SetActive(_on && t_useNew);

        if (!tintTextOnSelect) return;

        var t_color = _on ? selectedTextColor : normalTextColor;
        if (numberText != null) numberText.color = t_color;
    }

    public void SetInteractable(bool _on)
    {
        if (clickButton != null) clickButton.interactable = _on;
    }

    // 목록 칸은 런타임 생성이라 TutorialAnchor 컴포넌트를 프리팹에 붙일 수 없다 → 호출측이 대신 등록한다(LobbyTabController와 같은 처리).
    public void RegisterTutorialAnchor(EOutgameTutorialAnchor _key)
    {
        TutorialAnchorRegistry.Register(_key, transform as RectTransform, clickButton);
    }

    void Wire()
    {
        if (clickButton != null)
        {
            clickButton.onClick.RemoveAllListeners();   // 재빌드 시 중복 등록 방지
            clickButton.onClick.AddListener(OnClicked);
        }

        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(OnDeleteClicked);
        }
    }

    void OnClicked()
    {
        // 콜백 유무가 곧 모드다(Bind 진입점에서 한쪽만 채운다).
        if (m_onCreate != null)
        {
            m_onCreate();
            return;
        }

        if (m_slotIndex < 0) return;
        m_onClick?.Invoke(m_slotIndex);
    }

    // 삭제 버튼은 덱 칸에서만 노출되지만, 슬롯 좌표 가드는 클릭 경로와 동일하게 둔다.
    void OnDeleteClicked()
    {
        if (m_slotIndex < 0) return;
        m_onDelete?.Invoke(m_slotIndex);
    }
}
