using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>튜토리얼 탭 게이트용 풀스크린 입력 캐처.
/// **release(손 뗌) 기반**이며, "이번 대기 중에 시작된 press"의 release만 인정한다.
/// 이전 스텝에서부터 누르고 있던 손가락(롱프레스·직전 다이얼로그 press)이 다음 다이얼로그로
/// 흘러들어가 즉시 스킵되는 버그를 막는다.</summary>
public class TutorialTapCatcher : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public Action OnDown;
    public Action OnUp;

    public void OnPointerDown(PointerEventData _) => this.OnDown?.Invoke();
    public void OnPointerUp(PointerEventData _)   => this.OnUp?.Invoke();
}
