using DG.Tweening;
using UnityEngine;

public class SwipeGuide : MonoBehaviour
{
    [SerializeField] SpriteRenderer leftSr;
    [SerializeField] SpriteRenderer centerSr;
    [SerializeField] SpriteRenderer rightSr;
    [SerializeField] float dimAlpha = 0.3f;
    [SerializeField] float highlightAlpha = 1.0f;
    [SerializeField] float fadeInDuration = 0.25f;
    [SerializeField] float switchDuration = 0.12f;
    [SerializeField] float dirThreshold = 0.35f;
    [Header("위쪽 배치(드래그-포워드)")]
    // 카드 위에 놓을 때의 로컬 좌표·회전.
    // 자동값 = authoring(아래쪽) 값에서 위치 Y와 회전 Z의 부호만 뒤집은 것. 가이드 루트가 이미 기울어져
    // 있어서(Z 회전) 스케일 반전이나 회전 오프셋으로 미러를 흉내내면 좌/우가 같이 뒤집힌다 —
    // 그래서 "부호 반전"이라는 2D 미러 규칙만 쓰고, 아트가 안 맞으면 아래 값을 직접 넣어 덮어쓴다.
    [SerializeField] bool    useCustomAbovePlacement;
    [SerializeField] Vector3 aboveLocalPos;
    [SerializeField] Vector3 aboveLocalEuler;

    SpriteRenderer[] allSprites;
    SpriteRenderer currentHighlight;
    Sequence hideSeq;
    bool isVisible;
    // 프리팹에 authoring된 아래쪽 배치. 위쪽 배치는 이 값의 Y를 뒤집어 만든다 —
    // 위/아래 좌표를 각각 직렬화하면 한쪽만 옮겼을 때 조용히 어긋난다(배치의 단일 진실원은 프리팹 하나).
    Vector3 baseLocalPos;
    Vector3 baseLocalEuler;
    bool    baseCaptured;
    bool    isAbove;   // 현재 배치. 같은 쪽으로 재요청하면 transform을 건드리지 않는다(매 프레임 호출 대비).

    void Awake()
    {
        EnsureInit();
        EnsureBase();
        // 시작 시 숨김은 알파 0으로 처리. (예전 gameObject.SetActive(false)는 GO가 비활성으로 시작한 경우
        //  첫 Show()의 SetActive(true)가 뒤늦게 Awake를 돌려 다시 꺼버려 → 첫 드래그-백에서 가이드가 안 보였다.)
        foreach (SpriteRenderer t_sr in this.allSprites)
            if (t_sr != null) { Color t_c = t_sr.color; t_c.a = 0f; t_sr.color = t_c; }
    }

    void EnsureInit()
    {
        if (this.allSprites == null)
            this.allSprites = new[] { this.leftSr, this.centerSr, this.rightSr };
    }

    /// <summary>authoring 배치를 1회만 캡처. Awake 전에 SetAbove가 먼저 불려도(에디터 툴/테스트)
    /// 0,0,0을 기준으로 잡아 자리가 무너지지 않게 한다.</summary>
    void EnsureBase()
    {
        if (this.baseCaptured) return;
        this.baseCaptured = true;
        this.baseLocalPos   = transform.localPosition;
        this.baseLocalEuler = transform.localEulerAngles;
    }

    public void SetVisible(bool _visible)
    {
        if (_visible == this.isVisible) return;
        this.isVisible = _visible;
        if (_visible) Show();
        else Hide();
    }

    /// <summary>가이드를 카드 위/아래 중 어디에 둘지. 드래그-포워드(위로 끌기)는 손가락이 가는 쪽인
    /// 카드 위에 떠야 시선과 조작 방향이 맞는다. 드래그-백은 종전대로 아래.
    /// 드래그 중 매 프레임 불려도 되게 같은 쪽이면 아무것도 하지 않는다.</summary>
    public void SetAbove(bool _above)
    {
        EnsureBase();
        if (_above == this.isAbove) return;
        this.isAbove = _above;

        if (!_above)
        {
            transform.localPosition    = this.baseLocalPos;
            transform.localEulerAngles = this.baseLocalEuler;
            return;
        }

        transform.localPosition = this.useCustomAbovePlacement
            ? this.aboveLocalPos
            : new Vector3(this.baseLocalPos.x, -this.baseLocalPos.y, this.baseLocalPos.z);

        transform.localEulerAngles = this.useCustomAbovePlacement
            ? this.aboveLocalEuler
            : new Vector3(this.baseLocalEuler.x, this.baseLocalEuler.y, -this.baseLocalEuler.z);
    }

    public void UpdateDirection(float _aimX)
    {
        if (!this.isVisible) return;

        SpriteRenderer t_next;
        if (_aimX < -this.dirThreshold) t_next = this.rightSr;
        else if (_aimX > this.dirThreshold) t_next = this.leftSr;
        else t_next = this.centerSr;

        if (t_next == this.currentHighlight) return;
        this.currentHighlight = t_next;

        foreach (SpriteRenderer t_sr in this.allSprites)
        {
            t_sr.DOKill();
            t_sr.DOFade(t_sr == t_next ? this.highlightAlpha : this.dimAlpha, this.switchDuration).SetLink(gameObject);
        }
    }

    void Show()
    {
        EnsureInit();
        this.hideSeq?.Kill();
        gameObject.SetActive(true);
        this.currentHighlight = this.centerSr;

        foreach (SpriteRenderer t_sr in this.allSprites)
        {
            t_sr.DOKill();
            Color t_c = t_sr.color;
            t_c.a = 0f;
            t_sr.color = t_c;
            float t_target = t_sr == this.centerSr ? this.highlightAlpha : this.dimAlpha;
            t_sr.DOFade(t_target, this.fadeInDuration).SetLink(gameObject);
        }
    }

    void Hide()
    {
        this.hideSeq?.Kill();
        this.hideSeq = DOTween.Sequence();
        this.hideSeq.SetLink(gameObject);
        foreach (SpriteRenderer t_sr in this.allSprites)
            this.hideSeq.Join(t_sr.DOFade(0f, this.fadeInDuration));
        this.hideSeq.AppendCallback(() => gameObject.SetActive(false));
    }

    void OnDestroy()
    {
        this.hideSeq?.Kill();
        if (this.allSprites != null)
            foreach (SpriteRenderer t_sr in this.allSprites)
                if (t_sr != null) t_sr.DOKill();
    }
}
