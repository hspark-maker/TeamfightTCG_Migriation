using UnityEngine;
using UnityEngine.UI;

// 정지한 그림 한 장을 "흐르는 것"으로 바꾸는 상시 표류. 구름·안개처럼 배경 취급인 것에만 붙인다.
//
// 화면이 정적으로 보이는 원인은 대개 주역이 안 움직여서가 아니라 배경이 통째로 얼어 있어서다.
// 그래서 진폭은 작고 주기는 아주 길다 — 무엇이 움직였는지 못 짚겠는데 화면은 살아 있는 상태를 노린다.
// 눈에 띄는 순간 그건 배경이 아니라 사건이 되므로, 값을 올릴 땐 주기부터 늘려라.
//
// 두 축의 주기를 서로 나누어떨어지지 않게 잡아야 대각선 왕복으로 안 읽힌다.
public class UiIdleDrift : MonoBehaviour
{
    [Tooltip("표류할 대상. 비우면 자기 자신.\n" +
             "레이아웃 그룹이 자리를 잡아 주는 대상엔 붙이지 마라 — 매 프레임 anchoredPosition을 덮는다.")]
    [SerializeField] RectTransform target;

    [Header("좌우")]
    [Tooltip("좌우 진폭(px). 대상이 화면보다 넓어야 한다 — 딱 맞는 그림을 밀면 반대편에 빈자리가 드러난다.")]
    [SerializeField] float driftX = 26f;

    [Min(0.1f)]
    [Tooltip("좌우 한 번 왕복하는 시간(초).")]
    [SerializeField] float periodX = 13f;

    [Header("상하")]
    [Tooltip("상하 진폭(px). 좌우보다 작아야 구름이 '떠내려간다'로 읽힌다.")]
    [SerializeField] float driftY = 9f;

    [Min(0.1f)]
    [Tooltip("상하 한 번 왕복하는 시간(초). 좌우와 어긋나게 잡는다.")]
    [SerializeField] float periodY = 9f;

    [Header("밝기 호흡")]
    [Tooltip("옅어졌다 짙어질 그림. 비우면 밝기는 안 건드린다.")]
    [SerializeField] Graphic fade;

    [Range(0f, 1f)]
    [Tooltip("저작 알파에서 빠지는 최대 몫. 0.12면 알파 1인 안개가 0.88까지 옅어졌다 돌아온다.")]
    [SerializeField] float fadeAmount = 0.12f;

    [Min(0.1f)]
    [SerializeField] float fadePeriod = 11f;

    [Range(0f, 1f)]
    [Tooltip("시작 위상. 안개 두 장이 같은 박으로 흔들리면 화면이 통째로 흔들리는 것처럼 보인다 — 장마다 다르게 준다.")]
    [SerializeField] float phase;

    // 저작값(1회 캡처). 표류가 만든 오프셋이 굳으면 다음에 열 때 안개가 어긋난 자리에서 시작한다.
    Vector2 m_home;
    float m_alpha0;
    bool m_captured;

    RectTransform Target => this.target != null ? this.target : (RectTransform)this.transform;

    void Awake() => this.Capture();

    void OnEnable()
    {
        this.Capture();
        this.Apply(this.phase * Mathf.Max(this.periodX, this.periodY));
    }

    void OnDisable() => this.Restore();

    void Update()
    {
        // unscaledTime — 지도는 일시정지와 무관하게 흐른다.
        this.Apply(Time.unscaledTime + this.phase * Mathf.Max(this.periodX, this.periodY));
    }

    void Apply(float _time)
    {
        if (!this.m_captured) return;

        float t_x = Mathf.Sin(_time * Mathf.PI * 2f / this.periodX);
        float t_y = Mathf.Sin(_time * Mathf.PI * 2f / this.periodY);

        this.Target.anchoredPosition = this.m_home + new Vector2(t_x * this.driftX, t_y * this.driftY);

        if (this.fade == null || this.fadeAmount <= 0f) return;

        float t_f = (Mathf.Sin(_time * Mathf.PI * 2f / this.fadePeriod) + 1f) * 0.5f;   // 0~1

        Color t_color = this.fade.color;
        t_color.a = this.m_alpha0 * Mathf.Lerp(1f - this.fadeAmount, 1f, t_f);
        this.fade.color = t_color;
    }

    void Capture()
    {
        if (this.m_captured) return;

        this.m_home = this.Target.anchoredPosition;
        this.m_alpha0 = this.fade != null ? this.fade.color.a : 1f;
        this.m_captured = true;
    }

    void Restore()
    {
        if (!this.m_captured) return;

        this.Target.anchoredPosition = this.m_home;

        if (this.fade == null) return;

        Color t_color = this.fade.color;
        t_color.a = this.m_alpha0;
        this.fade.color = t_color;
    }
}
