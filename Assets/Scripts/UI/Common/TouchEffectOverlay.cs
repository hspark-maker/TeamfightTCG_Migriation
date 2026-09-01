using UnityEngine;

/// <summary>화면 아무 곳이나 누르면 그 자리에 터지는 전역 터치 이펙트.
///
/// 어느 화면 위에서 눌러도 보여야 하므로 정렬은 <see cref="UiSortingOrder.TouchEffect"/> 한 층이고,
/// <b>입력은 절대 먹지 않는다</b> — 루트에 GraphicRaycaster를 붙이지 않고 CanvasGroup도 raycast를 끈다.
/// 이 규칙이 깨지면 화면 전체가 눌리지 않는 증상이 에러 한 줄 없이 난다.
///
/// 발(<see cref="TouchEffectItem"/>)은 프리팹에 저작해 둔 것을 돌려 쓴다. 런타임 Instantiate는 하지 않는다.</summary>
public sealed class TouchEffectOverlay : SingletonOverlayBase
{
    static TouchEffectOverlay s_instance;

    [SerializeField] Canvas           overlayCanvas;
    [SerializeField] CanvasGroup      canvasGroup;
    [SerializeField] RectTransform    stage;      // 발들이 놓이는 기준 사각형
    [SerializeField] TouchEffectItem[] items;     // 저작된 발. 순환해서 쓴다

    int cursor;

    /// <summary>연타를 눌린 만큼 다 그리면 화면이 하얘진다 — 같은 프레임에 나오는 발 수를 묶는다.</summary>
    const int MAX_PER_FRAME = 4;

    /// <summary>끄고 켜는 스위치. 연출 중 손맛이 방해되는 구간에서 잠깐 내릴 때 쓴다.</summary>
    public static bool Enabled { get; set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_instance = null;
        Enabled    = true;
    }

    /// <summary>초기화에서 한 번 세운다. 멱등이라 재시도로 다시 들어와도 사본이 늘지 않는다.</summary>
    public static void Install()
    {
        if (s_instance != null) return;

        GameObject t_prefab = RuntimeOverlayPrefabs.Get<TouchEffectOverlay>();
        if (t_prefab == null) return;

        GameObject t_object = Instantiate(t_prefab);
        s_instance = t_object.GetComponent<TouchEffectOverlay>();
        if (s_instance == null)
        {
            Debug.LogError($"[TouchEffectOverlay] {t_prefab.name} 루트에 TouchEffectOverlay가 없습니다.", t_prefab);
            Destroy(t_object);
            return;
        }

        DontDestroyOnLoad(t_object);
        s_instance.Bind();
    }

    void Bind()
    {
        if (this.overlayCanvas == null) this.overlayCanvas = GetComponent<Canvas>();
        if (this.canvasGroup   == null) this.canvasGroup   = GetComponent<CanvasGroup>();
        if (this.stage         == null) this.stage         = transform as RectTransform;

        if (this.overlayCanvas == null)
            Debug.LogError("[TouchEffectOverlay] 프리팹 루트에 Canvas가 필요합니다.", this);

        UiSortingOrder.Stamp(this.overlayCanvas, UiSortingOrder.TouchEffect);

        if (this.canvasGroup != null)
        {
            // 눌린 것을 그리기만 하는 층이라 그 밑의 입력을 가로채면 안 된다.
            this.canvasGroup.blocksRaycasts = false;
            this.canvasGroup.interactable   = false;
        }

        if (this.items == null || this.items.Length == 0)
            Debug.LogError("[TouchEffectOverlay] 저작된 TouchEffectItem이 없습니다 — 아무것도 그려지지 않습니다.", this);

        for (int i = 0; i < (this.items?.Length ?? 0); i++)
            if (this.items[i] != null) this.items[i].gameObject.SetActive(false);
    }

    /// <summary>유저 입력과 무관하게 지정한 스크린 좌표에서 한 발 터뜨린다(튜토리얼 손가락 같은 유도 연출용).
    ///
    /// 설치 전이거나 <see cref="Enabled"/>가 내려가 있으면 <b>조용히 아무 일도 하지 않는다</b> —
    /// 이 fx는 장식이지 진행 조건이 아니라, 프리팹 미배선·초기화 실패가 부르는 쪽을 막으면 안 된다.</summary>
    public static void PlayAt(Vector2 _screenPos)
    {
        if (s_instance == null || !Enabled) return;

        s_instance.Emit(_screenPos);
    }

    void Update()
    {
        if (!Enabled) return;

        int t_touches = Input.touchCount;
        if (t_touches > 0)
        {
            // 터치가 있는 프레임에는 마우스를 보지 않는다 — 모바일에서 터치 0번이 마우스로도 잡혀 한 번 누른 것이 두 번 튄다.
            int t_emitted = 0;
            for (int i = 0; i < t_touches && t_emitted < MAX_PER_FRAME; i++)
            {
                Touch t_touch = Input.GetTouch(i);
                if (t_touch.phase != TouchPhase.Began) continue;

                Emit(t_touch.position);
                t_emitted++;
            }
            return;
        }

        if (Input.GetMouseButtonDown(0)) Emit(Input.mousePosition);
    }

    void Emit(Vector2 _screenPos)
    {
        if (this.items == null || this.items.Length == 0 || this.stage == null) return;

        // Overlay 캔버스는 카메라가 null이고 Camera 모드는 worldCamera가 필요하다 — 캔버스에게 물어서 한 줄로 접는다.
        Camera t_camera = (this.overlayCanvas != null &&
                           this.overlayCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? this.overlayCanvas.worldCamera
            : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                this.stage, _screenPos, t_camera, out Vector2 t_local))
            return;

        TouchEffectItem t_item = NextItem();
        if (t_item == null) return;

        t_item.Play(t_local);
    }

    /// <summary>다음 발을 고른다. 쉬고 있는 발이 있으면 그것부터, 전부 재생 중이면 가장 오래된 것을 뺏는다.</summary>
    TouchEffectItem NextItem()
    {
        TouchEffectItem t_oldest = null;

        for (int i = 0; i < this.items.Length; i++)
        {
            TouchEffectItem t_item = this.items[(this.cursor + i) % this.items.Length];
            if (t_item == null) continue;

            if (t_oldest == null) t_oldest = t_item;
            if (t_item.gameObject.activeSelf) continue;

            this.cursor = (this.cursor + i + 1) % this.items.Length;
            return t_item;
        }

        this.cursor = (this.cursor + 1) % this.items.Length;
        return t_oldest;
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }
}
