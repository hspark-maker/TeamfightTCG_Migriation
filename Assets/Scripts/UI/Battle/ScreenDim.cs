using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Battle Canvas 직하에서 전체 화면 딤을 공유한다.
/// SafeArea 다음, MulliganOverlay 앞 형제 순서를 유지해야 HUD는 가리고 안내 UI는 가리지 않는다.</summary>
public class ScreenDim : MonoBehaviour
{
    sealed class Request
    {
        public object owner;
        public float alpha;
        public bool block;
        public bool hasHole;
        public Rect hole;
        public float fade;
    }

    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] Image full;
    [SerializeField] RectTransform holeTop;
    [SerializeField] RectTransform holeBottom;
    [SerializeField] RectTransform holeLeft;
    [SerializeField] RectTransform holeRight;

    static ScreenDim instance;
    readonly List<Request> requests = new List<Request>();

    public static bool IsAvailable => instance != null;

    void Awake()
    {
        if (instance != null && instance != this)
            Debug.LogWarning("[ScreenDim] 씬에 인스턴스가 둘 이상 있습니다. 마지막 인스턴스를 사용합니다.");
        instance = this;
        ApplyHidden();
    }

    void OnDestroy()
    {
        if (instance != this) return;
        this.requests.Clear();
        instance = null;
    }

    public static void Show(object _owner, float _alpha = 0.62f, bool _block = true, float _fade = 0f)
        => instance?.Push(_owner, _alpha, _block, false, default, _fade);

    public static void ShowWithHole(object _owner, Rect _screenRect, float _alpha = 0.62f, bool _block = true)
        => instance?.Push(_owner, _alpha, _block, true, _screenRect, 0f);

    public static void Hide(object _owner)
    {
        if (instance == null || _owner == null) return;
        instance.Remove(_owner);
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

    void ApplyTop(bool _fadeFromHidden)
    {
        Request t_request = this.requests[this.requests.Count - 1];
        this.canvasGroup.DOKill();
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

        if (_fadeFromHidden && t_request.fade > 0f)
        {
            this.canvasGroup.alpha = 0f;
            this.canvasGroup.DOFade(t_request.alpha, t_request.fade).SetLink(gameObject);
        }
        else this.canvasGroup.alpha = t_request.alpha;
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
        this.canvasGroup.DOKill();
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
