using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

// 개봉 카드 더미. 카드를 한 자리에 겹쳐 쌓고, 맨 위부터 스와이프로 한 장씩 밀어낸다.
// 카드는 앞면이라 맨 위가 처음부터 보인다 — 서스펜스는 "밀어냈을 때 그 아래 뭐가 있나"에 있다.
// 방향은 가리지 않는다(좌우·위아래·대각 전부) — 민 쪽으로 그대로 날아간다. 짧아도 빠르게 튕기면 넘어간다.
// 밀린 카드는 민 방향으로 날아가며 사라진다. 결과 라인업은 더미가 다 빈 뒤 PackResultGrid가 따로 세운다 —
//   밀어내기(좌표 직접 조작)와 결과 배치(레이아웃)가 같은 오브젝트를 두고 다투지 않게 하려는 분리다.
//
// 배선 전제: 카드와 stackAnchor가 모두 cardLayer의 자식이고 앵커가 같아야 한다
//   (anchoredPosition을 같은 좌표계로 직접 비교·보간하므로).
// 그리고 cardLayer는 팩 앞뒷면 껍데기와 같은 무대(PackStage)의 자식이어야 한다 —
//   앞뒷면 사이에 끼어야 "팩 속"이 성립하고, 같은 좌표계라 PlaceInsidePack이 값 하나로 자리를 정할 수 있다.
// 입력은 이 컴포넌트가 붙은 오브젝트의 Graphic(raycastTarget)이 받는다 — 카드 프리팹은 입력을 모른다.
public class PackCardStack : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    // 새 카드가 맨 위로 드러났다(첫 장 포함). 강조 재생은 구독자가 결정한다.
    public event Action<PackCardView> OnCardRevealed;

    // 마지막 장까지 밀려 더미가 비었다.
    public event Action OnEmptied;

    // 드래그가 아닌 단순 탭 — 스킵 요청. 이 컴포넌트가 입력을 먹으므로 상위로 올려준다.
    public event Action OnSkipRequested;

    [Header("배치")]
    [Tooltip("카드·앵커가 함께 사는 좌표계 루트. 카드는 전부 이 아래 생성된다.")]
    [SerializeField] RectTransform cardLayer;
    [Tooltip("더미가 쌓이는 자리. 모든 카드가 여기 겹쳐 놓인다.")]
    [SerializeField] RectTransform stackAnchor;
    [SerializeField] PackCardView cardPrefab;

    [Header("등장 따라붙기")]
    [Tooltip("뒷장 한 장당 뒤처지는 거리(카드 로컬px). 팩 속에선 그만큼 더 깊이 잠겨 " +
             "봉인이 찢겨도 맨 위 한 장의 윗변만 구멍으로 삐져나온다.")]
    [SerializeField] Vector2 emergeLagStep = new Vector2(0f, -26f);
    [Tooltip("뒷장이 앞장을 따라붙기 시작하는 시차(초). n장이면 마지막 장이 (n-1)×이 값만큼 늦게 도착한다 " +
             "— 그 꼬리가 곧 \"탁 정리되는\" 한 박자다.")]
    [SerializeField] float emergeLagStagger = 0.045f;

    [Header("맨 위 카드 부유")]
    [Tooltip("제자리 기준 상하 진폭(px). 정지한 UI가 아니라 손에 들린 물체로 읽히게 하는 최소한의 움직임.")]
    [SerializeField] float topFloatAmplitude = 4f;
    [Tooltip("부유 왕복 한 주기(초).")]
    [SerializeField] float topFloatPeriod = 2.5f;

    [Header("스와이프")]
    [Tooltip("이만큼 밀면 넘어간다(방향 무관 — 민 거리 그대로). 못 미치면 제자리로 되돌아온다.")]
    [SerializeField] float flickThreshold = 90f;
    [Tooltip("이 속도(단위/초) 이상으로 튕기면 거리가 부족해도 넘어간다. 0이면 속도 판정 없음.")]
    [SerializeField] float flickSpeed = 700f;
    [Tooltip("속도로 넘길 때 최소한 이만큼은 밀어야 한다(스치는 터치를 넘김으로 오인하지 않게).")]
    [SerializeField] float flickMinDistance = 20f;
    [Tooltip("미는 양에 비례해 카드가 기우는 정도(도/픽셀).")]
    [SerializeField] float dragTiltPerPixel = 0.06f;
    [SerializeField] float returnDuration = 0.25f;

    [Header("밀어내기")]
    [Tooltip("넘긴 카드가 민 방향으로 더 날아가는 거리. 화면 밖까지 가도록 넉넉히.")]
    [SerializeField] float dismissDistance = 900f;
    [SerializeField] float dismissDuration = 0.28f;
    [Tooltip("날아가며 추가로 기우는 각도(도).")]
    [SerializeField] float dismissTiltAngle = 12f;

    // 더미(위→아래 순). 인덱스 0이 항상 현재 맨 위다.
    readonly List<PackCardView> m_stack = new List<PackCardView>();

    // 날아가는 중인 카드. 연출이 끝나기 전에 Clear가 들어와도 화면에 유령이 남지 않게 추적한다.
    readonly List<PackCardView> m_dismissing = new List<PackCardView>();

    // 카드가 쉬는 자리(되감기·부유의 기준). 모든 카드가 같은 자리에 정확히 겹쳐야 더미가 "한 장"으로 읽히므로
    // 카드별로 갈라 둘 값이 아니다 — 등장 중의 어긋남은 좌표가 아니라 도착 시각의 차이로 만든다.
    Vector2 m_cardHome;

    Canvas m_canvas;
    bool m_interactable;
    bool m_dragging;

    // 등장(따라붙기)이 카드 자리를 쥐고 있는 동안만 참. 중첩된 트윈은 끊을 수 없으니 이 플래그로 손을 떼게 한다.
    bool m_emerging;

    // 팩에서 뽑혀 나오는 등장 연출의 기준(=평소 자리). 뭉치 이동은 cardLayer 한 덩어리로 처리하므로
    // 카드가 쉬는 자리(m_cardHome)는 건드리지 않는다 — 등장과 넘기기가 같은 좌표를 두고 다투지 않게 한 분리다.
    Vector2 m_layerHome;
    Vector3 m_layerScaleHome;
    bool m_layerHomeCaptured;

    // 뽑혀 나온 더미가 정착하는 자리·크기. 씬 배치값에서 출발하되 PlayEmerge가 실제 목표로 갱신한다 —
    // 연출을 건너뛰고 되돌릴 때 씬 배치값으로 돌아가면 뽑아 올린 만큼이 도로 내려앉고 크기도 어긋난다.
    Vector2 m_layerSettled;
    Vector3 m_layerSettledScale = Vector3.one;

    // 최근 프레임의 미는 속도(카드 좌표계 단위/초). 짧고 빠른 플릭을 거리 대신 이 값으로 살린다.
    float m_dragSpeed;

    public int Remaining => m_stack.Count;

    void Awake()
    {
        m_canvas = GetComponentInParent<Canvas>();
        // anchoredPosition·localScale은 부모 rect 크기와 무관하다 — Canvas가 rect를 드라이브하기 전에 잡아도 안전하다.
        CaptureLayerHome();
    }

    /// <summary>뽑힌 카드로 더미를 만든다. 아직 입력은 받지 않는다(분출 연출이 끝난 뒤 BeginInteraction).</summary>
    public void Build(IReadOnlyList<DrawnCard> _cards)
    {
        Clear();

        if (cardPrefab == null || cardLayer == null || stackAnchor == null)
        {
            Debug.LogWarning("[PackCardStack] cardPrefab/cardLayer/stackAnchor 미배선 → 더미 생성 불가.");
            return;
        }

        m_cardHome = stackAnchor.anchoredPosition;

        int t_count = _cards != null ? _cards.Count : 0;

        // 뒤에서부터 만들어 인덱스 0이 마지막 sibling(=가장 위에 그려짐)이 되게 한다.
        for (int t_i = t_count - 1; t_i >= 0; t_i--)
        {
            var t_drawn = _cards[t_i];
            if (t_drawn.Card == null) continue;

            var t_view = Instantiate(cardPrefab, cardLayer);
            t_view.Bind(t_drawn);

            var t_rt = (RectTransform)t_view.transform;

            // 회전은 처음부터 0이다 — 각도가 어긋나면 몇 장이 겹쳤는지 세어지고 "한 장"이 깨진다.
            t_rt.localRotation = Quaternion.identity;
            t_rt.localScale = Vector3.one;

            m_stack.Insert(0, t_view);   // 역순 생성이라 앞에 꽂아야 위→아래 순서가 맞는다.
        }

        // 자리는 더미 순서가 확정된 뒤에 잡는다 — 빈 카드를 건너뛰면 원본 인덱스와 더미 깊이가 어긋나고,
        // 뒤처짐은 "몇 번째 카드였나"가 아니라 "몇 장 아래에 깔렸나"에 따라야 한다.
        ResetEmergeLag();
    }

    // 아직 아무것도 따라붙지 않은 상태 — 각 카드가 깊이만큼 뒤로 물려 있다.
    void ResetEmergeLag() => ApplyEmergeLag(0f, 1f);

    /// <summary>
    /// 등장 경과 시각(_elapsed)에 맞춰 모든 카드의 자리를 다시 계산한다.
    /// 쉬는 자리는 전부 같고 여기서 주는 것은 "아직 도착하지 않은 만큼"일 뿐이다 —
    /// 깊은 카드가 늦게 출발하는 그 시차가 곧 "뒷장이 앞장을 따라 올라온다"로 읽힌다.
    ///
    /// 카드마다 트윈을 따로 만들지 않고 시각 하나로 전부 다시 계산하는 이유는 <see cref="PlayEmerge"/> 참고.
    /// </summary>
    void ApplyEmergeLag(float _elapsed, float _duration)
    {
        for (int t_i = 0; t_i < m_stack.Count; t_i++)
        {
            if (m_stack[t_i] == null) continue;

            float t_progress = _duration > 0f
                ? Mathf.Clamp01((_elapsed - t_i * emergeLagStagger) / _duration)
                : 1f;

            // InOutQuint — 잠깐 뒤처져 버티다 쭉 붙고 멈춘다. OutBack류는 쓸 수 없다(제자리를 지나쳐 다시 어긋난다).
            float t_closed = DOVirtual.EasedValue(0f, 1f, t_progress, Ease.InOutQuint);

            ((RectTransform)m_stack[t_i].transform).anchoredPosition =
                m_cardHome + emergeLagStep * (t_i * (1f - t_closed));
        }
    }

    // 카드를 전부 쉬는 상태로 되돌린다(연출 중단 대비). 등장 도중 끊기면 계단 모양으로 굳는데,
    // 지터가 있던 시절엔 그냥 겹친 더미로 보였지만 정렬이 규약이 된 지금은 고장으로 보인다.
    void SnapCardsHome()
    {
        for (int t_i = 0; t_i < m_stack.Count; t_i++)
        {
            // OnDisable은 씬이 걷히는 중에도 온다 — 이미 파괴된 카드가 섞여 있을 수 있는 유일한 경로다.
            if (m_stack[t_i] == null) continue;

            var t_rt = (RectTransform)m_stack[t_i].transform;
            t_rt.anchoredPosition = m_cardHome;
            t_rt.localRotation = Quaternion.identity;
            m_stack[t_i].SnapPunchToRest();
        }
    }

    /// <summary>
    /// 더미를 팩 속에 든 것처럼 줄여 _stackCenter(무대 로컬 좌표)에 겹쳐 둔다.
    /// 팩이 등장하기 전에 호출한다 — 카드는 처음부터 팩 속에 들어 있어야 하고,
    /// 봉인이 찢기는 순간 그 구멍으로 카드 끝이 저절로 드러난다(따로 등장시키지 않는다).
    ///
    /// 배선 전제: cardLayer와 팩 껍데기가 같은 무대(PackStage)의 자식이어야 한다 —
    /// 같은 좌표계라 월드 변환 없이 값 하나로 "팩 속 어디"를 지정할 수 있다.
    /// </summary>
    public void PlaceInsidePack(Vector2 _stackCenter, float _scale)
    {
        if (cardLayer == null || stackAnchor == null) return;

        CaptureLayerHome();
        cardLayer.DOKill();

        cardLayer.localScale = m_layerScaleHome * _scale;
        cardLayer.anchoredPosition = LayerPosFor(_stackCenter, _scale);
    }

    /// <summary>
    /// 팩 속에 있던 더미를 _targetCenter(무대 로컬 좌표)·_targetScale(씬 배치 대비 배율)로
    /// 솟아오르게 한다(뭉치째 뽑혀 나오는 연출).
    /// 반환 시퀀스를 호출부의 흐름에 끼우면 스킵 한 번으로 등장까지 함께 완료된다.
    /// 아래쪽은 팩 앞면이 계속 가리므로, 뽑히는 동안 카드가 입구에서 빠져나오는 것처럼 읽힌다.
    ///
    /// 목표를 밖에서 받는 이유: 팩 속 자리와의 차이가 곧 "뽑혀 나온 거리"다 —
    /// 그 거리를 씬 배치에 묶어 두면 팩만 내려가고 더미는 제자리인 그림이 된다.
    /// 배율까지 받는 이유: 무대가 통째로 확대돼 있으면 그 배율이 더미에도 곱해진다 —
    /// 되물릴 몫을 호출부가 넘겨야 팩을 얼마나 키우든 뽑힌 카드는 늘 같은 크기로 선다.
    /// </summary>
    public Sequence PlayEmerge(Vector2 _targetCenter, float _targetScale, float _duration)
    {
        if (cardLayer == null || stackAnchor == null) return null;

        CaptureLayerHome();
        cardLayer.DOKill();

        m_layerSettled = LayerPosFor(_targetCenter, _targetScale);
        m_layerSettledScale = m_layerScaleHome * _targetScale;

        var t_seq = DOTween.Sequence()
            .SetLink(cardLayer.gameObject)
            // 살짝 넘겼다 내려앉는다 — 뽑혀 나온 물건은 관성으로 한 번 튄다. 과하면 "커졌다"로 읽히므로 약하게.
            .Join(cardLayer.DOAnchorPos(m_layerSettled, _duration).SetEase(Ease.OutBack, 1.05f))
            .Join(cardLayer.DOScale(m_layerSettledScale, _duration).SetEase(Ease.OutCubic));

        // 뒷장이 앞장을 따라붙는다. 뭉치 이동은 위에서 cardLayer가 통째로 맡고, 여기서 닫는 것은
        // Build가 깊이만큼 벌려 둔 뒤처짐뿐이다 — 두 축이 겹쳐 "따라 올라온다"가 된다.
        //
        // ⚠ 카드마다 트윈을 만들어 이 시퀀스에 끼우면 안 된다. 시퀀스에 들어간 트윈은 DOTween의 active 목록에서
        //   빠지므로 DOKill(target)이 닿지 않고 개별 Kill·SetLink도 거부된다 — 등장 도중 Clear가 들어오면
        //   이미 파괴된 RectTransform에 계속 쓰는 트윈이 남는다. 그래서 굴리는 것은 경과 시각 하나뿐이고
        //   자리는 setter가 매 프레임 현재 더미를 보고 다시 계산한다(더미가 비면 아무것도 하지 않는다).
        // 시퀀스 길이도 이 트윈이 결정한다 — 따라붙기 꼬리까지 끝난 뒤에 다음 단계로 넘어가야 한다.
        float t_span = _duration + Mathf.Max(0, m_stack.Count - 1) * emergeLagStagger;
        float t_elapsed = 0f;

        // 등장을 중단시키는 손잡이는 이 플래그 하나다. 반환한 시퀀스는 호출부의 흐름에 중첩되므로
        // 이쪽에서 Kill·SnapEmerged·DOKill 어느 것으로도 멈출 수 없다(위 ⚠ 참고) — 그래서 트윈을 끊는 대신
        // setter가 스스로 손을 떼게 한다. 이게 없으면 더미를 걷은 뒤에도 다음 프레임에 계단을 다시 그린다.
        m_emerging = true;

        // 가감속은 카드별로 setter가 따로 입힌다 → 굴리는 시각 자체는 등속이어야 한다.
        t_seq.Join(DOTween.To(() => t_elapsed,
                              _v => { t_elapsed = _v; if (m_emerging) ApplyEmergeLag(_v, _duration); },
                              t_span, t_span)
                          .SetEase(Ease.Linear)
                          .OnComplete(() => m_emerging = false));

        return t_seq;
    }

    // 지정 중심(무대 로컬)에 더미를 놓기 위한 레이어 좌표.
    // 레이어를 줄이면 그 안의 앵커 자리도 같이 당겨진다 — 그만큼 되밀어야 더미 중심이 정확히 그 지점에 온다.
    Vector2 LayerPosFor(Vector2 _center, float _scale)
        => _center - stackAnchor.anchoredPosition * (m_layerScaleHome.x * _scale);

    // 등장 연출을 건너뛰고 평소 자리로 되돌린다(걷어내기·비활성 대비). 외부에서 부를 일은 없다 —
    // 자리를 되돌릴 시점은 "더미를 치울 때"뿐이고 그 판단은 이 클래스가 쥔다.
    void SnapEmerged()
    {
        if (cardLayer == null) return;

        CaptureLayerHome();
        cardLayer.DOKill();
        cardLayer.anchoredPosition = m_layerSettled;
        cardLayer.localScale = m_layerSettledScale;
    }

    /// <summary>입력을 켜고 첫 장이 드러났음을 알린다(앞면이라 이미 보이고 있다).</summary>
    public void BeginInteraction()
    {
        m_interactable = true;
        if (m_stack.Count == 0) return;

        // 알림이 먼저다 — 구독자의 등장 강조(스케일 펀치)가 부유(위치)보다 뒤에 걸리면
        // 그쪽이 이 트랜스폼을 DOKill하며 부유까지 함께 끊는다. 두 축은 타깃이 같다.
        OnCardRevealed?.Invoke(m_stack[0]);
        StartFloat(m_stack[0]);
    }

    // 맨 위 카드만 제자리에서 미세하게 뜬다. 아래 카드는 건드리지 않는다 — 하나만 살아 있어야 그 대비가 읽힌다.
    // 대상을 인자로 받는 이유: 호출 시점(되감기 완료 콜백 포함)마다 "지금 맨 위"를 다시 물으면
    // 그사이 맨 위가 바뀌었는지를 추론해야 한다. 걸 대상은 호출부가 이미 알고 있다.
    //
    // DOKill을 부르지 않는 이유: 카드가 맨 위가 되는 것은 생애 한 번이고, 되돌리기 경로는 OnBeginDrag가
    // 이미 트윈을 걷어낸 뒤다. 여기서 걷으면 같은 트랜스폼에 방금 걸린 등장 펀치(스케일)까지 죽는다.
    void StartFloat(PackCardView _view)
    {
        if (_view == null || topFloatAmplitude <= 0f || topFloatPeriod <= 0f) return;

        var t_rt = (RectTransform)_view.transform;

        // 제자리에서 그대로 떠오른다 — 진폭 끝으로 순간이동시켜 시작하면 등장·되감기가 착지한 바로 다음 프레임에
        // 툭 튄다. 가장 매끄러워야 하는 두 지점이라 4px도 눈에 걸린다.
        // 그래서 첫 구간만 1/4주기로 위 끝까지 올리고, 거기서부터 반주기 왕복(=한 주기 topFloatPeriod)에 넘긴다.
        t_rt.DOAnchorPosY(m_cardHome.y + topFloatAmplitude, topFloatPeriod * 0.25f)
            .SetEase(Ease.InOutSine)
            .SetLink(t_rt.gameObject)
            .OnComplete(() =>
            {
                t_rt.DOAnchorPosY(m_cardHome.y - topFloatAmplitude, topFloatPeriod * 0.5f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetLink(t_rt.gameObject);
            });
    }

    /// <summary>남은 카드를 전부 즉시 치운다(스킵). 결과는 곧이어 뜨는 결과 격자가 보여준다.</summary>
    public void FlickAllImmediate()
    {
        m_interactable = false;
        m_dragging = false;
        m_emerging = false;   // 카드를 지우는 중이다 — 등장 setter가 더 손대지 않게 먼저 뗀다.

        while (m_stack.Count > 0)
        {
            var t_view = m_stack[0];
            m_stack.RemoveAt(0);

            if (t_view != null)
            {
                t_view.transform.DOKill();
                Destroy(t_view.gameObject);
            }
        }

        OnEmptied?.Invoke();
    }

    /// <summary>생성된 카드를 모두 제거(다음 개봉 세션 대비). 날아가는 중인 카드도 함께 걷는다.</summary>
    public void Clear()
    {
        // 파괴보다 먼저 뗀다 — 등장 setter는 이 클래스가 끊을 수 없는 중첩 트윈이 굴린다(PlayEmerge의 ⚠ 참고).
        m_emerging = false;

        for (int t_i = 0; t_i < m_stack.Count; t_i++)
            if (m_stack[t_i] != null) Destroy(m_stack[t_i].gameObject);

        for (int t_i = 0; t_i < m_dismissing.Count; t_i++)
            if (m_dismissing[t_i] != null) Destroy(m_dismissing[t_i].gameObject);

        m_stack.Clear();
        m_dismissing.Clear();
        m_interactable = false;
        m_dragging = false;

        // 등장 도중 걷어냈다면 레이어가 축소된 채 굳는다 — 다음 개봉이 그 상태를 물려받지 않게 되돌린다.
        SnapEmerged();
    }

    public void OnBeginDrag(PointerEventData _e)
    {
        if (!m_interactable || m_stack.Count == 0) return;
        m_dragging = true;
        m_dragSpeed = 0f;

        var t_top = m_stack[0];
        if (t_top == null) return;

        // DOKill은 타깃 단위라 부유(위치)와 함께 카드 뷰의 등장 펀치(스케일)도 걷힌다 —
        // 재생 중에 끊기면 어긋난 배율에 굳으므로 뷰에 제자리를 되돌리게 한다.
        t_top.transform.DOKill();
        t_top.SnapPunchToRest();
    }

    public void OnDrag(PointerEventData _e)
    {
        if (!m_dragging || m_stack.Count == 0) return;

        var t_rt = TopRect();
        if (t_rt == null) return;

        // 캔버스 스케일을 나눠야 화면 이동량과 카드 이동량이 일치한다.
        float t_scale = m_canvas != null ? m_canvas.scaleFactor : 1f;
        if (t_scale <= 0f) t_scale = 1f;

        var t_move = _e.delta / t_scale;
        t_rt.anchoredPosition += t_move;

        // 속도는 거리와 같은 좌표계에서 재야 두 임계를 나란히 비교할 수 있다.
        float t_dt = Time.unscaledDeltaTime;
        if (t_dt > 0f) m_dragSpeed = t_move.magnitude / t_dt;

        // 민 만큼 기운다 — 손에 붙는 느낌은 위치보다 회전에서 온다(기울기는 좌우 성분만 반영).
        float t_dx = t_rt.anchoredPosition.x - m_cardHome.x;
        t_rt.localRotation = Quaternion.Euler(0f, 0f, -t_dx * dragTiltPerPixel);
    }

    public void OnEndDrag(PointerEventData _e)
    {
        if (!m_dragging || m_stack.Count == 0) return;
        m_dragging = false;

        var t_top = m_stack[0];
        var t_rt = TopRect();
        if (t_rt == null) return;

        // 방향을 가리지 않는다 — 어느 쪽으로 밀었든 민 거리로 판정하고 그 방향으로 날려보낸다.
        var t_offset = t_rt.anchoredPosition - m_cardHome;
        float t_dist = t_offset.magnitude;

        // 거리가 찼거나, 짧아도 충분히 빠르게 튕겼으면 넘긴다.
        bool t_flicked = flickSpeed > 0f && m_dragSpeed >= flickSpeed && t_dist >= flickMinDistance;

        // 둘 다 아니면 되감기 — 실수로 건드린 것을 넘김으로 처리하지 않는다.
        if (t_dist < flickThreshold && !t_flicked)
        {
            ReturnHome(t_top);
            return;
        }

        m_stack.RemoveAt(0);
        DismissCard(t_top, t_offset / Mathf.Max(0.0001f, t_dist));   // 민 방향 그대로 날려보낸다.

        // 위 장이 비켜난 순간 아래 장은 이미 완전히 드러나 있다.
        if (m_stack.Count == 0)
        {
            OnEmptied?.Invoke();
            return;
        }

        // 알림 → 부유 순서는 BeginInteraction과 같다(강조의 DOKill이 부유를 삼키지 않게).
        OnCardRevealed?.Invoke(m_stack[0]);
        StartFloat(m_stack[0]);
    }

    public void OnPointerClick(PointerEventData _e)
    {
        // 드래그로 소비된 포인터는 클릭으로 오지 않는다 — 순수 탭만 스킵으로 본다.
        if (_e.dragging) return;
        OnSkipRequested?.Invoke();
    }

    // 밀려난 카드를 민 방향으로 날려보내며 지운다. 결과는 남기지 않는다 — 전부 넘긴 뒤 결과 격자가 다시 보여준다.
    void DismissCard(PackCardView _view, Vector2 _dir)
    {
        if (_view == null) return;

        var t_rt = (RectTransform)_view.transform;
        t_rt.DOKill();

        var t_target = t_rt.anchoredPosition + _dir * dismissDistance;
        var t_group  = _view.Group;

        m_dismissing.Add(_view);

        var t_seq = DOTween.Sequence()
            .SetLink(_view.gameObject)
            .Join(t_rt.DOAnchorPos(t_target, dismissDuration).SetEase(Ease.OutCubic))
            .Join(t_rt.DOLocalRotate(new Vector3(0f, 0f, -_dir.x * dismissTiltAngle), dismissDuration));

        if (t_group != null)
            t_seq.Join(t_group.DOFade(0f, dismissDuration).SetEase(Ease.InQuad));

        t_seq.OnComplete(() =>
        {
            m_dismissing.Remove(_view);
            if (_view != null) Destroy(_view.gameObject);
        });
    }

    // 임계에 못 미친 카드를 원래 자리로 되돌린다. 되돌아온 뒤엔 부유를 되찾는다 —
    // 손을 뗀 카드가 굳어 있으면 조금 밀어봤다는 이유로 맨 위가 죽은 것처럼 보인다.
    void ReturnHome(PackCardView _view)
    {
        if (_view == null) return;

        var t_rt = (RectTransform)_view.transform;
        t_rt.DOKill();
        t_rt.DOAnchorPos(m_cardHome, returnDuration).SetEase(Ease.OutBack)
            .SetLink(_view.gameObject).OnComplete(() => StartFloat(_view));
        t_rt.DOLocalRotate(Vector3.zero, returnDuration).SetLink(_view.gameObject);
    }

    RectTransform TopRect()
        => m_stack.Count > 0 && m_stack[0] != null ? (RectTransform)m_stack[0].transform : null;

    void CaptureLayerHome()
    {
        if (m_layerHomeCaptured || cardLayer == null) return;
        m_layerHome = cardLayer.anchoredPosition;
        m_layerScaleHome = cardLayer.localScale;
        // PlayEmerge가 실제 목표로 덮어쓴다.
        m_layerSettled = m_layerHome;
        m_layerSettledScale = m_layerScaleHome;
        m_layerHomeCaptured = true;
    }

    void OnDisable()
    {
        // 등장 setter가 먼저 손을 떼야 아래 SnapCardsHome이 실제로 남는다 — 트윈 쪽은 끊을 수 없다.
        m_emerging = false;

        // 연출 도중 비활성 시 좀비 트윈 정리 + 입력 상태 리셋(재활성 후 유령 드래그 방지).
        for (int t_i = 0; t_i < m_stack.Count; t_i++)
            if (m_stack[t_i] != null) m_stack[t_i].transform.DOKill();

        // 등장·부유·미는 도중에 끊겼으면 카드가 그 자리에 굳는다 — 자리를 되돌려 다음 표시가 그 상태를 물려받지 않게 한다.
        // 레이어만 되돌리고(SnapEmerged) 카드는 두면 계단 모양으로 어긋난 더미가 그대로 남는다.
        SnapCardsHome();

        // 날아가던 카드는 도착지가 화면 밖이다 — 트윈만 끊으면 반투명 유령이 남으므로 여기서 정리한다.
        for (int t_i = 0; t_i < m_dismissing.Count; t_i++)
            if (m_dismissing[t_i] != null) Destroy(m_dismissing[t_i].gameObject);
        m_dismissing.Clear();

        SnapEmerged();

        m_interactable = false;
        m_dragging = false;
    }
}
