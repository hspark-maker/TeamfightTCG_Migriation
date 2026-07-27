using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>턴 전환 연출. 월드 오브젝트(TurnInfo)에 붙는다.
/// 스케일이 커졌다 작아지며 한 바퀴 회전하고, 배경 스프라이트 색이
/// 내 턴=파랑 / 상대 턴=빨강으로 바뀐다.</summary>
public class TurnBannerUI : MonoBehaviour
{
    [Header("연출 대상")]
    [SerializeField] Transform      target;          // 스케일·회전 대상(보통 self)
    [SerializeField] SpriteRenderer background;      // 색을 바꿀 배경
    [SerializeField] TMP_Text       whosTurnLabel;   // "누구 턴" 라벨(선택)

    [Header("배경 스프라이트")]
    [SerializeField] Sprite playerSprite; // 내 턴 스프라이트
    [SerializeField] Sprite enemySprite;  // 상대 턴 스프라이트

    [Header("라벨 문구")]
    [SerializeField] string playerText = "내 턴";
    [SerializeField] string enemyText  = "상대 턴";

    [Header("타이밍")]
    [SerializeField] float startScale   = 0.6f;   // 시작 스케일 배율(작게)
    [SerializeField] float peakScale    = 1.25f;  // 커질 때 최대 배율
    [SerializeField] float growDuration = 0.35f;  // 커지며 회전
    [SerializeField] float settleDuration = 0.25f;// 원래 크기로 안착

    Vector3 baseScale;
    bool cached;

    void Awake()
    {
        if (this.target == null) this.target = this.transform;
        this.baseScale = this.target.localScale;
        this.cached = true;
    }

    public async UniTask Play(bool _isMyTurn)
    {
        TurnState.InputAllowed = false;

        if (!this.cached) { this.baseScale = this.target.localScale; this.cached = true; }

        this.target.DOKill();

        // 배경 스프라이트 스왑(내 턴 / 상대 턴)
        if (this.background != null)
        {
            Sprite t_sprite = _isMyTurn ? this.playerSprite : this.enemySprite;
            if (t_sprite != null) this.background.sprite = t_sprite;
        }

        // 라벨 문구
        if (this.whosTurnLabel != null)
            this.whosTurnLabel.text = _isMyTurn ? this.playerText : this.enemyText;

        // 스케일 팝 + 한 바퀴 회전
        this.target.localScale       = this.baseScale * this.startScale;
        this.target.localEulerAngles = Vector3.zero;

        Sequence t_seq = DOTween.Sequence();
        t_seq.Append(this.target.DOScale(this.baseScale * this.peakScale, this.growDuration).SetEase(Ease.OutQuad));
        t_seq.Join(this.target.DOLocalRotate(new Vector3(360f, 0f, 0f), this.growDuration + this.settleDuration,
                                             RotateMode.FastBeyond360).SetEase(Ease.OutCubic));
        t_seq.Append(this.target.DOScale(this.baseScale, this.settleDuration).SetEase(Ease.OutBack));

        await t_seq.ToUniTask();

        this.target.localEulerAngles = Vector3.zero;
    }
}
