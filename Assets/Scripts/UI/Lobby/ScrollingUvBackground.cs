using UnityEngine;
using UnityEngine.UI;

/// 배경 패턴을 한 방향으로 계속 흘려보낸다(로비 메인 배경).
///
/// ── 왜 RawImage인가 ──
/// uGUI <see cref="Image"/>로는 UV를 못 민다. UI/Default 셰이더가 `_MainTex_ST`를 쓰지 않아서
/// 머티리얼 오프셋이 먹지 않고, 먹게 하려면 전용 셰이더를 새로 만들어야 한다.
/// <see cref="RawImage"/>는 <c>uvRect</c>가 직접 열려 있어 셰이더·머티리얼 인스턴스 없이 흐른다.
///
/// ── 왜 rect가 아니라 UV를 움직이나 ──
/// 오브젝트를 움직이면 언젠가 끝이 보여서 되감아야 하고, 되감는 순간이 눈에 띈다.
/// UV는 wrapMode=Repeat면 무한히 이어지므로 이음매가 없다.
/// **텍스처 임포트 설정이 Repeat여야 한다** — Clamp면 가장자리 픽셀이 늘어붙어 줄무늬로 보인다.
///
/// 타임스케일 비의존(unscaled): 로비 연출이 배속·일시정지에 끌려갈 이유가 없다.
[RequireComponent(typeof(RawImage))]
public class ScrollingUvBackground : MonoBehaviour
{
    [Tooltip("UV가 1초에 흐르는 양. 둘 다 0이 아니면 사선으로 흐른다.\n" +
             "0.01 = 텍스처 한 장을 지나가는 데 100초. 배경은 '움직이는 게 눈에 띄면' 이미 과하다.")]
    [SerializeField] Vector2 speed = new Vector2(0.010f, 0.006f);

    [Tooltip("화면을 텍스처 몇 장으로 채울지. 1이면 텍스처 원본 비율 그대로 한 장 크기로 깔린다.\n" +
             "키우면 패턴이 작아지고 촘촘해진다.")]
    [Min(0.01f)] [SerializeField] float tiling = 1f;

    [Tooltip("끄면 흐르지 않는다(정지 배경). 켜고 끄는 것만으로 종전 동작으로 돌아간다.")]
    [SerializeField] bool scroll = true;

    RawImage m_image;
    Vector2  m_offset;

    void Awake() => this.m_image = GetComponent<RawImage>();

    void OnEnable()
    {
        // 다시 켜질 때 이어서 흐르게 현재 오프셋을 그대로 쓴다 — 0으로 되돌리면 배경이 튄다.
        ApplyUv();
    }

    void Update()
    {
        if (this.scroll)
        {
            // Repeat로 0~1에 가둔다. 그냥 누적하면 장시간 켜 둔 뒤 float 정밀도가 떨어져
            // 패턴이 계단처럼 끊긴다(로비는 몇 시간씩 떠 있을 수 있다).
            this.m_offset.x = Mathf.Repeat(this.m_offset.x + this.speed.x * Time.unscaledDeltaTime, 1f);
            this.m_offset.y = Mathf.Repeat(this.m_offset.y + this.speed.y * Time.unscaledDeltaTime, 1f);
        }

        ApplyUv();
    }

    /// <summary>uvRect 크기를 **매번 다시 잰다**. 세이프에어리어·해상도에 따라 rect가 달라지는데
    /// 크기를 한 번만 굳히면 기기마다 패턴이 늘어나거나 눌린다. 원본 픽셀 비율을 유지하는 값이다.</summary>
    void ApplyUv()
    {
        if (this.m_image == null || this.m_image.texture == null) return;

        Rect  t_rect = ((RectTransform)transform).rect;
        float t_texW = Mathf.Max(1, this.m_image.texture.width);
        float t_texH = Mathf.Max(1, this.m_image.texture.height);

        var t_size = new Vector2(t_rect.width / t_texW * this.tiling,
                                 t_rect.height / t_texH * this.tiling);

        this.m_image.uvRect = new Rect(this.m_offset.x, this.m_offset.y, t_size.x, t_size.y);
    }

#if UNITY_EDITOR
    // 인스펙터에서 값만 만져도 바로 보이게(플레이 전에 타일 크기를 맞출 수 있다).
    void OnValidate()
    {
        if (this.m_image == null) this.m_image = GetComponent<RawImage>();
        ApplyUv();
    }
#endif
}
