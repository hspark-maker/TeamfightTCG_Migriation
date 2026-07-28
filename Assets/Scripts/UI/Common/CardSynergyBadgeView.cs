using UnityEngine;
using UnityEngine.UI;

// 아웃게임(uGUI) 카드 타일의 시너지 배지 1개. CardVisualView가 생성·세팅한다.
//
// 아이콘 선택 규약은 인게임 SynergyBadgeView와 동일 — 활성/비활성을 알파·색 dim이 아니라
// activeIcon / inactiveIcon 스프라이트 교체로 인코딩한다(전투/로비 비주얼 일치).
// 단 인게임의 pop 연출(PlayPop)은 옮기지 않는다. 아웃게임엔 "효과 발동 순간"이라는 게이트 자체가 없어
// 재생할 시점이 없고, 그것 때문에 DOTween 의존을 끌어올 이유도 없다.
public class CardSynergyBadgeView : MonoBehaviour
{
    [SerializeField] Image icon;   // 시너지 아이콘(SynergyData.activeIcon / inactiveIcon)

    // 인게임 SynergyBadgeView와 달리 배지→시너지 역참조(Synergy 프로퍼티)는 두지 않는다.
    // 아웃게임엔 툴팁/롱프레스 경로가 없어 소비자가 0건이고, 쓰이지 않는 상태는 갱신 누락의 씨앗이 된다.

    /// <summary>배지를 특정 시너지로 세팅. _synergy가 null이면 비활성화(빈 태그 슬롯).</summary>
    public void Set(SynergyData _synergy, bool _active)
    {
        if (_synergy == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        if (this.icon != null)
        {
            Sprite t_sprite = _active ? _synergy.activeIcon : _synergy.inactiveIcon;
            this.icon.sprite  = t_sprite;
            this.icon.enabled = t_sprite != null;
        }
    }
}
