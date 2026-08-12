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

    /// <summary>손가락이 새로 내려앉은 순간 1회. "이 손짓은 아직 안 쓴 것"을 표시하려는 구독자용이다 —
    /// <see cref="OnDragProgress"/>의 0 통지로 대신하면 안 된다. 끌다가 시작점을 되지나가도 0이 오기 때문이다.</summary>
    public Action OnDragBegin;

    /// <summary>끄는 동안 매 프레임. 기준 폭 대비 누적 이동량(-1~1, 오른쪽이 +). 시작할 때 0으로 한 번 온다.
    /// 그림을 손가락에 붙이려는 구독자만 쓴다 — <see cref="OnSwipe"/>만 보는 쪽(상점 캐러셀)은 영향이 없다.</summary>
    public Action<float> OnDragProgress;

    /// <summary>끌던 것이 무위로 끝났다(임계 미달로 뗐거나, 도중에 잠겼다) — 그림을 제자리로 되돌리라는 신호.
    /// 이게 없으면 손가락을 따라가던 그림이 중간 자세로 굳는다.</summary>
    public Action OnDragCancel;

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

    /// <summary>현재 손짓이 시작된 화면 좌표. 구독자가 드래그 시작 위치에 연출을 맞출 때 쓴다.</summary>
    public Vector2 BeginPosition { get; private set; }

    /// <summary>드래그 중인 손가락의 최신 화면 좌표. 세로 위치까지 실시간으로 따르는 연출용이다.</summary>
    public Vector2 CurrentPosition { get; private set; }

    /// <summary>끄면 진행 중이던 드래그까지 즉시 무효화한다 — 잠금이 드래그 도중에 들어와도
    /// 손을 떼는 순간 한 칸 넘어가 버리는 일이 없게.</summary>
    public bool Interactable
    {
        get => this.m_interactable;
        set
        {
            bool t_wasDragging = this.m_dragging;

            this.m_interactable = value;
            if (value) return;

            this.m_dragging = false;

            // 끌던 도중에 잠겼다 — 구독자에게 되돌리라고 알려야 그림이 중간 자세로 굳지 않는다.
            if (t_wasDragging) OnDragCancel?.Invoke();
        }
    }

    public void OnBeginDrag(PointerEventData _e)
    {
        if (!this.m_interactable) return;

        this.m_dragging = true;
        this.m_delta    = 0f;
        this.m_speed    = 0f;
        // OnBeginDrag는 드래그 임계값을 넘긴 뒤 호출되므로 현재 position이 아니라 실제 터치다운 좌표를 보존한다.
        this.BeginPosition = _e.pressPosition;
        this.CurrentPosition = this.BeginPosition;

        OnDragBegin?.Invoke();
        OnDragProgress?.Invoke(0f);
    }

    public void OnDrag(PointerEventData _e)
    {
        if (!this.m_dragging) return;

        this.CurrentPosition = _e.position;

        // 캔버스 스케일을 나눠야 임계값이 해상도와 무관해진다(기준 폭도 같은 캔버스 좌표계다).
        float t_scale = this.ResolveCanvasScale();
        float t_move  = _e.delta.x / t_scale;
        this.m_delta += t_move;

        // 속도는 거리와 같은 좌표계에서 재야 두 임계를 나란히 비교할 수 있다.
        float t_dt = Time.unscaledDeltaTime;
        if (t_dt > 0f) this.m_speed = t_move / t_dt;

        OnDragProgress?.Invoke(this.m_delta / this.ResolveWidth());
    }

    public void OnEndDrag(PointerEventData _e)
    {
        if (!this.m_dragging) return;
        this.m_dragging = false;

        float t_width = this.ResolveWidth();
        bool  t_flick = this.flickSpeed > 0f
                     && Mathf.Abs(this.m_speed) >= this.flickSpeed
                     && Mathf.Abs(this.m_delta) >= t_width * this.flickMinRatio;

        if (Mathf.Abs(this.m_delta) < t_width * this.snapRatio && !t_flick)
        {
            OnDragCancel?.Invoke();
            return;
        }

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
