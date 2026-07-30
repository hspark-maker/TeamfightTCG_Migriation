using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 신규 카드 뒤에서 터져 나와 천천히 도는 방사형 광선.
//
// ⚠ 배선 전제: 카드보다 **먼저 그려지는 sibling**(카드 프리팹의 첫 자식)이어야 한다.
//   카드 위에 얹으면 광선이 아트를 덮어 주인공이 카드에서 이펙트로 바뀐다. 뒤에 두면 카드가 빛 앞에 선
//   실루엣이 되고, 화려해진 만큼 카드가 더 도드라진다 — 이 연출이 성립하는 지점이 정확히 여기다.
//
// 두 얼굴이 있다:
//   PlayBurst()     — 카드가 드러나는 순간. 안에서 밖으로 터져 나온 뒤 지속 세기로 내려앉는다.
//   ShowSustained() — 결과 격자. 터짐 없이 은은한 세기로 서서 회전만 계속한다.
// 회전은 둘의 공통이고 멈추지 않는다 — "아직도 돌고 있는 것"이 결과 화면에서 신규를 가리키는 표식이다.
//
// 중복 카드에서는 Hide()로 완전히 내린다. 신규만 이 광선을 갖는 것이 이 축의 전부다 —
// 중복에도 조금 주면 대비가 사라지고 "그냥 개봉 이펙트"가 된다.
//
// 알파와 배율은 이 컴포넌트가 쥔다. 스프라이트 색의 알파·노드의 배율은 무시되므로,
// 인스펙터에서 색조와 크기(sizeDelta)만 잡아 두면 된다.
public class PackCardAura : MonoBehaviour
{
    [Header("대상")]
    [Tooltip("방사형 광선 스프라이트. 미배선이면 이 오브젝트의 Graphic을 쓴다.")]
    [SerializeField] Graphic rays;

    [Header("회전")]
    [Tooltip("한 바퀴 도는 데 걸리는 시간(초). 빠르면 회전이 눈에 잡혀 \"돌아가는 UI\"가 된다 " +
             "— 눈치채기 직전이 적정선이다. 음수면 반대 방향으로 돈다.")]
    [SerializeField] float spinPeriod = 18f;

    [Header("세기")]
    [Tooltip("가장 밝을 때(터짐 정점)의 알파.")]
    [Range(0f, 1f)] [SerializeField] float peakAlpha = 1f;
    [Tooltip("가장 밝을 때의 배율. 노드에 잡아 둔 크기 기준이다.")]
    [SerializeField] float peakScale = 1.12f;
    [Tooltip("터짐이 잦아든 뒤 카드가 서 있는 동안 유지하는 세기(0=완전히 내려간 상태, 1=정점). " +
             "0이면 터지고 사라진다 — 그러면 결과 격자까지 이어지는 \"계속 돌고 있다\"가 끊긴다.")]
    [Range(0f, 1f)] [SerializeField] float sustainLevel = 0.5f;
    [Tooltip("결과 격자에서 유지하는 세기. 낱장 확인 때보다 낮춘다 — 여러 장이 동시에 도는 화면이라 " +
             "같은 세기면 격자 전체가 번져 어느 카드가 신규인지 되레 안 읽힌다.")]
    [Range(0f, 1f)] [SerializeField] float resultLevel = 0.3f;

    [Header("터짐")]
    [Tooltip("터지기 직전의 배율. 작게 시작해야 \"안에서 밖으로 터져 나왔다\"가 된다.")]
    [SerializeField] float burstFromScale = 0.45f;
    [Tooltip("정점까지 부풀는 시간. 짧을수록 타격이 된다.")]
    [SerializeField] float burstRise = 0.22f;
    [Tooltip("정점에서 지속 세기로 내려앉는 시간. 올라가는 쪽보다 길어야 빛이 잦아드는 것으로 읽힌다.")]
    [SerializeField] float burstSettle = 0.7f;

    // 지금 세기(0=내려간 상태, 1=정점). 알파와 배율을 이 값 하나에서 파생시킨다 —
    // 둘을 따로 트윈하면 시간·이즈가 어긋나 "커짐"과 "밝아짐"이 두 애니메이션으로 갈라 읽힌다.
    float m_level;

    // 회전을 굴리는 중인지. 내려간 상태에서도 Update가 계속 돌지 않게 하는 스위치다.
    bool m_spinning;

    Graphic m_rays;

    /// <summary>카드가 드러나는 순간: 작게 웅크렸다 터져 나온 뒤 지속 세기로 내려앉는다.</summary>
    public void PlayBurst()
    {
        var t_rays = ResolveRays();
        if (t_rays == null) return;

        KillLevel();
        Wake(t_rays);

        SetLevel(0f);

        // 두 트윈을 이어 붙인다(Sequence로 묶지 않는다) — 시퀀스에 들어간 트윈은 DOTween의 active 목록에서
        // 빠져 DOKill·개별 Kill이 닿지 않으므로, 카드가 밀려 사라질 때 걷어낼 수 없다.
        // 두 번째 트윈은 지연이 끝나는 순간 m_level을 읽어 출발한다(getter라 값 캡처가 늦다) — 이어짐이 성립한다.
        DOTween.To(() => m_level, SetLevel, 1f, burstRise)
               .SetEase(Ease.OutQuint)
               .SetTarget(this)
               .SetLink(gameObject);

        DOTween.To(() => m_level, SetLevel, sustainLevel, burstSettle)
               .SetDelay(burstRise)
               .SetEase(Ease.OutSine)
               .SetTarget(this)
               .SetLink(gameObject);
    }

    /// <summary>결과 격자: 터짐 없이 은은한 세기로 세우고 회전만 계속한다.</summary>
    public void ShowSustained()
    {
        var t_rays = ResolveRays();
        if (t_rays == null) return;

        KillLevel();
        Wake(t_rays);

        SetLevel(resultLevel);
    }

    /// <summary>완전히 내린다(중복 카드·재바인드). 회전도 멈추고 노드 자체를 꺼 부담을 남기지 않는다.</summary>
    public void Hide()
    {
        var t_rays = ResolveRays();
        if (t_rays == null) return;

        KillLevel();
        m_spinning = false;

        SetLevel(0f);
        t_rays.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!m_spinning) return;

        var t_rays = ResolveRays();
        if (t_rays == null || Mathf.Abs(spinPeriod) < 0.01f) return;

        // Time.unscaledDeltaTime — 개봉 화면은 일시정지와 무관하게 살아 있어야 한다(PackIdleMotion과 같은 규약).
        t_rays.transform.Rotate(0f, 0f, -360f / spinPeriod * Time.unscaledDeltaTime);
    }

    void OnDisable()
    {
        // 연출 도중 비활성되면 트윈만 남아 다음 표시의 세기를 뒤늦게 덮어쓴다.
        KillLevel();
        m_spinning = false;
    }

    // ── 내부 ────────────────────────────────────────────────────

    // 세기 하나에서 알파와 배율이 함께 나온다. 배율은 "터지기 직전 → 정점" 구간을 그대로 쓰고,
    // 지속 세기에서는 그 사이 어딘가에 선다 — 지속 상태가 정점보다 작아야 터짐이 정점으로 읽힌다.
    void SetLevel(float _level)
    {
        m_level = _level;

        var t_rays = ResolveRays();
        if (t_rays == null) return;

        var t_c = t_rays.color;
        t_c.a = peakAlpha * _level;
        t_rays.color = t_c;

        t_rays.transform.localScale = Vector3.one * Mathf.Lerp(burstFromScale, peakScale, _level);
    }

    // 켜고 회전을 되살린다. 회전 각도는 되돌리지 않는다 — 방사형은 어느 각도에서 봐도 같은 그림이라
    // 되돌릴 이유가 없고, 되돌리면 되레 켜지는 순간 각도가 튄다.
    void Wake(Graphic _rays)
    {
        _rays.gameObject.SetActive(true);
        m_spinning = true;
    }

    // 세기 트윈만 걷는다. 타깃이 이 컴포넌트라 회전(트랜스폼)이나 카드 쪽 트윈은 건드리지 않는다.
    void KillLevel() => DOTween.Kill(this);

    Graphic ResolveRays()
    {
        if (m_rays != null) return m_rays;

        m_rays = rays != null ? rays : GetComponent<Graphic>();
        return m_rays;
    }
}
