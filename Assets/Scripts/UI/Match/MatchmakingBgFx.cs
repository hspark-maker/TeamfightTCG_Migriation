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
    [Tooltip("위 판(상대색). 아랫변이 이음매다 — pivot이 (0.5, 0)이어야 한다.\n" +
             "미배선이면 배경 축이 통째로 빠지고 배경은 지금처럼 정적으로 남는다.")]
    [SerializeField] RectTransform top;

    [Tooltip("아래 판(내색). 윗변이 이음매다 — pivot이 (0.5, 1)이어야 한다.")]
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

    [Header("기하")]
    [Tooltip("판을 화면 밖까지 넉넉히 밀어내는 여유(px). 기울어진 이음매는 화면 좌우 끝에서 더 내려앉아 " +
             "여유가 없으면 판 귀퉁이가 화면에 남는다(계산은 CurtainView.Solve가 한다).")]
    [Min(0f)] [SerializeField] float pad = 48f;

    // 저작 자리는 한 번만 잡는다 — 이미 밀린 값을 다시 캡처하면 열 때마다 판이 화면 밖으로 걸어 나간다.
    Vector2 m_topHome;
    Vector2 m_bottomHome;
    bool    m_captured;

    /// <summary>갈라짐이 끝나는 시각. 화면을 내리는 쪽(셸)이 이보다 일찍 내리면 판이 또 증발한다.</summary>
    public float PartDuration => this.HasPanels ? this.partDuration : 0f;

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
        this.Solve(_screen, out float t_up, out float t_down);

        this.top.DOKill();
        this.bottom.DOKill();

        this.top.anchoredPosition    = this.m_topHome    + new Vector2(0f, t_up);
        this.bottom.anchoredPosition = this.m_bottomHome - new Vector2(0f, t_down);

        t_seq.Insert(0f, this.top.DOAnchorPos(this.m_topHome, this.closeDuration).SetEase(this.closeEase));
        t_seq.Insert(0f, this.bottom.DOAnchorPos(this.m_bottomHome, this.closeDuration).SetEase(this.closeEase));

        // 맞물리는 프레임에 터진다 — 닫힘이 끝나는 자리다.
        this.StageSeam(t_seq, this.closeDuration, _screen);

        return t_seq;
    }

    /// <summary>두 판이 갈라져 덱 화면을 드러낸다(재생은 호출자). 배너가 밀려나는 그 결에 배경도 실린다.</summary>
    public Sequence BuildPart(RectTransform _screen)
    {
        var t_seq = DOTween.Sequence();

        if (!this.HasPanels) return t_seq;

        this.Capture();
        this.Solve(_screen, out float t_up, out float t_down);

        this.top.DOKill();
        this.bottom.DOKill();

        // 시작 자리를 못 박지 않는다 — 진입이 끝난 자리에서 그대로 이어받는다.
        t_seq.Insert(0f, this.top.DOAnchorPos(this.m_topHome + new Vector2(0f, t_up),
                                              this.partDuration).SetEase(this.partEase));
        t_seq.Insert(0f, this.bottom.DOAnchorPos(this.m_bottomHome - new Vector2(0f, t_down),
                                                 this.partDuration).SetEase(this.partEase));

        // 갈라지기 시작하는 프레임에 한 번 — 맞물릴 때와 같은 선이 갈라짐의 신호가 된다.
        this.StageSeam(t_seq, 0f, _screen);

        return t_seq;
    }

    /// <summary>안무가 세운 중간값을 저작 자리로 되돌린다. 잘려도 배경이 화면 밖으로 나간 채 굳지 않게.</summary>
    public void Reset()
    {
        if (!this.m_captured || !this.HasPanels) return;

        this.top.DOKill();
        this.bottom.DOKill();
        this.top.anchoredPosition    = this.m_topHome;
        this.bottom.anchoredPosition = this.m_bottomHome;
    }

    // 판이 화면을 완전히 비우는 거리. 이음매가 화면 중앙(0.5)에 있는 기하라 커튼과 같은 식이 그대로 성립한다 —
    // 진실원을 둘로 만들지 않으려 CurtainView.Solve를 그대로 부른다(판 크기는 저작값이라 쓰지 않는다).
    void Solve(RectTransform _screen, out float _up, out float _down)
    {
        float t_w = _screen != null ? _screen.rect.width  : 0f;
        float t_h = _screen != null ? _screen.rect.height : 0f;

        // 캔버스가 한 번도 갱신되지 않은 프레임에는 rect가 0이다(CurtainView와 같은 폴백).
        if (t_w <= 0f) t_w = Screen.width;
        if (t_h <= 0f) t_h = Screen.height;

        CurtainView.Solve(t_w, t_h, 0.5f, this.SeamAngle, this.pad, out _, out _up, out _down);
    }

    // 이음매 선. 프리팹에 배선할 자리를 만들지 않는다 — 스캔 띠·조임 빛·빛줄기와 같은 자가설치 규약이다.
    void StageSeam(Sequence _seq, float _at, RectTransform _screen)
    {
        if (this.seamThickness <= 0f || this.top == null || this.top.parent == null) return;

        float t_w = _screen != null && _screen.rect.width > 0f ? _screen.rect.width : Screen.width;

        var t_go = new GameObject("SeamFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rt = (RectTransform)t_go.transform;

        // 두 판과 같은 부모(BG)에 마지막 형제로 붙는다 — 판 위, 배너 아래다.
        t_rt.SetParent(this.top.parent, false);
        t_rt.SetAsLastSibling();

        t_rt.anchorMin = t_rt.anchorMax = new Vector2(0.5f, 0.5f);
        t_rt.pivot     = new Vector2(0.5f, 0.5f);

        // 이음매는 두 판의 pivot이 만나는 자리 — 판의 홈이 곧 그 자리다.
        t_rt.anchoredPosition = this.m_topHome;

        // 기울어진 선이라 화면 폭보다 길어야 좌우 끝이 비지 않는다.
        t_rt.sizeDelta     = new Vector2(t_w * 1.6f, this.seamThickness);
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
    }
}
