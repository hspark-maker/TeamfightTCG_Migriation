using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 세로 연속 드래그 한 축 → 진행도(0~1) 스트림 + 뗄 때 안착/복귀 판정 1회. 그림은 전혀 건드리지 않는다.
//
// ⚠ 임계 기본값(seatThreshold·flickSpeed·flickMinProgress)의 원본은 PackCardStack이다 —
//   손맛을 바꿀 땐 둘을 같이 본다(한쪽만 만지면 개봉 넘기기와 삽입의 감각이 갈라진다).
//
// PackCardStack은 카드를 직접 끌지만 여기는 이동량을 진행도로만 환산해 넘긴다 —
// 무엇을 얼마나 움직여 보일지는 전적으로 구독자(AlbumSleeveView) 몫이다.
// 좌우 기울기는 넣지 않는다: 꽂는 동작은 축이 하나여야 읽힌다.
//
// ⚠ 배선 전제: 이 컴포넌트가 붙은 노드에 raycastTarget인 Graphic(alpha 0 Image 등)이 있어야
//   드래그 이벤트가 들어온다. 없으면 조용히 아무 반응이 없다.
public class AlbumInsertCardDragger : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>미는 동안 매 프레임 진행도(0~1).</summary>
    public Action<float> OnProgress;

    /// <summary>임계를 넘겨 손을 뗀 순간 1회.</summary>
    public Action OnSeat;

    /// <summary>임계 미만으로 손을 뗀 순간 1회.</summary>
    public Action OnReturn;

    /// <summary>첫 접촉 1회(손가락 힌트를 걷는 신호).</summary>
    public Action OnGrab;

    [Header("임계")]
    [Tooltip("이 진행도 이상이면 안착. 진행도 1 = TravelPixels만큼 밀었을 때.")]
    [Range(0.2f, 0.95f)] [SerializeField] float seatThreshold = 0.6f;
    [Tooltip("이 속도(캔버스단위/초) 이상이면 거리가 부족해도 안착한다. 0이면 속도 판정 없음.")]
    [SerializeField] float flickSpeed = 700f;
    [Tooltip("속도로 안착시킬 때 최소한 이만큼(진행도)은 밀어야 한다.")]
    [Range(0f, 0.5f)] [SerializeField] float flickMinProgress = 0.15f;

    Canvas m_canvas;
    bool   m_dragging;
    float  m_pushed;          // 드래그 시작점 기준 누적 이동량(아래가 +, 캔버스 단위).
    float  m_speed;
    bool   m_interactable = true;
    float  m_travel = 1f;

    /// <summary>진행도 1에 해당하는 캔버스 단위 거리(세션이 카드 높이로 주입한다).
    /// 0으로 두면 임계가 0이 돼 스치기만 해도 안착하므로 하한을 둔다.</summary>
    public float TravelPixels
    {
        get => this.m_travel;
        set => this.m_travel = value > 1f ? value : 1f;
    }

    /// <summary>끄면 진행 중이던 드래그까지 즉시 무효화한다 — 안착 트윈이 도는 동안
    /// 손을 떼는 순간 한 장 더 판정돼 버리는 일이 없게.</summary>
    public bool Interactable
    {
        get => this.m_interactable;
        set
        {
            this.m_interactable = value;
            if (!value) this.m_dragging = false;
        }
    }

    /// <summary>진행도를 0으로 되돌린다. 카드가 실제로 시작 자리에 놓인 순간에만 부른다(스폰·복귀 완료).</summary>
    public void ResetProgress()
    {
        this.m_pushed = 0f;
        this.m_speed  = 0f;
    }

    public void OnBeginDrag(PointerEventData _e)
    {
        if (!this.m_interactable) return;

        this.m_dragging = true;
        this.m_speed    = 0f;

        // 누적을 0으로 되돌리지 않는다 — 복귀 트윈 도중 다시 잡으면 카드가 위로 튄다.
        // 시작 자리로의 리셋은 카드가 실제로 거기 있는 시점(ResetProgress)에만 한다.
        OnGrab?.Invoke();
    }

    public void OnDrag(PointerEventData _e)
    {
        if (!this.m_dragging) return;

        // 캔버스 스케일을 나눠야 화면 이동량과 카드 이동량(같은 캔버스 좌표계)이 일치한다.
        float t_scale = this.ResolveCanvasScale();
        float t_move  = -_e.delta.y / t_scale;   // 아래로 밀면 +

        // 음수 클램프 — 위로 당겨 놓은 여유분을 지우고 다시 밀 때 즉시 반응한다.
        this.m_pushed = Mathf.Max(0f, this.m_pushed + t_move);

        // 속도는 거리와 같은 좌표계에서 재야 두 임계를 나란히 비교할 수 있다.
        float t_dt = Time.unscaledDeltaTime;
        if (t_dt > 0f) this.m_speed = t_move / t_dt;

        OnProgress?.Invoke(this.Progress());
    }

    public void OnEndDrag(PointerEventData _e)
    {
        if (!this.m_dragging) return;
        this.m_dragging = false;

        float t_progress = this.Progress();

        // 거리가 찼거나, 짧아도 충분히 빠르게 밀어 넣었으면 안착시킨다.
        bool t_flicked = this.flickSpeed > 0f
                      && this.m_speed >= this.flickSpeed
                      && t_progress >= this.flickMinProgress;

        if (t_progress < this.seatThreshold && !t_flicked)
        {
            OnReturn?.Invoke();
            return;
        }

        OnSeat?.Invoke();
    }

    void OnDisable()
    {
        // 페이지 전환 중 드래그 상태로 굳으면 다음 카드의 첫 손짓이 이전 누적을 이어받는다.
        this.m_dragging = false;
        this.m_pushed   = 0f;
        this.m_speed    = 0f;
    }

    float Progress() => Mathf.Clamp01(this.m_pushed / this.m_travel);

    float ResolveCanvasScale()
    {
        if (this.m_canvas == null) this.m_canvas = GetComponentInParent<Canvas>();

        float t_scale = this.m_canvas != null ? this.m_canvas.scaleFactor : 1f;
        return t_scale > 0f ? t_scale : 1f;
    }
}
