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
    [Tooltip("한 장이 접혀 사라지기까지의 시간. 뒷장은 이미 아래 깔려 있으므로 펴는 구간은 없다.\n" +
             "0이면 넘김 연출을 끄고 즉시 교체한다.")]
    [SerializeField] float duration = 0.32f;

    [Tooltip("주변 UI(페이지 게이지·보상 상자·페이지 번호)가 사라졌다 돌아오는 편도 시간. 0이면 페이드 없이 즉시 바뀐다.")]
    [SerializeField] float crossfade = 0.12f;

    [Tooltip("종이가 완전히 세워졌을 때 접히는 쪽 그늘의 진하기. 0이면 그늘을 만들지 않는다.")]
    [Range(0f, 1f)] [SerializeField] float shadeMax = 0.55f;

    [Tooltip("세워질수록 세로로 눌리는 정도. Overlay 캔버스라 원근이 없어 가로만 줄면 종이가 아니라 '가림막'으로 읽힌다.")]
    [Range(0f, 0.3f)] [SerializeField] float verticalSquash = 0.08f;

    [Tooltip("비닐 광택 띠의 최대 진하기. 0이면 띠를 만들지 않는다(종이 느낌).")]
    [Range(0f, 1f)] [SerializeField] float glossMax = 0.3f;

    [Tooltip("광택 띠 폭(장 폭 대비). 좁을수록 반질반질한 비닐로 읽힌다.")]
    [Range(0.05f, 1f)] [SerializeField] float glossWidth = 0.35f;

    [Tooltip("넘기기 시작 시 뒤쪽 장의 밝기. 진행도에 따라 1까지 올라간다.")]
    [Range(0f, 1f)] [SerializeField] float underStartAlpha = 0.55f;

    [Tooltip("넘기기 시작 시 뒤쪽 장의 크기. 진행도에 따라 원래 크기로 돌아온다.")]
    [Range(0.8f, 1f)] [SerializeField] float underStartScale = 0.97f;

    [Header("말림(Roll)")]
    [Tooltip("켜면 페이지를 RenderTexture로 떠서 실제로 말리는 연출을 쓴다(PageRollGraphic).\n" +
             "끄면 예전 접힘(Y회전 = 가로 압축)으로 돌아간다. 뜨는 데 실패한 프레임도 접힘으로 물러난다.")]
    [SerializeField] bool useRoll = true;

    [Tooltip("말린 통의 굵기(장 폭 대비 반지름). 작을수록 여러 바퀴 도르르 말리고, 클수록 굵은 통이 한 번 크게 휜다.\n" +
             "감기는 총 각도 = 1/이 값 라디안 — 0.16이면 약 한 바퀴, 0.32면 반 바퀴, 1이면 57도만 휜다.")]
    [Range(0.04f, 1f)] [SerializeField] float rollRadiusRatio = 0.13f;

    [Tooltip("말린 구간을 몇 조각으로 나눌지. 낮으면 통이 각져 보이고, 높이면 정점만 늘어난다.")]
    [Range(8, 64)] [SerializeField] int rollSegments = 28;

    [Tooltip("말린 통이 앞으로 나오며 세로로 부푸는 정도. Overlay 캔버스라 원근이 없어 이 값이 유일한 입체 단서다.")]
    [Range(0f, 0.3f)] [SerializeField] float rollBulge = 0.06f;

    [Tooltip("말린 안쪽(종이 뒷면)의 밝기. 앞면 그림이 그대로 비치므로 충분히 어두워야 뒷면으로 읽힌다.")]
    [Range(0f, 1f)] [SerializeField] float rollBackShade = 0.32f;

    [Tooltip("페이지를 뜰 때만 쓰는 전용 카메라 레이어. 화면에 보이는 레이어(UI=5)와 겹치면 다른 것까지 찍힌다.")]
    [SerializeField] int captureLayer = 7;

    RectTransform m_page;        // Grid_Slots — Panel_Page와 같은 사각형이면서 레이아웃에 안 물린 유일한 노드
    RectTransform m_sideRoot;
    TMP_Text      m_label;
    CanvasGroup   m_sideGroup;
    Image         m_shade;
    Image         m_gloss;
    RectTransform m_underRoot;
    CanvasGroup   m_underGroup;

    UiRectCapture   m_capture;
    PageRollGraphic m_roll;

    Vector2 m_baseAnchored;
    Vector3 m_baseScale = Vector3.one;
    Vector3 m_underBaseScale = Vector3.one;
    float   m_underBaseAlpha = 1f;
    int     m_dir = 1;
    bool    m_active;
    bool    m_rolling;

    public float Duration => this.duration;

    /// <summary>주변 UI가 걷혔다 돌아오는 편도 시간. 교체 뒤 글자를 되돌릴 때 호출부가 같은 값을 쓴다.</summary>
    public float Crossfade => Mathf.Max(0.01f, this.crossfade);

    public void Bind(
        RectTransform _page,
        RectTransform _sideRoot,
        TMP_Text _label,
        CanvasGroup _underGroup = null,
        RectTransform _underRoot = null)
    {
        m_page     = _page;
        m_sideRoot = _sideRoot;
        m_label    = _label;
        m_underGroup = _underGroup;
        m_underRoot  = _underRoot != null
            ? _underRoot
            : _underGroup != null ? _underGroup.transform as RectTransform : null;

        if (m_underGroup != null) m_underBaseAlpha = m_underGroup.alpha;
        if (m_underRoot != null)  m_underBaseScale = m_underRoot.localScale;

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
            m_baseScale    = m_page.localScale;
            m_active       = true;
        }

        m_dir = _dir >= 0 ? 1 : -1;

        // 말림은 페이지를 통째로 떠서 다시 그린다 — 그늘·광택 판은 접힘 전용이라 여기서 만들지 않는다
        if (this.useRoll && this.TryBeginRoll())
        {
            this.SetFlipProgress(0f);
            return;
        }

        this.EnsureShade();
        if (m_shade != null)
        {
            m_shade.gameObject.SetActive(true);
            var t_scale = m_shade.rectTransform.localScale;
            m_shade.rectTransform.localScale = new Vector3(m_dir > 0 ? 1f : -1f, t_scale.y, t_scale.z);
            m_shade.rectTransform.SetAsLastSibling();
        }

        this.EnsureGloss();
        if (m_gloss != null)
        {
            m_gloss.gameObject.SetActive(true);
            m_gloss.rectTransform.SetAsLastSibling();   // 그늘 위에 얹혀야 광택으로 읽힌다
        }

        this.SetFlipProgress(0f);
    }

    public void SetFlipProgress(float _p)
    {
        if (m_page == null) return;

        float t_p = Mathf.Clamp01(_p);

        // 말림은 진행도 0.5(교체 지점)에서 이미 다 말려 있어야 한다 — 뒤 절반은 새 장의 몫이다
        if (m_rolling && m_roll != null) m_roll.SetRoll(Mathf.Clamp01(t_p / 0.5f), m_dir);
        else                             this.ApplyFold(t_p);

        this.ApplyChrome(t_p);
    }

    void ApplyFold(float _p)
    {
        float t_p    = Mathf.Clamp01(_p);
        float t_fold = 1f - Mathf.Abs(t_p * 2f - 1f);               // 0 → 1 → 0
        float t_deg  = 90f * t_fold * (t_p < 0.5f ? 1f : -1f);      // 0→90 세우고, -90→0 편다
        float t_cos  = Mathf.Cos(t_deg * Mathf.Deg2Rad);

        // 축은 **세로 모서리**(책등)다. 기운 축으로 돌리면 원근 없는 Overlay에서는 종이가 휘는 게 아니라
        // 판이 삐뚤어진 채 납작해지는 것으로만 보인다 — 말림은 그늘·광택이 암시한다.
        var t_rotation = Quaternion.Euler(0f, t_deg, 0f);

        // 다음 장(_dir > 0)은 왼쪽 책등을 축으로 자유단(오른쪽)이 넘어간다. 이전 장은 좌우 대칭.
        // 고정점의 높이는 건드리지 않는다(y = 0) — 아래로 내리면 축은 세로인데 위쪽만 크게 휘둘린다.
        float t_hingeX = ((m_dir > 0 ? 0f : 1f) - m_page.pivot.x) * m_page.rect.width;
        var   t_hinge  = new Vector3(t_hingeX, 0f, 0f);

        // 가짜 원근. 세워질수록 세로로 살짝 눌러야 "누워 가는 판"으로 읽힌다.
        // 코너 보정에도 같은 배율을 넣어야 압축 때문에 고정점이 위아래로 미끄러지지 않는다.
        float t_squash = 1f - this.verticalSquash * (1f - t_cos);
        var t_foldScale = new Vector3(m_baseScale.x, m_baseScale.y * t_squash, m_baseScale.z);
        Vector3 t_baseHinge = Vector3.Scale(t_hinge, m_baseScale);
        Vector3 t_foldHinge = Vector3.Scale(t_hinge, t_foldScale);
        Vector3 t_hingeCorrection = t_baseHinge - t_rotation * t_foldHinge;

        m_page.localRotation    = t_rotation;
        m_page.anchoredPosition = m_baseAnchored + new Vector2(t_hingeCorrection.x, t_hingeCorrection.y);
        m_page.localScale       = t_foldScale;

        if (m_shade != null)
        {
            m_shade.rectTransform.localRotation = Quaternion.identity;
            var t_color = m_shade.color;
            t_color.a     = this.shadeMax * (1f - t_cos);   // 정면 0 — 평상시 그림이 안 바뀐다
            m_shade.color = t_color;
        }

        // 광택 띠는 접히는 내내 표면을 훑고 지나간다. 세기는 그늘과 같은 (1-cos) 축이라
        // 정면에서는 0 — 평상시 그림에 띠가 얹히지 않는다.
        if (m_gloss != null)
        {
            var t_rect  = m_gloss.rectTransform;
            float t_width = m_page.rect.width;

            // 폭은 매번 다시 잡는다 — 생성 시점엔 레이아웃(GridRatioFitter) 전이라 rect가 0일 수 있고,
            // 그때 굳으면 띠가 영영 안 보인다.
            t_rect.sizeDelta     = new Vector2(t_width * this.glossWidth, 0f);
            t_rect.localRotation = Quaternion.identity;

            // 자유단에서 책등 쪽으로 흘러간다 — 빛이 넘어가는 가장자리를 따라 훑어야 말리는 것으로 읽힌다.
            float t_travel = t_width * 0.5f;
            t_rect.anchoredPosition = new Vector2(Mathf.Lerp(-t_travel, t_travel, t_p) * -m_dir, 0f);

            var t_color = m_gloss.color;
            t_color.a     = this.glossMax * (1f - t_cos);
            m_gloss.color = t_color;
        }
    }

    // 종이 말고 주변(게이지·페이지 번호)과 뒤에 깔린 장. 접힘·말림 어느 쪽이든 같은 규칙이다.
    void ApplyChrome(float _p)
    {
        float t_p = Mathf.Clamp01(_p);

        // 알파도 p의 함수로 둔다. 시퀀스로 따로 몰면 넘김이 잘렸을 때 알파만 남는다
        float t_span  = this.duration > 0f ? this.crossfade / this.duration : 0f;
        float t_alpha = t_span > 0f ? Mathf.Clamp01(Mathf.Abs(t_p - 0.5f) / t_span) : 1f;

        if (m_sideGroup != null) m_sideGroup.alpha = t_alpha;
        if (m_label != null)     m_label.alpha     = t_alpha;

        // 실제 블러 대신 알파와 미세 축소를 복원해 추가 머티리얼 없이 초점이 맞는 느낌을 낸다.
        float t_underFocus = Mathf.Clamp01(t_p * 2f);
        if (m_underGroup != null)
            m_underGroup.alpha = Mathf.Lerp(this.underStartAlpha, 1f, t_underFocus);
        if (m_underRoot != null)
            m_underRoot.localScale = m_underBaseScale * Mathf.Lerp(this.underStartScale, 1f, t_underFocus);
    }

    /// <summary>주변 UI(게이지·페이지 번호)의 알파만 따로 민다. 종이가 접혀 사라진 뒤 새 장이 그 자리를 이을 때,
    /// 자세는 이미 평평해도 글자는 아직 걷혀 있어야 해서 이 축만 따로 필요하다.</summary>
    public void SetSideAlpha(float _alpha)
    {
        float t_a = Mathf.Clamp01(_alpha);

        if (m_sideGroup != null) m_sideGroup.alpha = t_a;
        if (m_label != null)     m_label.alpha     = t_a;
    }

    /// <summary>뒤에 깔린 장을 지금 넘기고 있는 표면 바로 아래에 끼운다.
    /// 말림 중에는 표면이 페이지 자신이 아니라 그것을 떠 온 판(Page_Roll)이므로 순서의 기준이 다르다 —
    /// 호출부가 그 사정을 알 필요 없게 여기서 하나로 정한다.</summary>
    public void OrderUnderBelowPage(Transform _under)
    {
        if (_under == null) return;

        Transform t_surface = m_rolling && m_roll != null ? m_roll.rectTransform : (Transform)m_page;
        if (t_surface == null || t_surface.parent != _under.parent) return;

        int t_order = t_surface.GetSiblingIndex();
        _under.SetSiblingIndex(t_order);
        t_surface.SetSiblingIndex(t_order + 1);
    }

    /// <summary>RefreshPage가 슬롯을 새로 Instantiate하면 그늘·광택이 카드 뒤로 묻힌다 — 교체 직후 한 번 부른다.</summary>
    public void EnsureShadeOnTop()
    {
        if (m_shade != null) m_shade.rectTransform.SetAsLastSibling();
        if (m_gloss != null) m_gloss.rectTransform.SetAsLastSibling();
    }

    /// <summary>자세·그늘·주변 알파를 넘김 전과 픽셀 단위로 같은 상태로 되돌린다.</summary>
    public void Cancel()
    {
        if (!m_active) return;
        m_active = false;

        // 촬영대에서 페이지를 먼저 돌려놓는다 — 이게 늦으면 원래 자리로 못 돌아온 판 위에 자세를 다시 계산한다
        this.EndRoll();

        this.SetFlipProgress(0f);

        // p=0이면 항등이지만 값으로 못 박는다 — 축 계산이 바뀌어도 잔재가 남지 않게(원복은 알려진 정상값으로).
        if (m_page != null)
        {
            m_page.localRotation    = Quaternion.identity;
            m_page.localScale       = m_baseScale;
            m_page.anchoredPosition = m_baseAnchored;
        }
        if (m_underGroup != null) m_underGroup.alpha = m_underBaseAlpha;
        if (m_underRoot != null)  m_underRoot.localScale = m_underBaseScale;
        if (m_shade != null) m_shade.gameObject.SetActive(false);
        if (m_gloss != null) m_gloss.gameObject.SetActive(false);
    }

    /// <summary>소유 뷰가 파괴될 때 캡처용 런타임 오브젝트까지 함께 정리한다.</summary>
    public void Dispose()
    {
        this.EndRoll();
        if (m_capture != null)
        {
            m_capture.Dispose();
            m_capture = null;
        }
    }

    // 페이지를 촬영대로 넘기고, 그 자리에 같은 크기의 말림 판을 세운다.
    // 뜨는 데 실패하면(레이아웃 전이라 rect가 0인 프레임 등) false — 호출부는 접힘으로 물러난다.
    bool TryBeginRoll()
    {
        if (m_rolling) return true;
        if (m_page == null || m_page.parent == null) return false;
        if (m_page.rect.width < 1f || m_page.rect.height < 1f) return false;

        this.EnsureRoll();
        if (m_roll == null) return false;

        // 자세는 페이지가 촬영대로 떠나기 **전에** 베낀다 — 떠난 뒤엔 촬영대 기준 값이라 자리가 어긋난다
        var t_rect = m_roll.rectTransform;
        t_rect.anchorMin        = m_page.anchorMin;
        t_rect.anchorMax        = m_page.anchorMax;
        t_rect.pivot            = m_page.pivot;
        t_rect.sizeDelta        = m_page.sizeDelta;
        t_rect.anchoredPosition = m_page.anchoredPosition;
        t_rect.localScale       = m_page.localScale;
        t_rect.localRotation    = Quaternion.identity;
        t_rect.SetSiblingIndex(m_page.GetSiblingIndex());   // 페이지가 있던 층을 그대로 물려받는다

        if (m_capture == null) m_capture = new UiRectCapture();
        if (!m_capture.Begin(m_page, this.captureLayer))
        {
            m_roll.gameObject.SetActive(false);
            return false;
        }

        m_roll.SetTexture(m_capture.Texture);
        m_roll.Configure(this.rollRadiusRatio, this.rollSegments, this.rollBulge, this.rollBackShade, this.glossMax);
        m_roll.SetRoll(0f, m_dir);
        m_roll.gameObject.SetActive(true);

        m_rolling = true;
        return true;
    }

    void EndRoll()
    {
        if (!m_rolling) return;
        m_rolling = false;

        if (m_roll != null)
        {
            m_roll.SetTexture(null);   // 텍스처를 놓고 나서 지워야 이미 없어진 RenderTexture를 그리지 않는다
            m_roll.gameObject.SetActive(false);
        }
        if (m_capture != null) m_capture.End();
    }

    void EnsureRoll()
    {
        if (m_roll != null || m_page == null || m_page.parent == null) return;

        // CanvasRenderer를 손으로 적는다 — 이게 없으면 Graphic이 리빌드를 통째로 건너뛰어 판이 안 그려진다
        var t_go = new GameObject("Page_Roll",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement), typeof(PageRollGraphic));
        var t_rect = (RectTransform)t_go.transform;
        t_rect.SetParent(m_page.parent, false);

        t_go.GetComponent<LayoutElement>().ignoreLayout = true;   // 그늘·광택과 같은 이유(레이아웃 칸으로 세지 않게)

        m_roll = t_go.GetComponent<PageRollGraphic>();
        m_roll.raycastTarget = false;
        t_go.SetActive(false);
    }

    // 광택 띠. 그늘과 같은 규약으로 만든다(레이아웃 무시·raycast 끔·평상시 비활성).
    // 띠 그림은 코인 플립·시너지 상징과 같은 공용 sin² 밴드다 — 반짝임의 형태를 화면마다 갈라두지 않는다.
    void EnsureGloss()
    {
        if (m_gloss != null || m_page == null || this.glossMax <= 0f) return;

        var t_go = new GameObject("Page_Gloss", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        var t_rect = (RectTransform)t_go.transform;
        t_rect.SetParent(m_page, false);

        // 세로는 장 전체를 덮고 가로만 띠 폭 — 접히는 축(가로)을 훑어야 광택으로 읽힌다.
        t_rect.anchorMin = new Vector2(0.5f, 0f);
        t_rect.anchorMax = new Vector2(0.5f, 1f);
        t_rect.pivot     = new Vector2(0.5f, 0.5f);
        t_rect.sizeDelta = new Vector2(m_page.rect.width * this.glossWidth, 0f);

        t_go.GetComponent<LayoutElement>().ignoreLayout = true;   // GridLayoutGroup이 칸으로 세지 않게(그늘과 같은 이유)

        m_gloss = t_go.GetComponent<Image>();
        m_gloss.sprite        = ShineBandSprite.Get();
        m_gloss.color         = new Color(1f, 1f, 1f, 0f);
        m_gloss.raycastTarget = false;
        t_go.SetActive(false);
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
