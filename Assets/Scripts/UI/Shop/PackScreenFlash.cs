using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 화면을 덮은 어둠(Dim)이 순간 밝아졌다 돌아온다.
//
// 이 축은 신규 카드가 나왔을 때만 쏜다 — 화면 전체가 반응하는 것이 신규뿐이어야
// "이번엔 뭔가 건졌다"가 카드 한 장의 사건을 넘어 화면의 사건이 된다.
// 중복까지 번쩍이면 그 대비가 사라지고 개봉마다 깜빡이는 화면이 될 뿐이다.
// 그래서 발화 판단은 이 컴포넌트가 하지 않는다 — 진행자(PackRevealView)가 신규일 때만 Play를 부른다.
//
// 알파는 건드리지 않고 색만 밝힌다. 알파를 내리면 어둠이 걷혀 뒤에 깔린 것들이 드러나고
// 그 순간 화면 구도가 바뀐다 — 우리가 원하는 것은 구도 변화가 아니라 빛의 반응이다.
public class PackScreenFlash : MonoBehaviour
{
    [Tooltip("밝힐 대상. 화면을 덮고 있는 Dim 이미지다. 미배선이면 이 오브젝트의 Graphic을 쓴다.")]
    [SerializeField] Graphic dim;

    [Tooltip("가장 밝을 때 도달하는 색. RGB만 쓴다 — 알파는 원래 값을 그대로 지킨다.")]
    [SerializeField] Color flashColor = new Color(0.28f, 0.34f, 0.52f, 1f);

    [Tooltip("밝아지는 시간. 짧아야 \"번쩍\"이 된다 — 길면 화면이 밝아지는 전환으로 읽힌다.")]
    [SerializeField] float riseDuration = 0.1f;

    [Tooltip("돌아오는 시간. 올라가는 쪽보다 길어야 빛이 잦아드는 것으로 읽힌다.")]
    [SerializeField] float fallDuration = 0.4f;

    // 지금 밝기(0=평소 어둠, 1=가장 밝음).
    float m_level;

    // 평소 어둠의 색. 밝힘의 기준이자 돌아갈 자리다 — 연출 도중의 색을 기준으로 잡으면
    // 신규가 연달아 나올 때 기준이 점점 밝은 쪽으로 밀린다. 그래서 최초 1회만 캡처한다.
    Color m_baseColor;
    bool m_baseCaptured;

    Graphic m_dim;

    /// <summary>한 번 번쩍인다. 재생 중에 다시 불러도 현재 밝기에서 이어 올라간다.</summary>
    public void Play()
    {
        var t_dim = ResolveDim();
        if (t_dim == null) return;

        CaptureBase(t_dim);
        DOTween.Kill(this);

        // 0으로 되돌리고 시작하지 않는다 — 신규가 연달아 나오면 그 리셋이 화면을 툭 끊는다.
        // getter가 지연 종료 시점에 m_level을 읽으므로, 내려오는 트윈도 그때의 밝기에서 출발한다.
        DOTween.To(() => m_level, SetLevel, 1f, riseDuration)
               .SetEase(Ease.OutQuad)
               .SetTarget(this)
               .SetLink(gameObject);

        DOTween.To(() => m_level, SetLevel, 0f, fallDuration)
               .SetDelay(riseDuration)
               .SetEase(Ease.OutQuad)
               .SetTarget(this)
               .SetLink(gameObject);
    }

    void OnDisable()
    {
        // 밝아진 채로 굳으면 다음 개봉이 밝은 화면에서 시작한다 — 트윈을 걷고 평소 어둠으로 되돌린다.
        DOTween.Kill(this);
        SetLevel(0f);
    }

    void SetLevel(float _level)
    {
        m_level = _level;

        var t_dim = ResolveDim();
        if (t_dim == null || !m_baseCaptured) return;

        var t_c = Color.Lerp(m_baseColor, flashColor, _level);
        t_c.a = m_baseColor.a;   // 어둠의 두께는 그대로 — 밝히는 것은 색뿐이다.
        t_dim.color = t_c;
    }

    void CaptureBase(Graphic _dim)
    {
        if (m_baseCaptured) return;

        m_baseColor = _dim.color;
        m_baseCaptured = true;
    }

    Graphic ResolveDim()
    {
        if (m_dim != null) return m_dim;

        m_dim = dim != null ? dim : GetComponent<Graphic>();
        return m_dim;
    }
}
