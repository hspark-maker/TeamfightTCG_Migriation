using UnityEngine;
using UnityEngine.UI;

// 팩을 "떠 있는 물건"으로 읽히게 하는 아이들 모션. 부유 + 펄스 + 바닥 그림자 연동.
//
// 평면 이미지에 입체감을 만드는 축은 조명이 아니라 운동이다. 특히 바닥 그림자 연동이 핵심 —
// 뜰수록 그림자가 작아지고 옅어져야 "떠 있다"가 성립한다. 그림자가 고정이면 팩은 벽에 붙은 스티커로 읽힌다.
//
// 대상은 packRoot가 아니라 그 자식(visual)이다. packRoot는 PackRevealView의 등장 트윈이 쓰고 있어
// 여기서 같이 만지면 두 트윈이 같은 값을 두고 싸운다. shadow는 바닥에 고정돼야 하므로 visual 바깥에 둔다.
//
// 뜯기 드래그가 시작되면 정지한다 — 손가락이 유일한 기준인 순간에 아이들 오프셋이 끼어들면 조작감이 무너진다.
public class PackIdleMotion : MonoBehaviour
{
    [Header("대상")]
    [Tooltip("부유·펄스가 적용될 노드. packRoot가 아니라 그 자식이어야 한다(등장 트윈과 충돌 방지).")]
    [SerializeField] RectTransform visual;
    [Tooltip("바닥 그림자. visual 바깥(형제)에 두어야 팩만 뜬다. 미배선이면 그림자 연동 없음.")]
    [SerializeField] RectTransform shadow;
    [Tooltip("팩 뒤 방사광. 펄스에 맞춰 밝기가 오르내린다. 미배선이면 광채 연동 없음.")]
    [SerializeField] Graphic glow;

    [Header("부유")]
    [Tooltip("위아래 진폭(캔버스 참조px).")]
    [SerializeField] float floatDistance = 22f;
    [Tooltip("왕복 주기(초). 펄스 주기와 어긋나게 잡아야 패턴이 안 읽힌다.")]
    [SerializeField] float floatPeriod = 2.4f;

    [Header("펄스")]
    [SerializeField] float pulseScale = 1.03f;
    [SerializeField] float pulsePeriod = 2.0f;

    [Header("그림자 연동")]
    [Tooltip("팩이 가장 높이 떴을 때의 그림자 배율.")]
    [SerializeField] float shadowScaleAtTop = 0.82f;
    [Tooltip("팩이 가장 높이 떴을 때의 그림자 알파 배율.")]
    [SerializeField] float shadowAlphaAtTop = 0.55f;

    [Header("광채 연동")]
    [SerializeField] float glowAlphaMin = 0.18f;
    [SerializeField] float glowAlphaMax = 0.34f;

    [Header("드래그 중 복귀")]
    [Tooltip("정지 시 원위치로 걷히는 속도(초당 비율). 클수록 즉각적.")]
    [SerializeField] float settleSpeed = 12f;

    [Header("연결")]
    [Tooltip("뜯기 진행도를 구독해 드래그 중 아이들을 멈춘다. 미배선이면 항상 동작.")]
    [SerializeField] PackTearHandle tearHandle;

    // 원위치·원값(1회 캡처). visual/shadow/glow가 전부 중앙 앵커라 rect 드라이브 타이밍과 무관하다.
    Vector2 m_visualHome;
    Vector3 m_shadowHomeScale;
    float m_shadowHomeAlpha;
    bool m_homeCaptured;

    float m_time;
    bool m_paused;

    void Awake()
    {
        CaptureHome();
    }

    void OnEnable()
    {
        CaptureHome();
        m_time = 0f;
        m_paused = false;
        if (tearHandle != null) tearHandle.OnProgress += HandleProgress;
    }

    void OnDisable()
    {
        if (tearHandle != null) tearHandle.OnProgress -= HandleProgress;
        Restore();
    }

    void Update()
    {
        if (!m_homeCaptured) return;

        if (m_paused) { Settle(); return; }

        // Time.unscaledDeltaTime — 개봉 화면은 일시정지와 무관하게 살아 있어야 한다.
        m_time += Time.unscaledDeltaTime;

        float t_float = Mathf.Sin(m_time * Mathf.PI * 2f / Mathf.Max(0.01f, floatPeriod));   // -1~1
        float t_pulse = Mathf.Sin(m_time * Mathf.PI * 2f / Mathf.Max(0.01f, pulsePeriod));   // -1~1
        float t_up01  = (t_float + 1f) * 0.5f;                                                // 0(바닥)~1(최고)

        if (visual != null)
        {
            visual.anchoredPosition = m_visualHome + new Vector2(0f, t_float * floatDistance);
            visual.localScale = Vector3.one * Mathf.Lerp(1f, pulseScale, (t_pulse + 1f) * 0.5f);
        }

        ApplyShadow(t_up01);
        ApplyGlow((t_pulse + 1f) * 0.5f);
    }

    // 진행도가 조금이라도 붙으면 = 손가락이 팩을 잡고 있다.
    void HandleProgress(float _progress) => m_paused = _progress > 0.001f;

    // 정지 상태: 원위치로 부드럽게 걷어낸다(툭 끊기면 그 자체가 눈에 띈다).
    void Settle()
    {
        float t_k = 1f - Mathf.Exp(-settleSpeed * Time.unscaledDeltaTime);

        if (visual != null)
        {
            visual.anchoredPosition = Vector2.Lerp(visual.anchoredPosition, m_visualHome, t_k);
            visual.localScale = Vector3.Lerp(visual.localScale, Vector3.one, t_k);
        }
        if (shadow != null) shadow.localScale = Vector3.Lerp(shadow.localScale, m_shadowHomeScale, t_k);

        ApplyGlow(0f);
    }

    void ApplyShadow(float _up01)
    {
        if (shadow == null) return;

        shadow.localScale = m_shadowHomeScale * Mathf.Lerp(1f, shadowScaleAtTop, _up01);

        var t_g = shadow.GetComponent<Graphic>();
        if (t_g == null) return;

        var t_c = t_g.color;
        t_c.a = m_shadowHomeAlpha * Mathf.Lerp(1f, shadowAlphaAtTop, _up01);
        t_g.color = t_c;
    }

    void ApplyGlow(float _pulse01)
    {
        if (glow == null) return;

        var t_c = glow.color;
        t_c.a = Mathf.Lerp(glowAlphaMin, glowAlphaMax, _pulse01);
        glow.color = t_c;
    }

    void CaptureHome()
    {
        if (m_homeCaptured) return;

        m_visualHome = visual != null ? visual.anchoredPosition : Vector2.zero;
        m_shadowHomeScale = shadow != null ? shadow.localScale : Vector3.one;

        var t_g = shadow != null ? shadow.GetComponent<Graphic>() : null;
        m_shadowHomeAlpha = t_g != null ? t_g.color.a : 1f;

        m_homeCaptured = true;
    }

    // 비활성 시 원값 복구 — 아이들이 만든 오프셋이 그대로 굳으면 다음 개봉이 어긋난 자리에서 시작한다.
    void Restore()
    {
        if (!m_homeCaptured) return;

        if (visual != null)
        {
            visual.anchoredPosition = m_visualHome;
            visual.localScale = Vector3.one;
        }
        if (shadow != null)
        {
            shadow.localScale = m_shadowHomeScale;
            var t_g = shadow.GetComponent<Graphic>();
            if (t_g != null)
            {
                var t_c = t_g.color;
                t_c.a = m_shadowHomeAlpha;
                t_g.color = t_c;
            }
        }
    }
}
