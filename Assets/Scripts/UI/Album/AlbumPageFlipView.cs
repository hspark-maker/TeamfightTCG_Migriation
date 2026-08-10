using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 책 넘김 자세의 단일 진실원. 진행도 0~1 하나가 회전·축 보정·그늘·주변 알파를 전부 정한다
// (AlbumSleeveView.SetProgress와 같은 규약 — 바깥에서 개별 속성을 따로 트윈하면 서로 어긋난다).
//
// ⚠ 캔버스가 Screen Space - Overlay라 원근이 없다. Y회전은 "가로 압축"으로만 보인다(CoinFlipUI와 같은 전제).
//   그래서 0→180으로 밀면 cos이 음수가 되어 내용이 좌우 반전된다 — 반드시 0→90 / -90→0으로 접는다.
[System.Serializable]
public class AlbumPageFlipView
{
    [Tooltip("한 장을 넘기는 전체 시간. 앞 절반은 세우고 뒤 절반은 편다. 0이면 넘김 연출을 끄고 즉시 교체한다.")]
    [SerializeField] float duration = 0.32f;

    [Tooltip("주변 UI(페이지 게이지·보상 상자·페이지 번호)가 사라졌다 돌아오는 편도 시간. 0이면 페이드 없이 즉시 바뀐다.")]
    [SerializeField] float crossfade = 0.12f;

    [Tooltip("종이가 완전히 세워졌을 때 접히는 쪽 그늘의 진하기. 0이면 그늘을 만들지 않는다.")]
    [Range(0f, 1f)] [SerializeField] float shadeMax = 0.55f;

    RectTransform m_page;        // Grid_Slots — Panel_Page와 같은 사각형이면서 레이아웃에 안 물린 유일한 노드
    RectTransform m_sideRoot;
    TMP_Text      m_label;
    CanvasGroup   m_sideGroup;
    Image         m_shade;

    Vector2 m_baseAnchored;
    int     m_dir = 1;
    bool    m_active;

    public float Duration => this.duration;

    public void Bind(RectTransform _page, RectTransform _sideRoot, TMP_Text _label)
    {
        m_page     = _page;
        m_sideRoot = _sideRoot;
        m_label    = _label;

        // 프리팹에 CanvasGroup을 저작하지 않는다 — PopupTransition.ResolveGroup과 같은 관용구
        if (m_sideRoot != null)
        {
            m_sideGroup = m_sideRoot.GetComponent<CanvasGroup>();
            if (m_sideGroup == null) m_sideGroup = m_sideRoot.gameObject.AddComponent<CanvasGroup>();
        }
    }

    /// <summary>넘김 시작 — 기준 자세를 캡처하고 그늘을 방향에 맞춰 세운다. _dir &gt; 0이면 왼쪽 책등(다음 장).</summary>
    public void Begin(int _dir)
    {
        if (m_page == null) return;

        // 이미 돌고 있는 중이면 기준을 다시 잡지 않는다 — 어긋난 자세가 새 기준이 되면 원복이 안 된다
        if (!m_active)
        {
            m_baseAnchored = m_page.anchoredPosition;
            m_active       = true;
        }

        m_dir = _dir >= 0 ? 1 : -1;

        this.EnsureShade();
        if (m_shade != null)
        {
            m_shade.gameObject.SetActive(true);
            var t_scale = m_shade.rectTransform.localScale;
            m_shade.rectTransform.localScale = new Vector3(m_dir > 0 ? 1f : -1f, t_scale.y, t_scale.z);
            m_shade.rectTransform.SetAsLastSibling();
        }

        this.SetFlipProgress(0f);
    }

    public void SetFlipProgress(float _p)
    {
        if (m_page == null) return;

        float t_p    = Mathf.Clamp01(_p);
        float t_fold = 1f - Mathf.Abs(t_p * 2f - 1f);               // 0 → 1 → 0
        float t_deg  = 90f * t_fold * (t_p < 0.5f ? 1f : -1f);      // 0→90 세우고, -90→0 편다
        float t_cos  = Mathf.Cos(t_deg * Mathf.Deg2Rad);

        // 회전이 책등을 hinge·cosθ로 밀어낸 만큼 되뺀다 — pivot을 옮기지 않는 게 원복이 싸다
        // (AlbumSleeveView.ApplyPose와 같은 기법)
        float t_hinge = (m_dir > 0 ? -0.5f : 0.5f) * m_page.rect.width;

        m_page.localRotation    = Quaternion.Euler(0f, t_deg, 0f);
        m_page.anchoredPosition = m_baseAnchored + new Vector2(t_hinge * (1f - t_cos), 0f);

        if (m_shade != null)
        {
            var t_color = m_shade.color;
            t_color.a     = this.shadeMax * (1f - t_cos);   // 정면 0 — 평상시 그림이 안 바뀐다
            m_shade.color = t_color;
        }

        // 알파도 p의 함수로 둔다. 시퀀스로 따로 몰면 넘김이 잘렸을 때 알파만 남는다
        float t_span  = this.duration > 0f ? this.crossfade / this.duration : 0f;
        float t_alpha = t_span > 0f ? Mathf.Clamp01(Mathf.Abs(t_p - 0.5f) / t_span) : 1f;

        if (m_sideGroup != null) m_sideGroup.alpha = t_alpha;
        if (m_label != null)     m_label.alpha     = t_alpha;
    }

    /// <summary>RefreshPage가 슬롯을 새로 Instantiate하면 그늘이 카드 뒤로 묻힌다 — 교체 직후 한 번 부른다.</summary>
    public void EnsureShadeOnTop()
    {
        if (m_shade != null) m_shade.rectTransform.SetAsLastSibling();
    }

    /// <summary>자세·그늘·주변 알파를 넘김 전과 픽셀 단위로 같은 상태로 되돌린다.</summary>
    public void Cancel()
    {
        if (!m_active) return;
        m_active = false;

        this.SetFlipProgress(0f);
        if (m_shade != null) m_shade.gameObject.SetActive(false);
    }

    void EnsureShade()
    {
        if (m_shade != null || m_page == null || this.shadeMax <= 0f) return;

        var t_go = new GameObject("Page_Shade", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        var t_rect = (RectTransform)t_go.transform;
        t_rect.SetParent(m_page, false);
        t_rect.anchorMin = Vector2.zero;
        t_rect.anchorMax = Vector2.one;
        t_rect.offsetMin = Vector2.zero;
        t_rect.offsetMax = Vector2.zero;

        // 이게 없으면 GridLayoutGroup이 그늘을 10번째 칸으로 세어 3x3 배치가 무너진다
        t_go.GetComponent<LayoutElement>().ignoreLayout = true;

        m_shade = t_go.GetComponent<Image>();
        m_shade.sprite        = EdgeShadeSprite.Get();
        m_shade.color         = new Color(0f, 0f, 0f, 0f);
        m_shade.raycastTarget = false;
        t_go.SetActive(false);
    }
}
