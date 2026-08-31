using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 매칭 화면 배경 두 판(BG/Top·BG/Bottom)의 안무. 이 화면의 배경은 처음부터 씬 커튼과 같은 물건이었다 —
// pivot (0.5,0) / (0.5,1)에 같은 기울기, 맞닿는 변이 이음매. 그런데 한 번도 움직인 적이 없었다.
//
// 동사는 "켜진다/꺼진다"가 아니라 "맞물린다 / 갈라진다"이다. 로비 위로 두 판이 대각으로 맞물려 닫히는 것이
// 곧 이 화면의 등장이고, 그 대각이 다시 갈라지며 덱 화면이 드러나는 것이 퇴장이다.
//
// 갈라짐이 특히 중요하다 — 판은 alpha 0.94라 덱 화면을 완전히 가린다. 이 축이 없으면 덱의 등장 안무가
// 판 뒤에서 진행되다가, 판이 통째로 사라지는 프레임에 이미 절반쯤 진행된 화면이 튀어나온다.
//
// 기하는 CurtainView.Solve가 이미 순수 함수로 풀어 놨다 — 같은 문제를 두 번 풀지 않는다.
//
// ⚠ 판 참조를 뺀 모든 필드에 C# 이니셜라이저 기본값을 준다(MatchHandoffFx와 같은 규약).
[Serializable]
public class MatchmakingBgFx
{
    [Tooltip("위 판(상대색). 아랫변이 이음매다.\n"
           + "저작해서 쓰는 값: 색·스프라이트 / 기울기(회전 Z) / 이음매 세로 위치(앵커 Y) / 홈 좌표(Pos).\n"
           + "⚠ 크기와 배율은 런타임이 화면에 맞춰 다시 잡는다 — 프리팹의 Width·Height·Scale은 편집 중 미리보기일 뿐 무시된다.\n"
           + "⚠ pivot은 반드시 (0.5, 0) — 회전이 pivot을 중심으로 돌기 때문에, 이음매가 될 변 위에 pivot이 있어야 "
           + "해상도와 무관하게 아래 판의 변과 정확히 겹친다. 이 규약이 깨지면 대각선에 틈이 생긴다.\n"
           + "미배선이면 배경 축이 통째로 빠지고 배경은 지금처럼 정적으로 남는다.")]
    [SerializeField] RectTransform top;

    [Tooltip("아래 판(내색). 윗변이 이음매다. 크기·배율은 위 판과 같이 런타임이 잡는다.\n"
           + "⚠ pivot은 반드시 (0.5, 1). 앵커·회전은 위 판과 같은 값이어야 한다(다르면 진입 시 경고가 뜬다).")]
    [SerializeField] RectTransform bottom;

    [Header("맞물림 — 진입")]
    [Tooltip("두 판이 맞물려 로비를 덮는 시간(초). 커튼의 닫힘(0.22)과 같은 박자로 두면 두 전환이 한 문법으로 읽힌다.")]
    [Min(0.01f)] [SerializeField] float closeDuration = 0.22f;

    [Tooltip("덮는 움직임이라 가속이 맞는다 — 감속이면 판이 스스로 다가와 멈춘 것으로 보인다.")]
    [SerializeField] Ease closeEase = Ease.InCubic;

    [Header("갈라짐 — 퇴장")]
    [Tooltip("두 판이 갈라져 덱 화면을 드러내는 시간(초). 배너가 밀려나는 시간(MatchHandoffFx.partSweep)보다 " +
             "길어야 한다 — 배너가 먼저 나가고 그 길을 따라 판이 열려야 밀려 열린 것으로 읽힌다.")]
    [Min(0.01f)] [SerializeField] float partDuration = 0.32f;

    [SerializeField] Ease partEase = Ease.InCubic;

    [Header("이음매")]
    [Tooltip("맞물리는 순간 번쩍이는 선의 두께(px). 0이면 이음매 축을 통째로 건너뛴다.\n" +
             "두 판은 색이 달라 맞물림 자체는 보이지만, 이 한 줄이 있어야 딱 맞물린 순간이 생긴다.")]
    [Min(0f)] [SerializeField] float seamThickness = 7f;

    [SerializeField] Color seamColor = new Color(1f, 0.97f, 0.88f, 1f);

    [Range(0f, 1f)] [SerializeField] float seamAlpha = 0.85f;

    [Min(0.01f)] [SerializeField] float seamRise = 0.05f;
    [Min(0.01f)] [SerializeField] float seamFall = 0.22f;

    [Header("확정 — 상대가 정해지면 판이 진해진다")]
    [Tooltip("상대가 확정되는 순간 위 판이 옮겨 앉을 색. 덱 화면 EnemySection의 판 색을 그대로 넣는다 —\n" +
             "두 화면은 같은 팔레트로 저작돼 있어서(위쪽은 RGB가 아예 같고 알파만 0.94다) " +
             "여기서 색을 맞춰 두면 판이 갈라질 때 밑에서 나오는 색과 정확히 이어진다.\n" +
             "알파를 1로 올리는 것이 곧 '진해짐'이다 — 로비가 비치던 6%가 여기서 닫힌다.")]
    [SerializeField] Color topConfirmColor = new Color(0.878f, 0.357f, 0.357f, 1f);

    [Tooltip("아래 판이 옮겨 앉을 색. 덱 화면 MySection의 판 색이다 — 매칭의 청록(B 0.682)보다 " +
             "파랑 쪽(B 0.941)이라 확정되는 순간 색이 한 걸음 옮겨 앉는 것이 눈에 보인다.")]
    [SerializeField] Color bottomConfirmColor = new Color(0.290f, 0.639f, 0.941f, 1f);

    [Tooltip("색이 옮겨 앉는 시간(초). 발견의 한 방이 아니라 그 뒤로 천천히 스며야 한다 — " +
             "짧으면 슬램과 겹쳐 색 변화가 묻히고, 조임 구간(chargeHold)을 넘기면 충돌 때까지 안 끝난다.")]
    [Min(0.01f)] [SerializeField] float confirmDuration = 0.55f;

    [Header("기하")]
    [Tooltip("판을 화면 밖까지 넉넉히 밀어내는 여유(px). 기울어진 이음매는 화면 좌우 끝에서 더 내려앉아 " +
             "여유가 없으면 판 귀퉁이가 화면에 남는다(계산은 CurtainView.Solve가 한다).")]
    [Min(0f)] [SerializeField] float pad = 48f;

    // 저작 자리는 한 번만 잡는다 — 이미 밀린 값을 다시 캡처하면 열 때마다 판이 화면 밖으로 걸어 나간다.
    Vector2 m_topHome;
    Vector2 m_bottomHome;
    bool    m_captured;

    // 색 축이 쓰는 것들. 저작 색도 같은 이유의 1회 캡처다 — 확정이 옮겨 놓은 색을 다시 잡으면 되돌릴 자리가 사라진다.
    Image m_topImage;
    Image m_bottomImage;
    Color m_topBaseColor;
    Color m_bottomBaseColor;
    bool  m_imagesCaptured;

    /// <summary>갈라짐이 끝나는 시각. 화면을 내리는 쪽(셸)이 이보다 일찍 내리면 판이 또 증발한다.</summary>
    public float PartDuration => this.HasPanels ? this.partDuration : 0f;

    /// <summary>두 판이 맞물리는 데 걸리는 시간. 판에 무언가를 실어 함께 내리려면 같은 값을 써야 한다.</summary>
    public float CloseDuration => this.closeDuration;

    /// <summary>맞물림의 이징. 실려 오는 쪽이 값을 복제하면 다음 튜닝에 판만 바뀌고 실린 것이 남는다.</summary>
    public Ease CloseEase => this.closeEase;

    /// <summary>한 연출 몫의 이음매 굵기. 매칭과 대치가 같은 두 판을 쓰고 이 값만 갈아끼운다.</summary>
    // readonly로 잠그지 못한다 — Unity는 readonly 필드를 직렬화하지 않아 인스펙터로 저작할 수 없다.
    [Serializable]
    public struct SeamTuning
    {
        [Min(0f)] public float seamThickness;
    }

    /// <summary>지금 저작된 이음매 굵기를 한 묶음으로 꺼낸다. 갈아끼우기 전에 한 번만 잡아야 저작값이 진실원으로 남는다.</summary>
    public SeamTuning CaptureSeam() => new SeamTuning { seamThickness = this.seamThickness };

    /// <summary>이음매 굵기를 갈아끼운다. 이음매 선은 맞물림을 지을 때마다 새로 세워지므로 그 전에 부르면 된다.</summary>
    public void ApplySeam(in SeamTuning _tuning)
    {
        this.seamThickness = Mathf.Max(0f, _tuning.seamThickness);
    }

    /// <summary>
    /// 두 판이 화면을 비우는 거리(위·아래). 판에 실려 함께 떨어질 것이 <b>같은 거리</b>를 쓰게 하려고 연다 —
    /// 이음매 각도와 여유는 이 클래스만 알고 있어서, 실어 오는 쪽이 직접 풀면 기하 진실원이 둘이 된다.
    /// 판이 미배선이면 0이다 — 실을 판이 없으면 실린 것도 움직이지 않아야 한다.
    /// </summary>
    public void SolveTravel(RectTransform _screen, out float _up, out float _down)
    {
        _up = _down = 0f;

        if (!this.HasPanels) return;

        this.Solve(_screen, out _, out _up, out _down);
    }

    /// <summary>
    /// 배너가 들어오고 나갈 방향. 이음매의 법선이라 판이 맞물리는 방향과 같아진다 —
    /// 이 벡터를 쓰면 배너·판·이음매가 한 축으로 정렬된다. 판이 없으면 그냥 위쪽이다.
    /// </summary>
    public Vector2 EnterNormal
    {
        get
        {
            if (!this.HasPanels) return Vector2.up;

            float t_rad = this.SeamAngle * Mathf.Deg2Rad;

            // 이음매 방향이 (cos, sin)이므로 법선은 (-sin, cos). 기울기가 0이면 정확히 Vector2.up이 된다.
            return new Vector2(-Mathf.Sin(t_rad), Mathf.Cos(t_rad));
        }
    }

    bool HasPanels => this.top != null && this.bottom != null;

    // 이음매의 기울기는 프리팹에 저작된 값이 진실원이다 — 코드가 각도를 들지 않는다.
    float SeamAngle => this.top != null ? Mathf.DeltaAngle(0f, this.top.localEulerAngles.z) : 0f;

    /// <summary>두 판이 대각으로 맞물려 로비를 덮는다(재생은 호출자). 이게 이 화면의 등장이다.</summary>
    public Sequence BuildClose(RectTransform _screen)
    {
        var t_seq = DOTween.Sequence();

        if (!this.HasPanels) return t_seq;

        this.Capture();
        this.Solve(_screen, out Vector2 t_size, out float t_up, out float t_down);

        this.top.DOKill();
        this.bottom.DOKill();

        this.ApplyPanelSize(t_size);

        this.top.anchoredPosition    = this.m_topHome    + new Vector2(0f, t_up);
        this.bottom.anchoredPosition = this.m_bottomHome - new Vector2(0f, t_down);

        t_seq.Insert(0f, this.top.DOAnchorPos(this.m_topHome, this.closeDuration).SetEase(this.closeEase));
        t_seq.Insert(0f, this.bottom.DOAnchorPos(this.m_bottomHome, this.closeDuration).SetEase(this.closeEase));

        // 맞물리는 프레임에 터진다 — 닫힘이 끝나는 자리다.
        this.StageSeam(t_seq, this.closeDuration, t_size.x);

        return t_seq;
    }

    /// <summary>두 판이 갈라져 덱 화면을 드러낸다(재생은 호출자). 배너가 밀려나는 그 결에 배경도 실린다.</summary>
    public Sequence BuildPart(RectTransform _screen)
    {
        var t_seq = DOTween.Sequence();

        if (!this.HasPanels) return t_seq;

        this.Capture();
        this.Solve(_screen, out Vector2 t_size, out float t_up, out float t_down);

        this.top.DOKill();
        this.bottom.DOKill();

        this.ApplyPanelSize(t_size);

        // 시작 자리를 못 박지 않는다 — 진입이 끝난 자리에서 그대로 이어받는다.
        t_seq.Insert(0f, this.top.DOAnchorPos(this.m_topHome + new Vector2(0f, t_up),
                                              this.partDuration).SetEase(this.partEase));
        t_seq.Insert(0f, this.bottom.DOAnchorPos(this.m_bottomHome - new Vector2(0f, t_down),
                                                 this.partDuration).SetEase(this.partEase));

        // 갈라지기 시작하는 프레임에 한 번 — 맞물릴 때와 같은 선이 갈라짐의 신호가 된다.
        this.StageSeam(t_seq, 0f, t_size.x);

        return t_seq;
    }

    /// <summary>
    /// 상대가 확정되면 두 판이 덱 화면의 섹션 색으로 옮겨 앉는다(재생은 호출자).
    /// 색을 직접 칠하지 않고 밝기 축의 <b>기준</b>을 미는 이유는, 이 구간에도 조임·충돌의 밝기 왕복이
    /// 계속 돌기 때문이다 — 같은 Graphic에 두 축이 직접 쓰면 늦게 쓴 쪽이 이긴다.
    /// </summary>
    public Sequence BuildConfirm(ScreenDimTint _tint)
    {
        var t_seq = DOTween.Sequence();

        if (_tint == null || !this.HasPanels) return t_seq;

        this.CaptureImages();

        Color t_topFrom = _tint.GetExtraBase(this.m_topImage);
        Color t_botFrom = _tint.GetExtraBase(this.m_bottomImage);

        // 기준이 안 잡혔다(딤이 이 판들을 모른다) — 색 축만 조용히 빠진다.
        if (t_topFrom.a <= 0f && t_botFrom.a <= 0f) return t_seq;

        t_seq.Insert(0f, DOTween.To(() => 0f, _v =>
        {
            _tint.SetExtraBase(this.m_topImage,    Color.Lerp(t_topFrom, this.topConfirmColor,    _v));
            _tint.SetExtraBase(this.m_bottomImage, Color.Lerp(t_botFrom, this.bottomConfirmColor, _v));
        }, 1f, this.confirmDuration).SetEase(Ease.OutQuad));

        return t_seq;
    }

    /// <summary>안무가 옮긴 <b>위치와 색</b>만 저작 자리로 되돌린다. 잘려도 배경이 화면 밖으로 나간 채 굳지 않게.
    /// 크기·배율은 코드 소유라 되돌리지 않는다 — 다음 열기의 BuildClose가 화면에서 다시 푼다.</summary>
    /// <param name="_tint">밝기 축. 확정이 옮겨 놓은 기준 색을 저작값으로 되돌리려면 이쪽을 거쳐야 한다.</param>
    public void Reset(ScreenDimTint _tint)
    {
        if (!this.m_captured || !this.HasPanels) return;

        this.top.DOKill();
        this.bottom.DOKill();
        this.top.anchoredPosition    = this.m_topHome;
        this.bottom.anchoredPosition = this.m_bottomHome;

        // 지난 매칭이 덱 색으로 옮겨 놓은 기준을 되돌린다 — 안 되돌리면 다음 매칭이 덱 색으로 열린다.
        if (_tint == null || !this.m_imagesCaptured) return;

        _tint.SetExtraBase(this.m_topImage,    this.m_topBaseColor);
        _tint.SetExtraBase(this.m_bottomImage, this.m_bottomBaseColor);
    }

    // 판의 Image와 저작 색. 색은 한 번만 잡는다 — 확정이 옮겨 놓은 색을 다시 캡처하면 되돌릴 자리가 사라진다.
    void CaptureImages()
    {
        if (this.m_imagesCaptured || !this.HasPanels) return;

        this.m_topImage    = this.top.GetComponent<Image>();
        this.m_bottomImage = this.bottom.GetComponent<Image>();

        if (this.m_topImage == null || this.m_bottomImage == null) return;

        this.m_imagesCaptured  = true;
        this.m_topBaseColor    = this.m_topImage.color;
        this.m_bottomBaseColor = this.m_bottomImage.color;
    }

    // 판의 크기는 화면에서 푼 값이 진실원이다 — 저작 크기·배율에 기대면 가로가 넓은 기기에서 기울어진 귀퉁이가 화면 안으로 들어온다.
    // 씬 커튼(CurtainView.ApplyGeometry)과 같은 규약이라, 두 커튼이 같은 판 조립을 나눠 쓸 수 있다.
    void ApplyPanelSize(in Vector2 _size)
    {
        this.top.sizeDelta    = _size;
        this.bottom.sizeDelta = _size;

        // 크기를 코드가 정하므로 배율이 1이 아니면 계산한 만큼 덮이지 않는다(이동은 anchoredPosition이라 배율과 무관하다).
        this.top.localScale    = Vector3.one;
        this.bottom.localScale = Vector3.one;
    }

    // 판의 크기와 판이 화면을 완전히 비우는 거리. 진실원을 둘로 만들지 않으려 CurtainView.Solve를 그대로 부른다 —
    // 이음매의 세로 위치·기울기도 씬 커튼과 같이 프리팹 저작값에서 읽는다(두 커튼이 같은 판 조립을 나눠 쓴다).
    void Solve(RectTransform _screen, out Vector2 _size, out float _up, out float _down)
    {
        float t_w = _screen != null ? _screen.rect.width  : 0f;
        float t_h = _screen != null ? _screen.rect.height : 0f;

        // 캔버스가 한 번도 갱신되지 않은 프레임에는 rect가 0이다. 이 화면은 스케일러가 붙은 로비 캔버스 아래라
        // Screen 픽셀을 그대로 쓰면 단위가 섞인다 — 판 크기까지 이 값으로 굳으므로 캔버스 단위로 환산해서 넣는다.
        if (t_w <= 0f || t_h <= 0f)
        {
            float t_scale = CanvasScaleFactor(_screen);

            if (t_w <= 0f) t_w = Screen.width  / t_scale;
            if (t_h <= 0f) t_h = Screen.height / t_scale;
        }

        CurtainView.Solve(t_w, t_h, this.top.anchorMin.y, this.SeamAngle, this.pad, out _size, out _up, out _down);
    }

    // 픽셀 → 캔버스 단위 환산비. 스케일러가 없거나 캔버스를 못 찾으면 1이라 예전 폴백과 같은 값이 된다.
    static float CanvasScaleFactor(RectTransform _screen)
    {
        Canvas t_canvas = _screen != null ? _screen.GetComponentInParent<Canvas>(true) : null;

        return t_canvas != null && t_canvas.scaleFactor > 0f ? t_canvas.scaleFactor : 1f;
    }

    // 이음매 선. 프리팹에 배선할 자리를 만들지 않는다 — 스캔 띠·조임 빛·빛줄기와 같은 자가설치 규약이다.
    void StageSeam(Sequence _seq, float _at, float _span)
    {
        if (this.seamThickness <= 0f || this.top == null || this.top.parent == null) return;

        var t_go = new GameObject("SeamFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rt = (RectTransform)t_go.transform;

        // 두 판과 같은 부모(BG)에 마지막 형제로 붙는다 — 판 위, 배너 아래다.
        t_rt.SetParent(this.top.parent, false);
        t_rt.SetAsLastSibling();

        t_rt.anchorMin = t_rt.anchorMax = new Vector2(0.5f, 0.5f);
        t_rt.pivot     = new Vector2(0.5f, 0.5f);

        // 이음매는 두 판의 pivot이 만나는 자리 — 판의 홈이 곧 그 자리다.
        t_rt.anchoredPosition = this.m_topHome;

        // 판과 같은 길이로 눕는다 — 기울어진 선이라 화면 폭보다 길어야 좌우 끝이 비지 않고, 판 폭이 이미 그 몫이다.
        t_rt.sizeDelta     = new Vector2(_span, this.seamThickness);
        t_rt.localRotation = Quaternion.Euler(0f, 0f, this.SeamAngle);

        var t_image = t_go.GetComponent<Image>();
        t_image.raycastTarget = false;
        t_image.color         = new Color(this.seamColor.r, this.seamColor.g, this.seamColor.b, 0f);

        UiAdditive.Apply(t_go);

        _seq.Insert(_at, t_image.DOFade(this.seamAlpha, this.seamRise).SetEase(Ease.OutQuad));
        _seq.Insert(_at + this.seamRise, t_image.DOFade(0f, this.seamFall).SetEase(Ease.InQuad));

        // 잔해를 남기지 않는다 — 다음 매칭이 알파 0짜리 선을 물려받으면 두 벌이 겹친다.
        _seq.InsertCallback(_at + this.seamRise + this.seamFall,
                            () => { if (t_go != null) UnityEngine.Object.Destroy(t_go); });
    }

    void Capture()
    {
        if (this.m_captured || !this.HasPanels) return;

        this.m_captured   = true;
        this.m_topHome    = this.top.anchoredPosition;
        this.m_bottomHome = this.bottom.anchoredPosition;

        this.WarnOnMisauthoredPanels();
    }

    // 이음매는 "두 판의 pivot이 같은 점에 있고 같은 각으로 돈다"는 것만으로 성립한다.
    // 두 커튼이 같은 판 프리팹을 나눠 쓰게 된 뒤로는 한쪽 인스턴스만 손대도 이 화면에서만 틈이 벌어지므로,
    // 눈으로 찾기 전에 로그로 잡는다(씬 커튼 CurtainView.WarnOnMisauthoredPanels와 같은 규약).
    void WarnOnMisauthoredPanels()
    {
#if UNITY_EDITOR
        if (!Mathf.Approximately(this.top.anchorMin.y, this.bottom.anchorMin.y))
            Debug.LogWarning($"[MatchmakingBgFx] 두 판의 앵커가 다릅니다(위 {this.top.anchorMin.y} ≠ 아래 {this.bottom.anchorMin.y}) — 이음매가 어긋납니다.");

        if (Mathf.Abs(Mathf.DeltaAngle(this.top.localEulerAngles.z, this.bottom.localEulerAngles.z)) > 0.01f)
            Debug.LogWarning($"[MatchmakingBgFx] 두 판의 기울기가 다릅니다(위 {this.top.localEulerAngles.z} ≠ 아래 {this.bottom.localEulerAngles.z}) — 이음매가 어긋납니다.");

        if (!Mathf.Approximately(this.top.pivot.y, 0f) || !Mathf.Approximately(this.bottom.pivot.y, 1f))
            Debug.LogWarning($"[MatchmakingBgFx] pivot 규약 위반(위 {this.top.pivot} 은 y=0, 아래 {this.bottom.pivot} 은 y=1이어야 함) — 이음매가 어긋납니다.");
#endif
    }
}
