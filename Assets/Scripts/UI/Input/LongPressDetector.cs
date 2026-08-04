using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class LongPressDetector : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    // const였으나 사용처마다 반응 속도가 달라야 해서(키워드 툴팁 0.45초 vs 덱 편집 드래그 개시) 필드로 승격했다.
    // 기본값은 기존 const와 동일 — 이미 배치된 프리팹들은 이 값을 직렬화하지 않았으므로 기본값이 그대로 적용된다.
    [SerializeField] float threshold      = 0.45f;
    [SerializeField] float cancelDistance = 12f;   // pointer drift > this cancels long press (scroll 구분)

    public Action OnLongPress;

    /// <summary>짧게 눌렀다 뗀 탭. 드리프트가 cancelDistance를 넘었거나(= ScrollRect 스크롤 제스처)
    /// 롱프레스가 이미 발동했으면 오지 않는다.
    ///
    /// IPointerClickHandler를 쓰지 않는 이유: uGUI의 클릭은 스크롤 드래그 뒤에도 손가락이 같은 타일 위에서
    /// 떨어지면 그대로 발생한다 — 스크롤할 때마다 카드가 열린다. 여기선 이미 누름 시작 좌표를 들고 있으므로
    /// 같은 cancelDistance 하나로 롱프레스와 탭이 같은 기준을 공유한다.</summary>
    public Action OnTap;

    bool    pressing, fired;
    float   timer;
    Vector2 startPos;

    void Update()
    {
        if (!this.pressing || this.fired) return;
        if (Vector2.Distance(Input.mousePosition, this.startPos) > this.cancelDistance)
        {
            this.pressing = false;
            return;
        }
        this.timer += Time.deltaTime;

        // 구독자가 없으면 발동 표식(fired)을 세우지 않는다 — 세워버리면 롱프레스를 쓰지 않는 화면에서
        // 오래 눌렀다 뗀 손가락이 탭으로도 인정되지 않아 아무 반응이 없다.
        if (this.timer >= this.threshold && OnLongPress != null)
        {
            this.fired = true;
            OnLongPress.Invoke();
        }
    }

    public void OnPointerDown(PointerEventData _data)
    {
        this.pressing = true;
        this.timer    = 0f;
        this.fired    = false;
        this.startPos = _data.position;
    }

    public void OnPointerUp(PointerEventData _data)
    {
        // Update가 이미 취소(pressing=false)했으면 탭이 아니다. 뗀 프레임에 한 번 더 재는 것은
        // 눌렀다 곧바로 멀리서 뗀 경우(Update가 중간값을 못 본 경우)를 막기 위해서다.
        bool t_tap = this.pressing
                  && !this.fired
                  && Vector2.Distance(_data.position, this.startPos) <= this.cancelDistance;

        this.pressing = false;

        if (t_tap) OnTap?.Invoke();
    }
}
