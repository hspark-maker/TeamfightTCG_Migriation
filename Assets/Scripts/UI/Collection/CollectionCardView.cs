using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 도감 카드 타일 한 장의 표시 바인딩(Card.prefab에 부착).
// 소유 = 정상(이름 표시), 미소유 = 잠김 오버레이(어둡게+실루엣, 이름 숨김).
// null 카드(부분행 빈칸)는 타일 자체를 숨긴다.
// 범위: 카드목록 연동만 — 생산/수확/클릭 상세는 다음 패스.
public class CollectionCardView : MonoBehaviour
{
    [SerializeField] Image portrait;          // 카드 아트
    [SerializeField] TMP_Text nameText;       // 카드 이름(미소유 시 숨김)
    [SerializeField] GameObject lockOverlay;  // 미소유 시 활성(어두운 오버레이 + 잠김 표시)

    // 카드 데이터·소유여부로 타일을 바인딩. _card가 null이면 빈칸으로 숨긴다.
    public void Bind(CardData _card, bool _owned)
    {
        if (_card == null)
        {
            gameObject.SetActive(false);
            return;
        }
        gameObject.SetActive(true);

        if (portrait != null)
        {
            portrait.sprite = _card.fullImage;
            portrait.enabled = portrait.sprite != null;
        }

        if (nameText != null)
        {
            // 미소유는 이름을 숨겨 실루엣만 노출.
            nameText.gameObject.SetActive(_owned);
            if (_owned) nameText.text = _card.displayName;
        }

        // 미소유 = 잠김 오버레이 on(아트를 어둡게 덮어 실루엣화).
        if (lockOverlay != null) lockOverlay.SetActive(!_owned);
    }
}
