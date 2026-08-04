using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 가로 스와이프 한 번 → 방향(-1 이전 / +1 다음) 콜백 하나. 그림은 전혀 건드리지 않는다.
//
// ⚠ 임계 기본값(snapRatio·flickSpeed·flickMinRatio)의 원본은 PackCarouselView다 —
//   손맛을 바꿀 땐 둘을 같이 본다(한쪽만 만지면 상점 캐러셀과 상세 넘기기의 감각이 갈라진다).
//
// PackCarouselView와 임계 판정을 똑같이 맞춘 이유는 손맛의 진실원을 하나로 두기 위해서다.
// 다만 저쪽은 페이지를 한 줄로 늘어놓은 Track을 손가락이 직접 끌지만, 여기는 화면에 한 장뿐이라
// 끌 트랙이 없다 — 그래서 이동량은 판정에만 쓰고, 뗄 때 한 번만 콜백한다.
// 무엇을 어떻게 바꿔 보일지(즉시 교체든 슬라이드 트윈이든)는 전적으로 구독자 몫이다.
//
// ⚠ 배선 전제: 이 컴포넌트가 붙은 노드에 raycastTarget인 Graphic(투명 Image 등)이 있어야
//   드래그 이벤트가 들어온다. 없으면 조용히 아무 반응이 없다.
public class HorizontalSwipeDetector : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>스와이프가 확정된 순간 1회. -1 = 이전, +1 = 다음(오른쪽으로 끌면 이전이 들어온다).</summary>
    public Action<int> OnSwipe;

    [Header("스와이프")]
    [Tooltip("넘기는 데 필요한 가로 이동량(기준 폭 대비). 해상도와 무관하게 같은 손맛.")]
    [Range(0.08f, 0.5f)] [SerializeField] float snapRatio = 0.22f;
    [Tooltip("이 속도(캔버스단위/초) 이상이면 거리가 부족해도 넘어간다. 0이면 속도 판정 없음.")]
    [SerializeField] float flickSpeed = 700f;
    [Tooltip("속도로 넘길 때 최소한 이만큼(기준 폭 대비)은 밀어야 한다.")]
    [Range(0f, 0.3f)] [SerializeField] float flickMinRatio = 0.03f;

    Canvas m_canvas;
    bool   m_dragging;
    float  m_delta;        // 드래그 시작점 기준 누적 이동량(오른쪽이 +).
    float  m_speed;
    bool   m_interactable = true;

    /// <summary>끄면 진행 중이던 드래그까지 즉시 무효화한다 — 잠금이 드래그 도중에 들어와도
    /// 손을 떼는 순간 한 칸 넘어가 버리는 일이 없게.</summary>
    public bool Interactable
    {
        get => this.m_interactable;
        set
        {
            this.m_interactable = value;
            if (!value) this.m_dragging = false;
        }
    }

    public void OnBeginDrag(PointerEventData _e)
    {
        if (!this.m_interactable) return;

        this.m_dragging = true;
        this.m_delta    = 0f;
        this.m_speed    = 0f;
    }

    public void OnDrag(PointerEventData _e)
    {
        if (!this.m_dragging) return;

        // 캔버스 스케일을 나눠야 임계값이 해상도와 무관해진다(기준 폭도 같은 캔버스 좌표계다).
        float t_scale = this.ResolveCanvasScale();
        float t_move  = _e.delta.x / t_scale;
        this.m_delta += t_move;

        // 속도는 거리와 같은 좌표계에서 재야 두 임계를 나란히 비교할 수 있다.
        float t_dt = Time.unscaledDeltaTime;
        if (t_dt > 0f) this.m_speed = t_move / t_dt;
    }

    public void OnEndDrag(PointerEventData _e)
    {
        if (!this.m_dragging) return;
        this.m_dragging = false;

        float t_width = this.ResolveWidth();
        bool  t_flick = this.flickSpeed > 0f
                     && Mathf.Abs(this.m_speed) >= this.flickSpeed
                     && Mathf.Abs(this.m_delta) >= t_width * this.flickMinRatio;

        if (Mathf.Abs(this.m_delta) < t_width * this.snapRatio && !t_flick) return;

        OnSwipe?.Invoke(this.m_delta > 0f ? -1 : 1);   // 오른쪽으로 끌면 이전이 들어온다.
    }

    void OnDisable()
    {
        // 탭 전환 중 드래그 상태로 굳으면 재진입 첫 손짓이 이전 누적을 이어받는다.
        this.m_dragging = false;
    }

    float ResolveCanvasScale()
    {
        if (this.m_canvas == null) this.m_canvas = GetComponentInParent<Canvas>();

        float t_scale = this.m_canvas != null ? this.m_canvas.scaleFactor : 1f;
        return t_scale > 0f ? t_scale : 1f;
    }

    // 판정 기준 폭 = 자기 영역. rect가 아직 드라이브되기 전(레이아웃 첫 프레임)이면 화면 폭으로 폴백한다 —
    // 0으로 두면 임계가 0이 돼 손가락이 스치기만 해도 넘어간다.
    float ResolveWidth()
    {
        var   t_rect  = transform as RectTransform;
        float t_width = t_rect != null ? t_rect.rect.width : 0f;
        if (t_width > 1f) return t_width;

        float t_scale = this.ResolveCanvasScale();
        return Screen.width / t_scale;
    }
}
