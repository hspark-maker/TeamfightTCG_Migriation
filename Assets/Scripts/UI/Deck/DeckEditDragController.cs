using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 덱 편집 화면의 카드 드래그 담당(DragLayer에 부착: LobbyCanvas 직하 / full-stretch / pivot 0.5,0.5 / CanvasGroup blocksRaycasts=false).
//
// 타일이 IDragHandler를 구현할 수 없으므로(부모 ScrollRect가 죽는다) 드래그는 uGUI 이벤트가 아니라
// 롱프레스 개시 + Update 폴링으로 굴린다. 그래서 Begin에서 "이미 시작된 ScrollRect 드래그"의 소유권을 명시적으로 뺏어야 한다.
public class DeckEditDragController : MonoBehaviour
{
    [SerializeField] RectTransform    dragLayer;    // 보통 자기 자신
    [SerializeField] Canvas           rootCanvas;   // LobbyCanvas
    [SerializeField] DeckEditCardTile ghostPrefab;  // 타일과 동일 프리팹 재사용
    [Tooltip("컬렉션 cellSize를 못 읽었을 때만 쓰는 폴백. 평소에는 Begin이 넘겨준 실제 cellSize가 이 값을 덮어쓴다.")]
    [SerializeField] Vector2          ghostSize  = new Vector2(290f, 386f);
    [SerializeField] float            ghostScale = 1.05f;
    [SerializeField] float            ghostAlpha = 0.85f;

    Func<IReadOnlyList<DeckEditSlotView>> m_slotProvider;
    Action<int, int>                      m_onDropped;
    Action                                m_onEnded;

    RectTransform  m_ghostRect;
    CanvasGroup    m_ghostGroup;

    // 교체로 밀려난 카드를 돌려보내는 비행. 드래그와 고스트 한 벌을 나눠 쓰므로 둘은 배타다.
    Tween m_flyTween;

    const float FLY_TIME      = 0.26f;
    const float FLY_END_SCALE = 0.5f;
    CardVisualView m_ghostView;

    bool             m_dragging;
    int              m_card;
    PointerEventData m_data;
    int              m_finger = -1;   // 추적 중인 레거시 Input 터치의 fingerId. -1 = 마우스(터치 없음)

    // 드래그 동안 세워둔 목록과 그 원래 축 설정. 소유권을 뺏는 것(pointerDrag=null)만으로는 부족하다 —
    // 그건 "이미 잡힌 드래그"를 끊을 뿐이라, 고스트를 든 채 손가락이 움직이는 동안 목록이 새로 잡혀 흐를 수 있다.
    ScrollRect m_lockedScroll;
    bool       m_scrollWasVertical;
    bool       m_scrollWasHorizontal;

    public bool IsDragging => m_dragging;

    // LobbyCanvas는 ScreenSpaceOverlay라 실질 null이 반환된다(Overlay에서는 카메라를 넘기면 안 된다).
    Camera EventCam => rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                       ? rootCanvas.worldCamera
                       : null;

    /// <summary>_onEnded는 드롭 성사 여부와 무관하게 드래그가 끝날 때마다 발화한다(취소·화면 이탈 포함).
    /// 드래그 동안 켜 둔 화면 신호를 끄는 자리다.</summary>
    public void Setup(Func<IReadOnlyList<DeckEditSlotView>> _slotProvider, Action<int, int> _onDropped, Action _onEnded = null)
    {
        m_slotProvider = _slotProvider;
        m_onDropped    = _onDropped;
        m_onEnded      = _onEnded;
    }

    // _cellSize는 카드가 뽑혀 나온 그리드의 실제 칸 크기. zero면 인스펙터 폴백(ghostSize)을 쓴다.
    public void Begin(int _card, PointerEventData _data, ScrollRect _ownerScroll, Vector2 _cellSize)
    {
        if (m_dragging || _card <= 0 || _data == null) return;

        // 1) ScrollRect가 이미 드래그 중이면 정상 종료 이벤트를 먹여서 끝낸다.
        //    pointerDrag만 null로 만들면 ScrollRect가 OnEndDrag를 못 받아
        //    m_Dragging=true인 채 남아 다음 프레임 관성이 튄다.
        if (_data.pointerDrag != null && _data.dragging)
            ExecuteEvents.Execute(_data.pointerDrag, _data, ExecuteEvents.endDragHandler);

        // 2) 입력 모듈이 더는 이 포인터의 드래그를 라우팅하지 않게 한다.
        //    StandaloneInputModule.ProcessDrag는 pointerDrag == null이면 첫 줄에서 return한다.
        _data.dragging    = false;
        _data.pointerDrag = null;

        // 3) 고스트 확보(4)에 실패하면 드래그가 성립하지 않으므로, 목록을 세우는 것은 그 뒤에 한다 —
        //    여기서 잠그면 아래 early return 경로에서 목록이 잠긴 채 영영 풀리지 않는다.

        // 4) 고스트 활성화 + 포인터 위치로 이동
        EnsureGhost();
        if (m_ghostRect == null)
        {
            // 여기서 멈추면 스크롤 소유권만 뺏고 드래그는 시작 못 한 상태라 조작이 먹통처럼 보인다 — 배선 누락을 반드시 알린다.
            Debug.LogError("[DeckEditDragController] ghostPrefab/dragLayer 미배선 — 드래그를 시작할 수 없다.");
            return;
        }

        // 손 뗄 때 타일 클릭(빈 칸 자동 배치)이 뒤늦게 발화하는 것 차단.
        // 고스트 확보에 성공한 뒤에 내리는 게 중요하다 — 위 early return 경로에서 미리 내려버리면
        // 드래그도 못 하고 클릭 지름길까지 죽어 조작이 통째로 먹통이 된다.
        _data.eligibleForClick = false;

        // 관성을 끊고 드래그가 끝날 때까지 목록을 세운다(OnEndDrag를 줘도 velocity는 남는다).
        LockScroll(_ownerScroll);

        m_card     = _card;
        m_data     = _data;
        m_finger   = ResolveFinger(_data.position);
        m_dragging = true;

        // 고스트는 1회 Instantiate 후 재사용되므로 크기는 드래그마다 다시 준다 —
        // 해상도나 패널이 바뀌면 cellSize도 바뀐다. else가 필요하다: 안 그러면 직전 드래그의 크기가 그대로 남는다.
        m_ghostRect.sizeDelta = _cellSize.x > 0f && _cellSize.y > 0f ? _cellSize : ghostSize;

        if (m_ghostView != null) m_ghostView.Bind(_card, true);
        m_ghostRect.gameObject.SetActive(true);
        MoveGhost(_data.position);
    }

    public void Cancel()
    {
        Finish(-1);
    }

    void OnDisable()
    {
        Cancel();
    }

    void Update()
    {
        if (!m_dragging) return;

        // 손 뗐는지는 여기서 판정한다. 타일 OnPointerUp에 의존하면
        // 드래그 중 타일이 재빌드/비활성화될 때 이벤트를 놓쳐 고스트가 화면에 붙어버린다.
        ReadPointer(out Vector2 t_pos, out bool t_held);
        MoveGhost(t_pos);
        HighlightHoveredSlot(t_pos);

        if (!t_held) End(t_pos);
    }

    void End(Vector2 _screenPos)
    {
        int t_hit = HitTestSlot(_screenPos);

        // 히트 결과는 provider 리스트 위치다. 콜백에는 슬롯이 스스로 아는 편성 인덱스를 넘긴다 —
        // 리스트 순서와 편성 번호가 어긋나는 배선이 생겨도 엉뚱한 칸에 꽂히지 않게.
        int t_slotIndex = t_hit;
        if (t_hit >= 0)
        {
            var t_slots = m_slotProvider?.Invoke();
            if (t_slots != null && t_slots[t_hit] != null && t_slots[t_hit].Index >= 0)
                t_slotIndex = t_slots[t_hit].Index;
        }

        Finish(t_slotIndex);
    }

    // _slotIndex >= 0이면 드롭 성사. 성사 여부와 무관하게 드래그 상태는 항상 여기서 정리된다.
    void Finish(int _slotIndex)
    {
        int              t_card = m_card;
        PointerEventData t_data = m_data;

        if (m_ghostRect != null) m_ghostRect.gameObject.SetActive(false);
        ClearHighlight();
        UnlockScroll();

        m_dragging = false;
        m_card     = 0;
        m_data     = null;
        m_finger   = -1;

        // 손을 뗄 때까지 입력 모듈이 이 포인터를 계속 갱신하므로, 종료 시점에도 클릭 자격을 다시 눌러둔다
        // (Begin 이후 다른 코드가 되살릴 여지 차단 — 드롭 직후 타일 클릭이 뒤늦게 터지는 것을 막는다).
        if (t_data != null) t_data.eligibleForClick = false;

        // 콜백은 정리 이후에 부른다 — 콜백이 그리드를 재빌드하며 Cancel을 되부를 수 있어 재진입에 안전해야 한다.
        // 종료 통지가 드롭보다 먼저다 — 드롭 콜백이 칸을 재바인딩하며 새로 칠한 표시를 뒤늦은 통지가 지우면 안 된다.
        m_onEnded?.Invoke();

        if (_slotIndex >= 0 && t_card > 0) m_onDropped?.Invoke(_slotIndex, t_card);
    }

    // ScrollRect를 통째로 끄지 않는다(enabled=false) — 드래그 도중 꺼지면 OnEndDrag를 못 받아
    // 내부 드래그 상태가 켜진 채 남고 다음 터치에서 관성이 튄다. 축만 닫으면 이벤트는 정상적으로 끝난다.
    // 원래 값을 기억했다 되돌린다 — 둘 다 true로 되돌리면 세로 전용 목록에 없던 가로 축이 생긴다.
    void LockScroll(ScrollRect _scroll)
    {
        if (_scroll == null) return;

        UnlockScroll();   // 앞선 드래그가 남긴 잠금이 있으면 먼저 되돌린다(꺼둔 값을 원래 값으로 기억하지 않게)

        m_lockedScroll        = _scroll;
        m_scrollWasVertical   = _scroll.vertical;
        m_scrollWasHorizontal = _scroll.horizontal;

        _scroll.StopMovement();
        _scroll.velocity   = Vector2.zero;
        _scroll.vertical   = false;
        _scroll.horizontal = false;
    }

    void UnlockScroll()
    {
        if (m_lockedScroll == null) return;

        m_lockedScroll.vertical   = m_scrollWasVertical;
        m_lockedScroll.horizontal = m_scrollWasHorizontal;

        // 잠긴 동안 쌓인 이동량이 풀리는 순간 튀어나오지 않게 한다.
        m_lockedScroll.StopMovement();
        m_lockedScroll.velocity = Vector2.zero;
        m_lockedScroll          = null;
    }

    // 고스트는 최초 1회만 Instantiate하고 이후 SetActive로 토글한다(드래그마다 Instantiate하면 GC와 레이아웃 비용이 붙는다).
    // UIPoolManager.AddOrUpdateUI<PooledCardElement>는 쓰지 않는다 — DontDestroyOnLoad된 자체 캔버스라
    // LobbyCanvas와 좌표계(스케일·해상도 대응)가 어긋나 고스트가 손끝에서 벗어난다.
    void EnsureGhost()
    {
        if (m_ghostRect != null || ghostPrefab == null || dragLayer == null) return;

        var t_ghost = Instantiate(ghostPrefab, dragLayer);
        var t_go    = t_ghost.gameObject;

        // 고스트는 순수 표시물 — 입력 로직이 남아 있으면 롱프레스가 중첩 발화한다.
        // enabled=false를 먼저 주는 이유: Destroy는 프레임 끝에 반영되므로 그 사이 한 프레임이 살아 있다.
        t_ghost.enabled = false;
        Destroy(t_ghost);

        var t_longPress = t_go.GetComponentInChildren<LongPressDetector>(true);
        if (t_longPress != null) { t_longPress.enabled = false; Destroy(t_longPress); }

        var t_group = t_go.GetComponent<CanvasGroup>();
        if (t_group == null) t_group = t_go.AddComponent<CanvasGroup>();
        t_group.blocksRaycasts = false;   // 고스트가 자기 밑의 슬롯 레이캐스트를 가리면 안 된다
        t_group.alpha          = ghostAlpha;
        m_ghostGroup           = t_group;

        m_ghostRect = (RectTransform)t_go.transform;

        // 앵커·피벗을 중앙으로 강제해야 ScreenPointToLocalPointInRectangle의 결과(localPoint)가
        // 그대로 anchoredPosition이 된다. 프리팹이 그리드용 좌상단 앵커를 갖고 있으면 손끝과 어긋난다.
        m_ghostRect.anchorMin  = new Vector2(0.5f, 0.5f);
        m_ghostRect.anchorMax  = new Vector2(0.5f, 0.5f);
        m_ghostRect.pivot      = new Vector2(0.5f, 0.5f);
        m_ghostRect.localScale = Vector3.one * ghostScale;

        // 크기도 반드시 직접 준다. 타일 프리팹 루트는 sizeDelta가 0이고 실제 크기를 GridLayoutGroup이 주입하는데,
        // DragLayer에는 레이아웃 그룹이 없어 그대로 두면 고스트가 0x0으로 렌더링돼 화면에 아무것도 안 보인다.
        // 실제 cellSize는 Begin이 덮어쓴다 — 여기 값은 그걸 못 받았을 때의 폴백이다.
        m_ghostRect.sizeDelta = ghostSize;

        m_ghostView = t_go.GetComponent<CardVisualView>();
        if (m_ghostView == null) m_ghostView = t_go.GetComponentInChildren<CardVisualView>(true);

        t_go.SetActive(false);
    }

    void MoveGhost(Vector2 _screenPos)
    {
        if (m_ghostRect == null || dragLayer == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(dragLayer, _screenPos, EventCam, out Vector2 t_local);
        m_ghostRect.anchoredPosition = t_local;
    }

    void HighlightHoveredSlot(Vector2 _screenPos)
    {
        var t_slots = m_slotProvider?.Invoke();
        if (t_slots == null) return;

        int t_hovered = HitTestSlot(_screenPos);
        for (int t_i = 0; t_i < t_slots.Count; t_i++)
        {
            if (t_slots[t_i] != null) t_slots[t_i].SetHighlight(t_i == t_hovered);
        }
    }

    void ClearHighlight()
    {
        var t_slots = m_slotProvider?.Invoke();
        if (t_slots == null) return;

        for (int t_i = 0; t_i < t_slots.Count; t_i++)
        {
            if (t_slots[t_i] != null) t_slots[t_i].SetHighlight(false);
        }
    }

    // 히트한 슬롯의 리스트 인덱스. 없으면 -1.
    int HitTestSlot(Vector2 _screenPos)
    {
        var t_slots = m_slotProvider?.Invoke();
        if (t_slots == null) return -1;

        for (int t_i = 0; t_i < t_slots.Count; t_i++)
        {
            var t_slot = t_slots[t_i];
            if (t_slot == null || t_slot.Rect == null) continue;

            if (RectTransformUtility.RectangleContainsScreenPoint(t_slot.Rect, _screenPos, EventCam))
                return t_i;
        }
        return -1;
    }

    // 드래그를 시작한 그 손가락만 추적한다.
    // Input.GetTouch(0)으로 고정해 읽으면, 두 번째 손가락이 닿았다가 첫 손가락을 떼는 순간 touch 배열이 당겨져
    // 고스트가 엉뚱한 손가락으로 순간이동하고 드래그가 끝나지도 않는다.
    void ReadPointer(out Vector2 _pos, out bool _held)
    {
        if (m_data == null)
        {
            _pos = Vector2.zero; _held = false;
            return;
        }

        if (m_finger >= 0)
        {
            for (int t_i = 0; t_i < Input.touchCount; t_i++)
            {
                Touch t_touch = Input.GetTouch(t_i);
                if (t_touch.fingerId != m_finger) continue;

                _pos  = t_touch.position;
                _held = t_touch.phase != TouchPhase.Ended && t_touch.phase != TouchPhase.Canceled;
                return;
            }

            // 그 손가락이 터치 목록에서 사라졌다 = 이미 뗐다. 마지막으로 알던 위치에서 종료 처리한다.
            _pos  = m_data.position;
            _held = false;
            return;
        }

        _pos  = Input.mousePosition;   // ProjectSettings activeInputHandler=Input Manager(Old)
        _held = Input.GetMouseButton(0);
    }

    // 손가락 판별에 PointerEventData.pointerId를 쓰지 않는다 — 그 값의 의미가 입력 모듈마다 다르다.
    // 레거시 StandaloneInputModule은 마우스에 음수(-1)를 주지만, 이 프로젝트가 쓰는 InputSystemUIInputModule은
    // 마우스에도 양수를 주고 터치 id도 레거시 fingerId와 체계가 다르다. 그대로 믿으면 마우스가 "터치"로 읽혀
    // 매칭에 실패하고, 드래그가 시작된 첫 프레임에 "이미 뗐다"로 판정돼 고스트가 뜨자마자 사라진다.
    // 그래서 추적 대상은 위치·눌림을 실제로 읽는 쪽(레거시 Input)에서 직접 고른다.
    static int ResolveFinger(Vector2 _startPos)
    {
        int   t_finger = -1;
        float t_best   = float.MaxValue;

        for (int t_i = 0; t_i < Input.touchCount; t_i++)
        {
            Touch t_touch = Input.GetTouch(t_i);
            if (t_touch.phase == TouchPhase.Ended || t_touch.phase == TouchPhase.Canceled) continue;

            float t_dist = Vector2.Distance(t_touch.position, _startPos);
            if (t_dist >= t_best) continue;

            t_best   = t_dist;
            t_finger = t_touch.fingerId;
        }

        return t_finger;   // 터치가 없으면 -1 = 마우스 경로
    }
}
