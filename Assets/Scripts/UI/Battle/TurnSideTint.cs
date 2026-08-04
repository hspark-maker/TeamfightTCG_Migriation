using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>턴 주인에 따라 UI 그래픽 색을 바꾼다(내 턴=하늘 / 상대 턴=분홍). 표시 전용 —
/// 게임 상태를 읽기만 하고 쓰지 않으므로 결정론·멀티와 무관하다.
///
/// 판정 근거는 <see cref="TurnEvents.TurnStarted"/>가 넘겨주는 그 턴의 행동 주체 필드 하나다.
/// 여기서 "지금 누구 턴인가"를 따로 계산하지 않는다 — 턴 소유의 단일 진실원은 턴 허브 쪽이다.
/// (멀티 P2는 LocalOwnerIndex가 1이라 ownerIndex를 직접 비교하면 색이 뒤집힌다.)</summary>
public class TurnSideTint : MonoBehaviour
{
    [Tooltip("색을 칠할 대상. 비우면 이 오브젝트의 Graphic(Image 등)")]
    [SerializeField] Graphic target;

    [SerializeField] Color myTurnColor    = new Color(0.45f, 0.80f, 1.00f);   // 하늘
    [SerializeField] Color enemyTurnColor = new Color(1.00f, 0.41f, 0.41f);   // 분홍

    [Tooltip("색이 바뀌는 시간(초). 0이면 즉시")]
    [SerializeField] float fadeDuration = 0.25f;

    void Awake()
    {
        if (this.target == null) this.target = GetComponent<Graphic>();
    }

    // 씬 종료 시 TurnEvents.Reset()이 구독을 통째로 비우지만, 오브젝트가 꺼졌다 켜지는 경우까지
    // 감당하려면 여기서 짝을 맞춰야 한다(중복 구독 방지).
    void OnEnable()  => TurnEvents.TurnStarted += HandleTurnStarted;
    void OnDisable() => TurnEvents.TurnStarted -= HandleTurnStarted;

    void OnDestroy()
    {
        if (this.target != null) this.target.DOKill();   // 파괴 대상 접근 방지(트윈 수명 규약)
    }

    void HandleTurnStarted(BattleField _field)
    {
        if (_field == null || this.target == null) return;
        Apply(TurnState.IsLocalTurn(_field.OwnerIndex));
    }

    /// <summary>턴 색 적용. 외부(연출 코드)에서 직접 부를 수도 있게 public.</summary>
    public void Apply(bool _isMyTurn)
    {
        if (this.target == null) return;

        Color t_color = _isMyTurn ? this.myTurnColor : this.enemyTurnColor;
        t_color.a = this.target.color.a;   // 알파는 저작값 유지(디자인이 반투명일 수 있다)

        this.target.DOKill();
        if (this.fadeDuration <= 0f) { this.target.color = t_color; return; }

        this.target.DOColor(t_color, this.fadeDuration).SetLink(gameObject);
    }
}
