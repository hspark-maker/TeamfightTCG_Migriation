using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public enum EDimLayer
{
    Full    = 0,
    Content = 1,
}

/// <summary>씬에 저작된 레이어별 인스턴스에서 공용 화면 딤을 표시한다.</summary>
public class ScreenDim : MonoBehaviour
{
    const int LAYER_COUNT = 2;

    internal sealed class Request
    {
        public object owner;
        public float alpha;
        public bool block;
        public bool hasHole;
        public Rect hole;
        public float fade;
        public bool managed;
        public int sortingOrder;
        public Color color = Color.black;
    }

    /// <summary>오버레이가 요청하는 암막의 표시 설정.</summary>
    public readonly struct Options
    {
        public readonly float Alpha;
        public readonly Color Color;
        public readonly bool Block;
        public readonly float Fade;
        public readonly int SortingOrder;

        public Options(float alpha, Color color, bool block, float fade, int sortingOrder)
        {
            Alpha = Mathf.Clamp01(alpha);
            Color = new Color(color.r, color.g, color.b, 1f);
            Block = block;
            Fade = Mathf.Max(0f, fade);
            SortingOrder = sortingOrder;
        }
    }

    /// <summary>한 번의 표시 요청. 이전 표시의 핸들은 새 표시를 해제하지 못한다.</summary>
    public sealed class Handle
    {
        readonly ScreenDim _screen;
        readonly Request _request;

        internal Handle(ScreenDim screen, Request request)
        {
            _screen = screen;
            _request = request;
        }

        public void Release(float duration = 0f)
        {
            if (_screen != null) _screen.Release(_request, Mathf.Max(0f, duration));
        }

        public void SetColor(Color color)
        {
            if (_screen != null) _screen.SetRequestColor(_request, color);
        }

        /// <summary>강제 비활성화 시 자기 요청의 남은 퇴장까지 즉시 취소한다.</summary>
        public void Cancel()
        {
            if (_screen == null) return;
            _screen.Release(_request, 0f);
            if (ReferenceEquals(_screen._releasingRequest, _request)) _screen.ApplyHidden();
        }
    }

    [SerializeField] EDimLayer layer = EDimLayer.Full;
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] Image full;
    [SerializeField] RectTransform holeTop;
    [SerializeField] RectTransform holeBottom;
    [SerializeField] RectTransform holeLeft;
    [SerializeField] RectTransform holeRight;
    [SerializeField] Canvas sortingCanvas;

    static readonly ScreenDim[] s_instances = new ScreenDim[LAYER_COUNT];
    readonly List<Request> requests = new List<Request>();
    bool _pendingRelease;
    float _releaseDuration;
    Request _releasingRequest;

    public static bool IsAvailable => Get(EDimLayer.Full) != null;

    public static ScreenDim Get(EDimLayer _layer)
    {
        int t_index = (int)_layer;
        return t_index >= 0 && t_index < s_instances.Length ? s_instances[t_index] : null;
    }

    public static bool IsAvailableAt(EDimLayer _layer) => Get(_layer) != null;

    /// <summary>로비의 공통 Full 딤을 요청한다.</summary>
    public static Handle Acquire(object owner, Options options)
    {
        ScreenDim screen = Get(EDimLayer.Full);
        if (screen == null || screen.sortingCanvas == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError("[ScreenDim] Overlay requires a Full dim with an authored sorting Canvas.");
#endif
            return null;
        }
        if (owner == null) return null;
        screen.PruneDestroyedOwners();
        screen.RemoveOwner(owner);
        var request = new Request
        {
            owner = owner, alpha = options.Alpha, color = options.Color,
            block = options.Block, fade = options.Fade,
            managed = true, sortingOrder = options.SortingOrder
        };
        screen.requests.Add(request);
        screen.ApplyTop(false, true);
        return new Handle(screen, request);
    }

    void Awake()
    {
        int t_index = (int)this.layer;
        if (t_index < 0 || t_index >= s_instances.Length)
        {
            Debug.LogError($"[ScreenDim] Unsupported layer: {this.layer}", this);
            return;
        }

        ScreenDim t_previous = Get(this.layer);
        if (t_previous != null && t_previous != this)
            Debug.LogWarning($"[ScreenDim] There is more than one {this.layer} instance in the scene. Using the last one.");
        s_instances[t_index] = this;
        ApplyHidden();
    }

    void OnDestroy()
    {
        this.canvasGroup.DOKill();
        this.full.DOKill();
        if (Get(this.layer) != this) return;
        this.requests.Clear();
        s_instances[(int)this.layer] = null;
    }

    void OnDisable()
    {
        this.requests.Clear();
        ApplyHidden();
    }

    void LateUpdate()
    {
        if (!_pendingRelease) return;
        _pendingRelease = false;
        if (this.requests.Count != 0) return;
        this.canvasGroup.DOFade(0f, _releaseDuration).SetLink(gameObject)
            .OnComplete(() => { if (this.requests.Count == 0) ApplyHidden(); });
    }

    void Release(Request request, float duration)
    {
        int index = this.requests.IndexOf(request);
        if (index < 0) return;
        bool top = index == this.requests.Count - 1;
        this.requests.RemoveAt(index);
        if (!top) return;
        PruneDestroyedOwners();
        if (this.requests.Count > 0)
        {
            ApplyTop(false, true);
            return;
        }
        if (duration <= 0f) { ApplyHidden(); return; }
        this.canvasGroup.DOKill();
        this.full.DOKill();
        // 같은 프레임의 다음 화면 요청이 들어오기 전에 알파를 낮추지 않는다.
        _pendingRelease = true;
        _releaseDuration = duration;
        _releasingRequest = request;
    }

    void SetRequestColor(Request request, Color color)
    {
        if (!this.requests.Contains(request)) return;
        request.color = new Color(color.r, color.g, color.b, 1f);
        if (!ReferenceEquals(request, this.requests[this.requests.Count - 1])) return;
        this.full.DOKill();
        this.full.color = request.color;
    }

    public static void Show(object _owner, float _alpha = 0.62f, bool _block = true, float _fade = 0f,
        EDimLayer _layer = EDimLayer.Full)
        => Get(_layer)?.Push(_owner, _alpha, _block, false, default, _fade);

    public static void ShowWithHole(object _owner, Rect _screenRect, float _alpha = 0.62f, bool _block = true)
        => Get(EDimLayer.Full)?.Push(_owner, _alpha, _block, true, _screenRect, 0f);

    /// <summary>레이어를 지정하지 않으면 전 레이어에서 걷는다 —
    /// Show에만 레이어를 넘기고 Hide에서 빠뜨리면 딤이 켜진 채 남아 입력이 영구히 죽기 때문이다.</summary>
    public static void Hide(object _owner)
    {
        if (_owner == null) return;

        for (int i = 0; i < s_instances.Length; i++)
        {
            ScreenDim t_instance = s_instances[i];
            if (t_instance != null) t_instance.Remove(_owner);
        }
    }

    public static void Hide(object _owner, EDimLayer _layer)
    {
        ScreenDim t_instance = Get(_layer);
        if (t_instance == null || _owner == null) return;
        t_instance.Remove(_owner);
    }

    void Push(object _owner, float _alpha, bool _block, bool _hasHole, Rect _hole, float _fade)
    {
        if (_owner == null) return;

        PruneDestroyedOwners();
        bool t_wasEmpty = this.requests.Count == 0;
        RemoveOwner(_owner);
        this.requests.Add(new Request
        {
            owner = _owner,
            alpha = Mathf.Clamp01(_alpha),
            block = _block,
            hasHole = _hasHole,
            hole = _hole,
            fade = Mathf.Max(0f, _fade)
        });
        ApplyTop(t_wasEmpty);
    }

    void Remove(object _owner)
    {
        // 다른 화면의 중복 Hide가 마지막 오버레이의 퇴장 페이드를 자르면 안 된다.
        if (!this.requests.Exists(request => ReferenceEquals(request.owner, _owner))) return;
        object t_previousTop = this.requests.Count > 0 ? this.requests[this.requests.Count - 1].owner : null;
        RemoveOwner(_owner);
        PruneDestroyedOwners();
        if (this.requests.Count == 0) ApplyHidden();
        else if (!ReferenceEquals(t_previousTop, this.requests[this.requests.Count - 1].owner)) ApplyTop(false);
    }

    void RemoveOwner(object _owner)
    {
        for (int i = this.requests.Count - 1; i >= 0; i--)
            if (ReferenceEquals(this.requests[i].owner, _owner)) this.requests.RemoveAt(i);
    }

    void PruneDestroyedOwners()
    {
        for (int i = this.requests.Count - 1; i >= 0; i--)
            if (this.requests[i].owner is Object t_owner && t_owner == null) this.requests.RemoveAt(i);
    }

    void ApplyTop(bool _fadeFromHidden, bool continuous = false)
    {
        Request t_request = this.requests[this.requests.Count - 1];
        _pendingRelease = false;
        _releasingRequest = null;
        this.canvasGroup.DOKill();
        this.full.DOKill();
        if (sortingCanvas != null)
        {
            if (t_request.managed)
            {
                sortingCanvas.overrideSorting = true;
                UiSortingOrder.Stamp(sortingCanvas, t_request.sortingOrder);
            }
            else UiSortingOrder.DropNested(sortingCanvas);
        }
        this.canvasGroup.blocksRaycasts = t_request.block;
        this.canvasGroup.interactable = false;

        if (t_request.hasHole && t_request.hole.width > 0f && t_request.hole.height > 0f)
        {
            this.full.gameObject.SetActive(false);
            ApplyHole(t_request.hole);
        }
        else
        {
            HideHole();
            this.full.gameObject.SetActive(true);
        }

        if (continuous && t_request.managed && t_request.fade > 0f)
        {
            if (this.canvasGroup.alpha <= 0f) this.full.color = t_request.color;
            else this.full.DOColor(t_request.color, t_request.fade).SetLink(gameObject);
            this.canvasGroup.DOFade(t_request.alpha, t_request.fade).SetLink(gameObject);
        }
        else if (_fadeFromHidden && t_request.fade > 0f)
        {
            this.full.color = t_request.color;
            this.canvasGroup.alpha = 0f;
            this.canvasGroup.DOFade(t_request.alpha, t_request.fade).SetLink(gameObject);
        }
        else
        {
            this.full.color = t_request.color;
            this.canvasGroup.alpha = t_request.alpha;
        }
    }

    void ApplyHole(Rect _screenRect)
    {
        const float k_pad = 24f;
        float t_left   = Mathf.Clamp01(Mathf.Round(_screenRect.xMin - k_pad) / Screen.width);
        float t_right  = Mathf.Clamp01(Mathf.Round(_screenRect.xMax + k_pad) / Screen.width);
        float t_bottom = Mathf.Clamp01(Mathf.Round(_screenRect.yMin - k_pad) / Screen.height);
        float t_top    = Mathf.Clamp01(Mathf.Round(_screenRect.yMax + k_pad) / Screen.height);

        Place(this.holeTop,    new Vector2(0f, t_top),         new Vector2(1f, 1f));
        Place(this.holeBottom, new Vector2(0f, 0f),            new Vector2(1f, t_bottom));
        Place(this.holeLeft,   new Vector2(0f, t_bottom),      new Vector2(t_left, t_top));
        Place(this.holeRight,  new Vector2(t_right, t_bottom), new Vector2(1f, t_top));
    }

    static void Place(RectTransform _rect, Vector2 _min, Vector2 _max)
    {
        bool t_visible = _max.x - _min.x > 0.0001f && _max.y - _min.y > 0.0001f;
        _rect.gameObject.SetActive(t_visible);
        if (!t_visible) return;
        _rect.anchorMin = _min;
        _rect.anchorMax = _max;
        _rect.offsetMin = Vector2.zero;
        _rect.offsetMax = Vector2.zero;
    }

    void ApplyHidden()
    {
        _pendingRelease = false;
        _releasingRequest = null;
        this.canvasGroup.DOKill();
        this.full.DOKill();
        this.full.color = Color.black;
        if (sortingCanvas != null) UiSortingOrder.DropNested(sortingCanvas);
        this.canvasGroup.alpha = 0f;
        this.canvasGroup.interactable = false;
        this.canvasGroup.blocksRaycasts = false;
        this.full.gameObject.SetActive(false);
        HideHole();
    }

    void HideHole()
    {
        this.holeTop.gameObject.SetActive(false);
        this.holeBottom.gameObject.SetActive(false);
        this.holeLeft.gameObject.SetActive(false);
        this.holeRight.gameObject.SetActive(false);
    }
}
