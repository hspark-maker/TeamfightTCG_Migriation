using System;
using UnityEngine;
using UnityEngine.EventSystems;

// uGUI 카드 타일의 "누르고 있는 동안" 입력 중계. 표시는 CardVisualView가 전부 맡고,
// 여기서는 누름 시작/끝만 알린다 — 표시와 입력을 한 컴포넌트에 섞지 않기 위해 나눠 둔다
// (덱편집의 DeckEditCardTile이 쓰는 것과 같은 분업).
//
// 떼기와 벗어남 둘 다에서 끝나며, 중복 호출은 여기서 막는다 —
// 콜백이 두 번 불리면 다른 카드가 연 창까지 닫는다.
public class CardPressRelay : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public Action onPressStart;
    public Action onPressEnd;

    bool interactable = true;
    bool pressing;

    /// <summary>끄면 누름을 아예 받지 않는다. 누르는 중에 껐다면 그 누름은 여기서 끝내 준다.</summary>
    public void SetInteractable(bool _on)
    {
        this.interactable = _on;
        if (!_on) EndPress();
    }

    public void OnPointerDown(PointerEventData _)
    {
        if (!this.interactable || this.pressing) return;

        this.pressing = true;
        this.onPressStart?.Invoke();
    }

    public void OnPointerUp(PointerEventData _)   => EndPress();
    public void OnPointerExit(PointerEventData _) => EndPress();

    void OnDisable() => EndPress();   // 풀 반납·목록 갱신으로 꺼질 때 창이 열린 채 남지 않게

    void EndPress()
    {
        if (!this.pressing) return;

        this.pressing = false;
        this.onPressEnd?.Invoke();
    }
}
