using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 덱 목록의 한 칸(DeckCard.prefab에 부착). 칸 전체가 버튼이다.
// 덱 칸과 "신규 생성" 칸을 한 프리팹으로 겸한다 — 모드 분기는 자식 표시 토글뿐이고,
// 별도 모드 상태 필드는 두지 않는다(Bind 진입점 자체가 모드라 상태·표시 불일치가 성립하지 않는다).
public class DeckSlotView : MonoBehaviour
{
    [SerializeField] Button     clickButton;   // 루트 Button(칸 전체)
    [SerializeField] TMP_Text   nameText;      // NamePill/Name — 두 모드 공통(라벨만 바뀜)
    [SerializeField] TMP_Text   numberText;    // Tile/Number  — 덱 칸 전용
    [SerializeField] Image      previewImage;  // Tile/Preview — 덱 칸 전용
    [SerializeField] GameObject plusObject;    // Tile/Plus    — 신규 생성 칸 전용
    [SerializeField] GameObject bannerObject;  // Tile/Banner  — 덱 칸 전용 장식

    [Header("라벨")]
    [SerializeField] string createLabel = "신규 생성";
    [SerializeField] string fullLabel   = "가득 참";

    // 클릭 시 돌려줄 저장 슬롯 인덱스. 화면 표시 번호(numberText)와 절대 같은 값이 아니다.
    int m_slotIndex = -1;
    Action<int> m_onClick;

    // 생성 칸은 저장 좌표를 들지 않는다 — 큐 삽입 위치는 저장이 확정되는 순간에만 생긴다.
    Action m_onCreate;

    // 덱 칸 모드. _displayNumber는 화면 표시용 순번(1-base), _slotIndex는 DeckSaveManager 좌표.
    public void BindDeck(int _slotIndex, int _displayNumber, string _deckName, Sprite _preview, Action<int> _onClick)
    {
        m_slotIndex = _slotIndex;
        m_onClick   = _onClick;
        m_onCreate  = null;
        Wire();

        if (plusObject   != null) plusObject.SetActive(false);
        if (bannerObject != null) bannerObject.SetActive(true);

        if (numberText != null)
        {
            numberText.gameObject.SetActive(true);
            numberText.text = _displayNumber.ToString("00");   // 01, 02 …
        }

        if (nameText != null) nameText.text = _deckName;

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
        Wire();

        if (plusObject   != null) plusObject.SetActive(true);
        if (bannerObject != null) bannerObject.SetActive(false);
        if (numberText   != null) numberText.gameObject.SetActive(false);
        if (previewImage != null) previewImage.gameObject.SetActive(false);
        if (nameText     != null) nameText.text = _enabled ? createLabel : fullLabel;

        SetInteractable(_enabled);
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
        if (clickButton == null) return;

        clickButton.onClick.RemoveAllListeners();   // 재빌드 시 중복 등록 방지
        clickButton.onClick.AddListener(OnClicked);
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
}
