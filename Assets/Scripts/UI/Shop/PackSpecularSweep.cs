using UnityEngine;
using UnityEngine.UI;

// 팩 표면을 주기적으로 훑고 지나가는 빛줄기.
//
// 정지한 평면에 "표면이 있다"는 인상을 주는 가장 싼 방법이다 — 빛이 미끄러지는 순간에만
// 재질(금속·코팅)이 읽힌다. 실제 반사가 아니라 마스킹된 그라디언트 슬라이드다.
//
// band는 팩의 RectMask2D 안에 있어야 한다. 밖으로 새면 광채가 아니라 화면을 가로지르는 막대가 된다.
//
// 봉인이 조금이라도 찢기면 멈춘다. 팩 위쪽에 구멍이 생겼는데 그 위를 빛줄기가 지나가면
// 팩 속(카드)에 표면 반사가 얹혀 "통이 뚫렸다"가 깨진다 — 멀쩡한 팩일 때만 도는 표현이다.
// 끄는 게 아니라 멈추는 것이라, 임계에 못 미쳐 되감기면 멀쩡해진 팩에서 다시 돈다.
public class PackSpecularSweep : MonoBehaviour
{
    [Tooltip("훑고 지나가는 빛줄기. 팩(RectMask2D)의 자식이어야 한다.")]
    [SerializeField] RectTransform band;
    [Tooltip("뜯기 진행도를 구독해 찢기 시작과 함께 멈춘다. 미배선이면 항상 동작.")]
    [SerializeField] PackTearHandle tearHandle;

    [Tooltip("스윕 간격(초). 이 주기마다 한 번 지나간다.")]
    [SerializeField] float period = 3.6f;
    [Tooltip("한 번 지나가는 데 걸리는 시간(초).")]
    [SerializeField] float sweepDuration = 0.8f;
    [Tooltip("시작~끝 이동 폭(캔버스 참조px). 팩 폭보다 넉넉해야 양끝이 마스크 밖에서 시작·종료한다.")]
    [SerializeField] float travel = 900f;
    [Tooltip("빛줄기 최대 알파.")]
    [Range(0f, 1f)] [SerializeField] float peakAlpha = 0.45f;

    Graphic m_graphic;
    float m_time;
    bool m_paused;

    void Awake()
    {
        if (band != null) m_graphic = band.GetComponent<Graphic>();
    }

    void OnEnable()
    {
        m_time = 0f;
        m_paused = false;
        SetAlpha(0f);
        if (tearHandle != null) tearHandle.OnProgress += HandleTearProgress;
    }

    void OnDisable()
    {
        if (tearHandle != null) tearHandle.OnProgress -= HandleTearProgress;
        SetAlpha(0f);
    }

    // 진행도가 조금이라도 붙으면 = 봉인이 찢기기 시작했다. 되감겨 0으로 돌아오면 다시 돈다.
    void HandleTearProgress(float _progress)
    {
        m_paused = _progress > 0.001f;
        if (m_paused) SetAlpha(0f);
    }

    void Update()
    {
        if (m_paused || band == null || period <= 0f) return;

        m_time += Time.unscaledDeltaTime;
        float t_phase = m_time % period;

        if (t_phase > sweepDuration) { SetAlpha(0f); return; }

        float t_k = t_phase / Mathf.Max(0.01f, sweepDuration);   // 0~1
        band.anchoredPosition = new Vector2(Mathf.Lerp(-travel * 0.5f, travel * 0.5f, t_k), 0f);

        // 양끝에서 페이드 — 마스크 경계에서 툭 끊기면 광채가 아니라 잘린 사각형으로 보인다.
        SetAlpha(peakAlpha * Mathf.Sin(t_k * Mathf.PI));
    }

    void SetAlpha(float _a)
    {
        if (m_graphic == null) return;

        var t_c = m_graphic.color;
        t_c.a = _a;
        m_graphic.color = t_c;
    }
}
