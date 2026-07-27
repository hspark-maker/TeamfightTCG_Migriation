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

    // 덱 칸 모드. _displayNumber는 화면 표시용 순번(1-base), _slotIndex는 DeckSaveManager 좌표.
    public void BindDeck(int _slotIndex, int _displayNumber, string _deckName, Sprite _preview, Action<int> _onClick)
    {
        m_slotIndex = _slotIndex;
        Wire(_onClick);

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

    // 신규 생성 칸 모드. _emptySlotIndex가 -1이면 6슬롯 만석 → 자리는 지키되 비활성.
    public void BindCreate(int _emptySlotIndex, Action<int> _onClick)
    {
        m_slotIndex = _emptySlotIndex;
        Wire(_onClick);

        if (plusObject   != null) plusObject.SetActive(true);
        if (bannerObject != null) bannerObject.SetActive(false);
        if (numberText   != null) numberText.gameObject.SetActive(false);
        if (previewImage != null) previewImage.gameObject.SetActive(false);
        if (nameText     != null) nameText.text = _emptySlotIndex >= 0 ? createLabel : fullLabel;

        SetInteractable(_emptySlotIndex >= 0);
    }

    public void SetInteractable(bool _on)
    {
        if (clickButton != null) clickButton.interactable = _on;
    }

    void Wire(Action<int> _onClick)
    {
        m_onClick = _onClick;
        if (clickButton == null) return;

        clickButton.onClick.RemoveAllListeners();   // 재빌드 시 중복 등록 방지
        clickButton.onClick.AddListener(OnClicked);
    }

    void OnClicked()
    {
        if (m_slotIndex < 0) return;   // 만석 신규 칸 이중 가드(interactable=false와 별개)
        m_onClick?.Invoke(m_slotIndex);
    }
}
