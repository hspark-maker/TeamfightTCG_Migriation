using UnityEngine;
using UnityEngine.UI;

// 아웃게임(uGUI) 카드 타일의 키워드 아이콘 1개. CardVisualView가 생성·세팅한다.
//
// 인게임 kewordIcon.prefab이 "루트 SpriteRenderer = 배경 + 자식 SpriteRenderer = 아이콘" 구조라
// uGUI 미러도 같은 구조를 쓴다(루트 Image = 배경, 자식 Image = 키워드 아이콘).
// 그래서 스프라이트는 루트가 아니라 자식 icon에만 주입한다 — 루트에 넣으면 배경이 사라진다.
public class CardKeywordIconView : MonoBehaviour
{
    [SerializeField] Image icon;   // 키워드 스프라이트가 주입될 자식 Image(배경은 루트 Image가 담당)

    /// <summary>키워드 아이콘 스프라이트를 세팅. null이면 아이콘만 끄고 배경은 유지한다.</summary>
    public void SetIcon(Sprite _sprite)
    {
        if (this.icon == null) return;

        this.icon.sprite  = _sprite;
        this.icon.enabled = _sprite != null;
    }
}
