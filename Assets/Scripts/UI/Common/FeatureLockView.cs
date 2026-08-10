using UnityEngine;
using UnityEngine.UI;

// 튜토리얼 진행으로 아직 열리지 않은 UI에 잠김 룩을 얹는 표시 컴포넌트.
// 판정은 갖지 않는다 — OutgameFeatureLock이 단일 진실원이고 여기는 그 결과를 그리기만 한다.
//
// 차단 수단이 둘로 갈리는 것이 이 컴포넌트의 핵심 계약이다:
//  - 단일 위젯(버튼 하나)은 interactable=false가 막는다. 오버레이를 버튼 "자식"으로 넣으면
//    자식 그래픽 클릭이 부모 Button으로 버블링되므로 딤만으로는 못 막는다.
//  - 영역(패널 전체)은 오버레이 딤의 raycastTarget이 막는다. 그 안의 위젯을 하나씩 잠글 필요가 없다.
// 따라서 controlInteractable을 끄는 대상(이미 다른 스크립트가 interactable을 매 갱신 덮어쓰는 곳)은
// 반드시 그쪽 계산식에 OutgameFeatureLock.IsUnlocked를 넣어야 한다 — 여긴 룩만 담당하게 된다.
public class FeatureLockView : MonoBehaviour
{
    const string OverlayPath = "UI/LockOverlay";
    const string DimName     = "Dim";   // 오버레이 프리팹에서 실루엣을 갈아끼울 자식

    [Tooltip("이 UI를 여는 기능 키. None이면 항상 열려 있다")]
    [SerializeField] EOutgameFeature feature;

    [Tooltip("잠글 대상. 비우면 자기 오브젝트에서 찾는다")]
    [SerializeField] Selectable target;

    [Tooltip("interactable을 직접 건드릴지. 이미 다른 스크립트가 매 갱신마다 덮어쓰는 대상은 꺼서 경합을 없앤다")]
    [SerializeField] bool controlInteractable = true;

    [Tooltip("잠김 오버레이를 놓을 자리. 비우면 자기 RectTransform")]
    [SerializeField] RectTransform overlayParent;

    GameObject m_overlay;
    Selectable m_target;
    bool       m_targetResolved;
    bool       m_overlayMissing;   // 프리팹 미배치 경고·재시도는 1회로 끝낸다

    public EOutgameFeature Feature => feature;

    /// <summary>지금 이 UI가 잠겨 있는가. 튜토리얼 게이트가 "왜 타깃이 안 눌리는지"를 진단할 때 읽는다.</summary>
    public bool IsLocked => feature != EOutgameFeature.None && !OutgameFeatureLock.IsUnlocked(feature);

    /// <summary>런타임 부착용(탭 버튼처럼 프리팹에 컴포넌트를 못 붙이는 대상).
    /// AddComponent 직후엔 OnEnable이 이미 지나갔으므로 여기서 다시 적용한다.</summary>
    public void Bind(EOutgameFeature _feature, bool _controlInteractable = true)
    {
        this.feature             = _feature;
        this.controlInteractable = _controlInteractable;

        if (isActiveAndEnabled) Apply();
    }

    void OnEnable()
    {
        OutgameFeatureLock.OnChanged += Apply;
        Apply();
    }

    void OnDisable()
    {
        OutgameFeatureLock.OnChanged -= Apply;
    }

    void Apply()
    {
        bool t_unlocked = OutgameFeatureLock.IsUnlocked(this.feature);

        if (this.controlInteractable)
        {
            var t_target = ResolveTarget();
            if (t_target != null) t_target.interactable = t_unlocked;
        }

        // 열린 대다수는 오버레이를 만들지도 않는다 — 잠겼을 때만 생성한다.
        if (t_unlocked)
        {
            if (m_overlay != null) m_overlay.SetActive(false);
            return;
        }

        EnsureOverlay();
        if (m_overlay != null) m_overlay.SetActive(true);
    }

    Selectable ResolveTarget()
    {
        if (m_targetResolved) return m_target;

        m_targetResolved = true;
        m_target         = this.target != null ? this.target : GetComponent<Selectable>();
        return m_target;
    }

    void EnsureOverlay()
    {
        if (m_overlay != null || m_overlayMissing) return;

        var t_parent = this.overlayParent != null ? this.overlayParent : transform as RectTransform;
        if (t_parent == null)
        {
            m_overlayMissing = true;
            Debug.LogWarning($"[FeatureLockView] '{name}'이 RectTransform이 아니라 잠김 표시를 얹을 자리가 없습니다.");
            return;
        }

        var t_prefab = Resources.Load<GameObject>(OverlayPath);
        if (t_prefab == null)
        {
            m_overlayMissing = true;
            Debug.LogWarning($"[FeatureLockView] Resources/{OverlayPath} 미배치 — '{name}'의 잠김 룩을 그리지 못합니다(차단은 interactable로만 걸립니다).");
            return;
        }

        m_overlay      = Instantiate(t_prefab, t_parent, false);
        m_overlay.name = "LockOverlay";

        // 대상 전체를 덮도록 늘린다 — 프리팹 앵커가 좁게 저작돼도 차단 면적을 보장한다.
        if (m_overlay.transform is RectTransform t_rect)
        {
            t_rect.anchorMin  = Vector2.zero;
            t_rect.anchorMax  = Vector2.one;
            t_rect.offsetMin  = Vector2.zero;
            t_rect.offsetMax  = Vector2.zero;
            t_rect.localScale = Vector3.one;
        }

        m_overlay.transform.SetAsLastSibling();   // 내용물 위에 그린다

        ShapeDim(t_parent);
    }

    // 딤이 대상 실루엣을 따르게 스프라이트를 복사한다 — 원형·라운드 버튼에 사각 딤이 얹히면 모양이 어긋난다.
    // 색은 프리팹의 검정 반투명 그대로라 스프라이트 알파가 곧 딤 모양이 된다.
    void ShapeDim(RectTransform _parent)
    {
        var t_dim = m_overlay.transform.Find(DimName);
        var t_dimImage = t_dim != null ? t_dim.GetComponent<Image>() : null;
        if (t_dimImage == null) return;

        var t_shape = FindShape(_parent);
        if (t_shape == null || t_shape.sprite == null) return;   // 못 찾으면 프리팹의 사각 딤 그대로

        t_dimImage.sprite                  = t_shape.sprite;
        t_dimImage.type                    = t_shape.type;
        t_dimImage.fillCenter              = t_shape.fillCenter;
        t_dimImage.preserveAspect          = t_shape.preserveAspect;
        t_dimImage.pixelsPerUnitMultiplier = t_shape.pixelsPerUnitMultiplier;

        if (t_shape.type != Image.Type.Filled) return;

        t_dimImage.fillMethod    = t_shape.fillMethod;
        t_dimImage.fillAmount    = t_shape.fillAmount;
        t_dimImage.fillClockwise = t_shape.fillClockwise;
        t_dimImage.fillOrigin    = t_shape.fillOrigin;
    }

    // 오버레이가 덮는 면적의 주인이 곧 모양의 주인이다 — 오버레이 부모 → 잠금 대상 순으로 찾는다.
    Image FindShape(RectTransform _parent)
    {
        var t_image = _parent.GetComponent<Image>();
        if (t_image != null && t_image.sprite != null) return t_image;

        var t_target = ResolveTarget();
        return t_target != null ? t_target.image : null;
    }
}
