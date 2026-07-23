using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 카드 정보 창의 시너지 아이콘. **누르면 바로** 설명이 뜨고 떼면 사라진다.
/// 키워드 아이콘(LongPressDetector 0.45초 + KeywordIconButton)과 달리 롱프레스가 아니다 —
/// 시너지는 아이콘만 봐선 뭔지 모르니 진입 장벽을 낮춘다.
/// </summary>
public class SynergyIconButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public Action onPointerDown;
    public Action onPointerUp;

    public void OnPointerDown(PointerEventData _) => this.onPointerDown?.Invoke();
    public void OnPointerUp(PointerEventData _)   => this.onPointerUp?.Invoke();
    // 누른 채 아이콘 밖으로 나가도 팝업이 남지 않게
    public void OnPointerExit(PointerEventData _) => this.onPointerUp?.Invoke();
}
