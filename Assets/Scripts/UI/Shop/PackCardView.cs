using Coffee.UIEffects;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// 개봉으로 뽑힌 카드 한 장의 표시.
// 카드 비주얼 자체(아트/이름/HP/키워드/시너지)는 도감 타일과 동일하게 CardVisualView에 위임한다 —
// 뽑은 카드가 도감에서 보던 그 카드와 다르게 생기면 안 된다.
// 이 클래스가 따로 존재하는 이유는 강조 축이 다르기 때문 — 개봉 카드는 항상 소유라 잠금 표현이 없고,
// 대신 이 화면에만 있는 축을 얹는다: 한 장이 드러날 때마다 오는 타격(펀치·플래시)과, 신규/중복 구분(NEW 리본·환급 숫자).
// 순수 표시 뷰다. 더미 배치·스와이프 이동은 PackCardStack이 이 오브젝트의 RectTransform을 직접 다룬다.
//
// 강조는 두 순간으로 갈린다. 섞으면 결과 화면이 개봉 연출의 잔상처럼 보이거나, 반대로 낱장 확인이
// 결과판처럼 밋밋해진다:
//   PlayRevealAccent() — 지금 이 한 장이 드러난 순간. 펀치·플래시(전 카드) + 신규 전용 광선·림라이트.
//   ApplyResultContrast() — 결과 격자에 놓인 상태. 신규는 광채가 계속 돌고 중복은 탈채도된 채 놓인다.
// 화면 전체가 반응하는 축(Dim 번쩍임)은 여기 없다 — 그것은 카드 한 장의 것이 아니라 화면의 것이라
// 진행자(PackRevealView)가 신규일 때만 쏜다.
public class PackCardView : MonoBehaviour
{
    [Header("카드")]
    [SerializeField] CardVisualView cardVisual;   // 카드 비주얼 단일 진실원

    // 카드가 드러날 때마다 오는 타격. 신규든 중복이든 "한 장 확인했다"의 감각은 같아야 하므로 전 카드 공통이다.
    [Header("등장 임팩트")]
    [Tooltip("드러나는 순간 즉시 이만큼 커진 뒤 제자리로 꽂힌다(배율 증가분). 서서히 커지면 타격이 아니라 숨쉬기가 된다.")]
    [SerializeField] float punchOvershoot = 0.2f;
    [Tooltip("가로로 더 벌리고 세로로 그만큼 눌러 준다(배율). 균일 확대는 \"커졌다\"로, 어긋난 확대는 \"맞았다\"로 읽힌다.")]
    [SerializeField] float punchStretch = 0.06f;
    [Tooltip("제자리로 회수하는 시간. 짧을수록 단단하다.")]
    [SerializeField] float punchDuration = 0.22f;

    // 아래 넷은 원래 신규 전용 광채(glow*)였다. 필드 이름이 곧 직렬화 키라, 개명하면서 [FormerlySerializedAs]로
    // 프리팹 배선을 승계시킨다 — 그러지 않으면 참조가 조용히 None이 되고 섬광만 사라진다.
    [Tooltip("드러나는 순간의 섬광. 향후 에셋 교체 지점 — 이 참조만 갈아끼우면 된다.")]
    [FormerlySerializedAs("glow")] [SerializeField] Graphic revealFlash;
    [FormerlySerializedAs("glowPeakAlpha")] [SerializeField] float flashPeakAlpha = 1f;
    [FormerlySerializedAs("glowRiseDuration")] [SerializeField] float flashRiseDuration = 0.18f;
    [FormerlySerializedAs("glowFallDuration")] [SerializeField] float flashFallDuration = 0.45f;

    // 카드 표면을 훑는 광택. NEW 워드마크의 gleam과 같은 기법(Transition-Shiny의 진행도를 코드가 민다)을
    // 카드 본체로 옮긴 것이다 — 이 씬 캔버스가 Overlay라 파티클이 렌더되지 않으므로(PackRevealView 주석 참고)
    // 표면 연출을 얹을 수 있는 수단은 UI 셰이더뿐이다.
    //
    // ⚠ 배선 전제: 이 UIEffect는 Frame(카드 테두리)에 붙고, Portrait와 프레임 장식들은 UIEffectReplica로
    //   같은 효과를 복제하되 useTargetTransform을 켜 Frame의 rect를 공유한다 — 그래야 띠가 조각나지 않고
    //   카드를 한 번에 가로지른다. 이름·HP 패널에는 걸지 않는다: 그쪽은 아트 위에 얹힌 정보 레이어라
    //   표면 광택이 지나가면 글자가 번져 읽히기만 나빠진다.
    [Header("카드 광택")]
    [Tooltip("카드 표면을 훑는 광택(UIEffect Transition-Shiny). 미배선이면 광택 없이 펀치·플래시만 남는다.")]
    [SerializeField] UIEffect cardGleam;
    [Tooltip("펀치가 꽂힌 뒤 광택이 지나가기까지의 뜸. 동시에 터지면 타격과 광택이 서로를 뭉갠다.")]
    [SerializeField] float cardGleamDelay = 0.06f;
    [SerializeField] float cardGleamDuration = 0.55f;
    [Tooltip("신규 카드의 광택 색. 중복보다 따뜻하게 — NEW 리본 말고 카드 표면 자체가 신규를 말하게 하는 축이다.")]
    [SerializeField] Color cardGleamNewColor = new Color(0.72f, 0.60f, 0.34f, 1f);
    [Tooltip("중복 카드의 광택 색. UIEffect 프리셋 기본값(무채색) — 카드를 죽이지 않으면서 신규와만 갈린다.")]
    [SerializeField] Color cardGleamDupeColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [Tooltip("신규는 광택이 이만큼 더 훑는다(0이면 중복과 같은 1회). 색만으로는 갈림이 약해 횟수로도 벌린다.")]
    [Min(0)] [SerializeField] int cardGleamNewExtraSweeps = 1;

    // 신규 카드 테두리를 한 바퀴 훑는 림라이트. UIEffect의 Edge-Shiny가 그린다 —
    // 카드 실루엣의 alpha 경계를 따라 빛나는 띠라, 스프라이트를 하나 더 얹지 않고 테두리 그대로를 훑는다.
    //
    // ⚠ 이 축은 cardGleam과 **같은 컴포넌트**를 쓴다(UIEffect는 DisallowMultipleComponent라 Frame에 하나뿐이다).
    //   광택은 transitionRate, 림라이트는 edgeShinyRate — 프로퍼티가 갈려 서로를 덮지 않는다.
    //   대신 트윈 타깃이 같으므로 걷어내는 지점을 두 곳으로 못 박았다: PlayCardGleam과 ResetAccent.
    //   PlayRim은 절대 Kill하지 않는다 — 바로 앞에서 시작한 광택 트윈까지 함께 끊긴다.
    //
    // ⚠ edge는 transition과 반대로 rate 0에서도 계속 보인다(띠가 항상 테두리 어딘가에 있다).
    //   내릴 때는 rate를 0으로 두는 것이 아니라 edgeMode를 None으로 꺼야 한다.
    //   그리고 띠는 서로 반대편에서 도는 한 쌍이다(셰이더가 각도를 반주기로 감는다) — rate 0→1이 정확히 한 바퀴다.
    [Header("신규 림라이트")]
    [Tooltip("림라이트 색. Additive로 얹히므로 밝은 색일수록 강하다.")]
    [SerializeField] Color rimColor = new Color(1f, 0.86f, 0.55f, 1f);
    [Tooltip("테두리에서 빛나는 띠의 두께. 카드 실루엣 안쪽으로 이만큼 번진다.")]
    [Range(0f, 1f)] [SerializeField] float rimThickness = 0.35f;
    [Tooltip("한 번에 빛나는 호의 길이(둘레 대비). 크면 테두리 절반이 통째로 빛나 \"훑는다\"가 아니라 \"켜졌다\"가 된다.")]
    [Range(0.02f, 0.5f)] [SerializeField] float rimArc = 0.12f;
    [Tooltip("펀치가 꽂힌 뒤 림라이트가 출발하기까지의 뜸.")]
    [SerializeField] float rimSweepDelay = 0.02f;
    [Tooltip("테두리를 한 바퀴 훑는 시간.")]
    [SerializeField] float rimSweepDuration = 0.6f;
    [Tooltip("결과 격자에서 신규 카드의 림라이트가 계속 도는 속도(회/초). 셰이더가 스스로 돌려 코드 트윈이 없다. 0이면 결과판에서는 멈춘다.")]
    [SerializeField] float rimResultSpeed = 0.12f;

    [Header("신규 후광")]
    [Tooltip("카드 뒤에서 터져 나와 천천히 도는 방사형 광선(신규 전용). " +
             "카드보다 먼저 그려지는 sibling이어야 한다 — 앞에 두면 아트가 가려진다. 미배선이면 광선 없음.")]
    [SerializeField] PackCardAura newAura;

    [Header("결과 격자 대비")]
    [Tooltip("결과 격자에서 중복 카드를 이만큼 탈채도한다(0=그대로, 1=완전 흑백). " +
             "낱장 확인 순간에는 걸지 않는다 — 그때는 중복도 온전한 획득이어야 한다.")]
    [Range(0f, 1f)] [SerializeField] float dupeResultDesaturation = 0.6f;

    [Header("신규 강조")]
    [Tooltip("NEW 워드마크(신규일 때만). 카드 안이 아니라 윗변에 걸쳐 앉는다 — 카드 밖으로 삐져나온 글자가 시선을 먼저 잡는다.")]
    [SerializeField] GameObject newBadge;
    [Tooltip("워드마크를 훑는 광택. UIEffect의 Transition-Shiny 진행도를 이 코드가 밀어 준다. 미배선이면 등장만 하고 광택은 없다.")]
    [SerializeField] UIEffect newGleam;
    [Tooltip("워드마크가 자리를 잡은 뒤 광택이 지나가기까지의 뜸. 동시에 터지면 둘 다 뭉개진다.")]
    [SerializeField] float newGleamDelay = 0.08f;
    [SerializeField] float newGleamDuration = 0.45f;
    [Tooltip("워드마크가 즉시 이만큼 커진 뒤 제자리로 꽂힌다(카드 펀치와 같은 규약). " +
             "카드의 자식이라 카드 펀치가 곱해진다 — 카드 쪽보다 작게 잡는다.")]
    [SerializeField] float newBadgeOvershoot = 0.18f;
    [SerializeField] float newBadgeDuration = 0.2f;

    [Header("중복 환급")]
    [SerializeField] GameObject refundBadge;  // "+10" 묶음(중복이고 환급이 있을 때만)
    [SerializeField] TMP_Text refundText;
    [SerializeField] float refundRiseDistance = 40f;
    [SerializeField] float refundDuration = 0.6f;

    public bool IsNew { get; private set; }
    public long Refund { get; private set; }

    // 카드 한 장 통째로 페이드하는 손잡이(밀어내기가 쓴다). 프리팹에 없으면 인스턴스에 붙여준다 —
    // 이 값이 알파를 쥐는 단일 지점이라, 프리팹에 컴포넌트가 있든 없든 페이드는 항상 성립한다.
    CanvasGroup m_group;
    public CanvasGroup Group
    {
        get
        {
            if (m_group == null) m_group = GetComponent<CanvasGroup>();
            if (m_group == null) m_group = gameObject.AddComponent<CanvasGroup>();
            return m_group;
        }
    }

    // 강조가 이미 재생됐는지. 스킵과 정상 진행이 겹쳐도 두 번 터지지 않게 한다.
    bool m_accented;

    // refundBadge의 원위치. 떠오른 뒤 되돌릴 기준이라 최초 1회만 캡처한다.
    Vector3 m_refundHome;
    bool m_refundHomeCaptured;

    // 펀치를 걸기 전의 배율. 도중에 끊겼을 때 돌아갈 자리다(SnapPunchToRest).
    Vector3 m_punchRestScale = Vector3.one;

    // NEW 워드마크의 제자리 배율. 프리팹에서 기울이거나 키워 뒀을 수 있어 1로 단정하지 않는다(최초 1회 캡처).
    Vector3 m_newBadgeRestScale = Vector3.one;
    bool m_newBadgeRestCaptured;

    /// <summary>카드 데이터·신규여부·환급액을 태운다. 강조는 아직 재생하지 않는다(완전히 드러난 뒤가 발화 시점).</summary>
    public void Bind(DrawnCard _drawn)
    {
        IsNew = _drawn.IsNew;
        Refund = _drawn.Refund;
        m_accented = false;

        // 개봉 카드는 항상 소유(위 헤더 주석) → _owned는 true 고정.
        if (cardVisual != null) cardVisual.Bind(_drawn.Card, true);

        ResetAccent();
    }

    /// <summary>
    /// 카드가 완전히 드러난 순간의 강조. 펀치·플래시는 전 카드 공통이고, 그 위에 신규는 NEW 리본,
    /// 중복은 환급 숫자가 얹힌다.
    /// _instant면 트윈 없이 최종 상태만 — 스킵으로 건너뛴 카드도 결과 표시는 남는다.
    /// </summary>
    public void PlayRevealAccent(bool _instant = false)
    {
        if (m_accented) return;
        m_accented = true;

        // 타격은 "지금 이 한 장을 확인했다"는 순간의 표현이라 즉시 모드엔 없다 —
        // 결과 격자(PackResultGrid)가 이 경로로 카드를 세우는데, 거기서 펀치가 걸리면 격자 배율과 스케일을 두고 다툰다.
        if (!_instant)
        {
            PlayPunch();
            PlayFlash();
        }

        // 광택은 즉시 모드에도 건다 — 다만 그쪽은 "다 지나간 상태"로 세우는 것이라 아무것도 보이지 않는다.
        // 펀치처럼 빼버리면 안 되는 이유는 반대다: 빼면 rate가 0에 남아 띠가 카드 앞에 걸린 채로 굳는다.
        PlayCardGleam(_instant);

        // 카드 뒤 광선과 테두리 림라이트는 "지금 이 한 장이 나왔다"는 순간의 것이라 즉시 모드엔 없다.
        // 결과 격자의 지속 대비는 ApplyResultContrast가 따로 쥔다 — 한 메서드가 두 순간을 겸하지 않게 갈랐다.
        if (IsNew && !_instant)
        {
            if (newAura != null) newAura.PlayBurst();
            PlayRim();
        }

        if (IsNew) PlayNewBadge(_instant);
        else PlayRefundAccent(_instant);
    }

    /// <summary>
    /// 결과 격자에 놓인 상태의 신규/중복 대비. 여기서 주는 것은 순간이 아니라 지속 상태다 —
    /// 신규는 광채가 계속 돌고 중복은 탈채도된 채 놓여, 마지막 화면이 "이번에 뭘 건졌나"를 한눈에 말한다.
    ///
    /// 격자의 팝(PackResultGrid.PlayPop)은 전 카드 동일하게 둔다. 정렬과 리듬이 어긋나면 격자가
    /// 결과판이 아니라 또 한 번의 연출로 읽힌다 — 대비는 움직임이 아니라 이 상태 차이로 준다.
    /// </summary>
    public void ApplyResultContrast()
    {
        if (IsNew)
        {
            if (newAura != null) newAura.ShowSustained();

            // 림라이트는 셰이더가 스스로 돌린다(autoPlaySpeed) — 카드가 여러 장이라 장당 트윈을 굴리지 않는다.
            SetRim(true);
            if (cardGleam != null) cardGleam.edgeShinyAutoPlaySpeed = rimResultSpeed;
            return;
        }

        if (newAura != null) newAura.Hide();
        SetRim(false);

        // 탈채도는 Frame의 UIEffect 한 곳에 걸면 Replica가 붙은 아트·프레임 장식까지 함께 빠진다
        // (이름·HP 패널은 Replica가 없어 색이 남는다 — 정보 레이어라 그대로 읽히는 편이 낫다).
        if (cardGleam != null)
        {
            cardGleam.toneFilter = ToneFilter.Grayscale;
            cardGleam.toneIntensity = dupeResultDesaturation;
        }
    }

    // 광택 띠가 카드를 한 번(신규는 그 이상) 훑고 지나간다.
    // 0과 1 양쪽 끝에서는 띠가 카드 밖이라 아무것도 남지 않는다 — NEW 워드마크의 gleam과 같은 규약이다.
    // 신규/중복의 갈림은 색과 횟수 둘로만 준다. 중복을 어둡게 하거나 흐리는 쪽은 쓰지 않는다 —
    // 중복도 환급이 붙는 정상 획득이라, 카드를 죽이면 "뽑았다"가 "손해 봤다"로 읽힌다.
    void PlayCardGleam(bool _instant)
    {
        if (cardGleam == null) return;

        cardGleam.transitionColor = IsNew ? cardGleamNewColor : cardGleamDupeColor;

        // 재생 중인 광택을 먼저 끊는다 — 신규는 반복 재생이라 다음 바인드까지 살아남을 수 있고,
        // 그러면 되돌려 놓은 rate를 뒤늦게 덮어쓴다. 트윈에 타깃을 달아두는 이유가 이 한 줄이다.
        DOTween.Kill(cardGleam);

        if (_instant)
        {
            cardGleam.transitionRate = 1f;
            return;
        }

        cardGleam.transitionRate = 0f;

        // 신규만 여러 번 훑는다. Restart 루프라 매 회 띠가 카드 앞에서 다시 출발한다(Yoyo면 되짚어 와 어색하다).
        int t_sweeps = IsNew ? 1 + cardGleamNewExtraSweeps : 1;

        DOTween.To(() => cardGleam.transitionRate, _v => cardGleam.transitionRate = _v, 1f, cardGleamDuration)
               .SetDelay(cardGleamDelay)
               .SetEase(Ease.InOutSine)
               .SetLoops(t_sweeps, LoopType.Restart)
               .SetTarget(cardGleam)
               .SetLink(cardGleam.gameObject);
    }

    // 림라이트가 테두리를 한 바퀴 돈다(신규 전용, 낱장이 드러나는 순간).
    // ⚠ 여기서 DOTween.Kill(cardGleam)을 부르면 안 된다 — 이 메서드는 항상 PlayCardGleam 뒤에 오고,
    //   타깃이 같아 방금 시작한 광택 트윈까지 함께 끊긴다. 걷어내기는 그쪽과 ResetAccent가 맡는다.
    void PlayRim()
    {
        if (cardGleam == null) return;

        SetRim(true);

        cardGleam.edgeShinyRate = 0f;

        // 등속이 아니라 InOutSine — 빛은 테두리를 도는 동안 모서리에서 잠깐 머물렀다 빠진다.
        DOTween.To(() => cardGleam.edgeShinyRate, _v => cardGleam.edgeShinyRate = _v, 1f, rimSweepDuration)
               .SetDelay(rimSweepDelay)
               .SetEase(Ease.InOutSine)
               .SetTarget(cardGleam)
               .SetLink(cardGleam.gameObject);
    }

    // 림라이트의 룩을 세우거나 완전히 내린다.
    // edgeColorFilter를 코드가 못 박는 이유: None이면 edgeColor가 무시돼 림이 아예 그려지지 않는다 —
    // 룩을 고르는 값이 아니라 이 효과가 성립하기 위한 전제라 인스펙터에 맡기지 않는다.
    void SetRim(bool _on)
    {
        if (cardGleam == null) return;

        if (!_on)
        {
            // rate로는 내릴 수 없다(위 ⚠ 참고) — 모드를 꺼야 띠가 사라진다.
            cardGleam.edgeMode = EdgeMode.None;
            cardGleam.edgeShinyAutoPlaySpeed = 0f;
            return;
        }

        cardGleam.edgeMode = EdgeMode.Shiny;
        cardGleam.edgeColorFilter = ColorFilter.Additive;
        cardGleam.edgeColor = rimColor;
        cardGleam.edgeWidth = rimThickness;
        cardGleam.edgeShinyWidth = rimArc;
        cardGleam.edgeShinyAutoPlaySpeed = 0f;   // 낱장 구간은 코드가 rate를 민다.
    }

    // 강조 요소를 내린 초기 상태. 재사용(풀링 없이 Instantiate이지만 Bind 재호출 대비)에도 안전하게.
    // 펀치는 여기서 되돌리지 않는다 — 그 축은 이 트랜스폼이고, 트랜스폼은 PackCardStack이 쥐고 있다.
    // 걷어낼 시점을 아는 쪽이 그쪽이라 취소도 그쪽이 SnapPunchToRest로 부른다.
    void ResetAccent()
    {
        Group.alpha = 1f;   // 더미에서든 결과 격자에서든 카드는 선명한 상태로 시작한다.

        if (newBadge != null) newBadge.SetActive(false);
        SetGleam(0f);   // 광택 띠를 글자 앞으로 되돌린다 — 중간에 멈춘 채 재사용되면 얼룩이 박힌 상태로 뜬다.

        // 카드 표면 광택도 같은 이유로 되돌린다. 이쪽은 반복 재생이 남아 있을 수 있어 트윈부터 끊는다.
        // 광택·림라이트가 같은 컴포넌트를 쓰므로 이 Kill 한 번이 둘을 함께 걷는다(타깃 단위).
        if (cardGleam != null)
        {
            DOTween.Kill(cardGleam);
            cardGleam.transitionRate = 0f;

            // 결과판에서 걸어 둔 탈채도를 되돌린다 — 남으면 다음 표시가 흑백으로 시작한다.
            cardGleam.toneFilter = ToneFilter.None;
            cardGleam.toneIntensity = 0f;
        }

        // 림라이트를 내린다. rate 0은 "안 보이는 상태"가 아니므로 모드를 꺼야 한다(SetRim 주석 참고).
        SetRim(false);

        // 뒤 광선은 신규가 드러나는 순간에만 켠다 — 더미에 깔린 카드들은 아직 자기 차례가 아니다.
        if (newAura != null) newAura.Hide();

        if (revealFlash != null)
        {
            revealFlash.DOKill();
            SetAlpha(revealFlash, 0f);
            revealFlash.gameObject.SetActive(false);
        }

        if (refundBadge != null)
        {
            refundBadge.transform.DOKill();
            if (m_refundHomeCaptured) refundBadge.transform.localPosition = m_refundHome;
            refundBadge.SetActive(false);
        }
    }

    // 카드가 통째로 톡 커졌다 돌아온다. 이 트랜스폼의 위치·회전은 PackCardStack이 쥐고 있으므로
    // 여기서는 스케일만 건드리고 DOKill도 부르지 않는다 — 걷어내면 같은 트랜스폼에 걸린 부유(위치)까지 죽는다.
    // 카드 한 장의 강조는 m_accented 가드로 생애 1회라 중복 펀치도 생기지 않는다.
    void PlayPunch()
    {
        if (punchDuration <= 0f) return;

        // 출발 배율을 적어 둔다 — 회수 목표이자, 도중에 끊겼을 때 돌아갈 자리다.
        m_punchRestScale = transform.localScale;

        // 충격은 t=0에 전부 들어간다. 커지는 구간을 트윈에 맡기면(DOPunchScale이 그랬다) 그 시간만큼 타격이 뭉개져
        // "톡 부풀었다"로 읽힌다 — 눈이 봐야 하는 것은 부풀어 오르는 과정이 아니라 이미 맞은 뒤의 회복이다.
        transform.localScale = Vector3.Scale(m_punchRestScale, new Vector3(
            1f + punchOvershoot + punchStretch,
            1f + punchOvershoot - punchStretch,
            1f));

        // OutQuint — 첫 프레임이 가장 빠르고 끝에서 단단히 선다.
        // OutBack·OutElastic은 제자리를 지나쳐 흔들려 다시 말랑해지므로 쓰지 않는다.
        transform.DOScale(m_punchRestScale, punchDuration)
                 .SetEase(Ease.OutQuint)
                 .SetLink(gameObject);
    }

    /// <summary>재생 중이던 펀치를 끊은 뒤 배율을 제자리로 돌린다.
    /// 트윈을 걷는 것은 이 트랜스폼을 공유하는 PackCardStack의 몫이고(타깃 단위 DOKill이라 부유와 함께 걷힌다),
    /// "쉬는 배율이 얼마인가"는 펀치를 건 이쪽만 안다 — 그 한 조각만 여기 남긴다.</summary>
    public void SnapPunchToRest() => transform.localScale = m_punchRestScale;

    // 섬광이 한 번 퍼졌다 잦아든다.
    void PlayFlash()
    {
        if (revealFlash == null) return;

        revealFlash.DOKill();
        revealFlash.gameObject.SetActive(true);
        SetAlpha(revealFlash, 0f);

        DOTween.Sequence()
            .SetLink(revealFlash.gameObject)
            .Append(revealFlash.DOFade(flashPeakAlpha, flashRiseDuration))
            .Append(revealFlash.DOFade(0f, flashFallDuration))
            .OnComplete(() => { if (revealFlash != null) revealFlash.gameObject.SetActive(false); });
    }

    // 신규: NEW 워드마크가 꽂히고 그 위를 광택이 훑는다. 섬광은 전 카드 공통(PlayFlash)이므로 여기 남은 것은
    // 워드마크뿐이다 — 신규를 가리는 신호는 이것으로 충분하고, 섬광까지 신규 전용으로 두면 중복 카드의 확인 순간이 밋밋해진다.
    void PlayNewBadge(bool _instant)
    {
        if (newBadge == null) return;

        newBadge.SetActive(true);

        var t_tr = newBadge.transform;
        if (!m_newBadgeRestCaptured)
        {
            m_newBadgeRestScale = t_tr.localScale;
            m_newBadgeRestCaptured = true;
        }

        if (_instant)
        {
            // 결과 격자에선 이미 다 지나간 상태로 세운다 — 광택 띠가 한 장에 걸린 채 멈춰 있으면 얼룩으로 보인다.
            t_tr.localScale = m_newBadgeRestScale;
            SetGleam(1f);
            return;
        }

        // 카드 펀치와 같은 규약 — 충격은 t=0에 다 들어가고 눈이 보는 것은 회복이다.
        t_tr.DOKill();
        t_tr.localScale = m_newBadgeRestScale * (1f + newBadgeOvershoot);
        t_tr.DOScale(m_newBadgeRestScale, newBadgeDuration).SetEase(Ease.OutQuint).SetLink(newBadge);

        PlayGleam();
    }

    // Transition-Shiny의 진행도를 0→1로 밀면 광택 띠가 글자를 훑고 지나간다.
    // 0과 1 양쪽 끝에서는 띠가 글자 밖이라 아무것도 남지 않는다 — 그래서 시작·종료 상태를 따로 치울 필요가 없다.
    void PlayGleam()
    {
        if (newGleam == null) return;

        SetGleam(0f);
        DOTween.To(() => newGleam.transitionRate, _v => newGleam.transitionRate = _v, 1f, newGleamDuration)
               .SetDelay(newGleamDelay)
               .SetEase(Ease.InOutSine)
               .SetLink(newGleam.gameObject);
    }

    void SetGleam(float _rate)
    {
        if (newGleam != null) newGleam.transitionRate = _rate;
    }

    // 중복: 환급 숫자가 떠오르며 사라진다. 환급이 0이면 아무 말도 하지 않는다(조용한 정산).
    void PlayRefundAccent(bool _instant)
    {
        if (refundBadge == null || Refund <= 0) return;

        if (refundText != null) refundText.text = $"+{Refund:N0}";

        var t_tr = refundBadge.transform;
        if (!m_refundHomeCaptured)
        {
            m_refundHome = t_tr.localPosition;
            m_refundHomeCaptured = true;
        }

        if (_instant)
        {
            // 스킵 시엔 제자리에 띄워두기만 — 떠오르는 도중 스킵돼도 숫자가 남는다.
            t_tr.DOKill();
            t_tr.localPosition = m_refundHome;
            refundBadge.SetActive(true);
            return;
        }

        t_tr.DOKill();
        t_tr.localPosition = m_refundHome;
        refundBadge.SetActive(true);
        t_tr.DOLocalMoveY(m_refundHome.y + refundRiseDistance, refundDuration)
            .SetEase(Ease.OutCubic)
            .SetLink(refundBadge);
    }

    static void SetAlpha(Graphic _graphic, float _alpha)
    {
        var t_c = _graphic.color;
        t_c.a = _alpha;
        _graphic.color = t_c;
    }
}
