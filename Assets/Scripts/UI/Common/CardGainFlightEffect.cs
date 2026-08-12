using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 획득한 카드가 원점에서 부채꼴로 펼쳐졌다가 목적지(도감 탭)로 빨려 들어가는 UI 연출.
// 궤적 규칙은 UiGainBurst가 갖고, 이 컴포넌트는 "카드를 만들고 걷는" 일만 한다(코인 연출과 궤적이 갈라지지 않게).
// 카드는 재생할 때 만들고 끝나면 지운다 — 로비 진입 시 한 번만 도는 연출이라 풀링하지 않는다.
public class CardGainFlightEffect : MonoBehaviour
{
    [Header("배선")]
    [Tooltip("날아갈 카드 프리팹(CardVisualView). 미배선이면 카드 아트 Image 한 장으로 대체한다.")]
    [SerializeField] CardVisualView cardPrefab;
    [SerializeField] RectTransform spawnCenter;   // 분출 원점(미배선이면 자기 자신)
    [SerializeField] RectTransform target;        // 카드가 모이는 목적지(보통 도감 탭 버튼)

    [Header("연출 값")]
    [Tooltip("화면에 보일 카드 크기. 프리팹을 쓰면 원본 비율을 유지한 채 이 높이에 맞춰 축소한다.")]
    [SerializeField] Vector2 cardSize = new Vector2(220f, 300f);
    [Tooltip("펼쳐지는 거리(이 오브젝트의 로컬 = 캔버스 참조px).")]
    [SerializeField] float scatterRadius = 260f;
    [SerializeField] float scatterDuration = 0.34f;
    [SerializeField] float gatherDuration = 0.4f;
    [Tooltip("카드 한 장씩 출발이 밀리는 간격. 0이면 전부 동시에 펼쳐진다.")]
    [SerializeField] float cardInterval = 0.09f;
    [SerializeField] float popDuration = 0.16f;
    [Tooltip("펼쳐지는 부채꼴의 시작 각(도). 기본값은 위쪽으로 펼친다.")]
    [SerializeField] float angleStart = 55f;
    [Tooltip("펼쳐지는 부채꼴의 각도 폭.")]
    [SerializeField] float angleSpan = 70f;
    [Tooltip("목적지에 닿을 때까지 줄어드는 배율 — 탭 안으로 삼켜지는 느낌.")]
    [SerializeField] float gatherScale = 0.15f;
    [Tooltip("비행 중 좌우로 흔드는 회전량(도).")]
    [SerializeField] float spinDegrees = 20f;

    readonly List<GameObject> m_cards = new List<GameObject>();

    /// <summary>
    /// 배선을 런타임에 갈아 끼운다(프리팹에 미리 꽂아둘 수 없는 상황용 — 로비 획득 연출이 이 경로를 쓴다).
    /// </summary>
    public void Configure(RectTransform _spawnCenter, RectTransform _target)
    {
        this.spawnCenter = _spawnCenter;
        this.target      = _target;
    }

    /// <summary>
    /// 펼침→수렴 시퀀스를 만들어 돌려준다(재생은 호출자 시퀀스에 맡긴다).
    /// _onArrived(도착한 장수, 전체 장수)는 카드가 목적지에 닿을 때마다 불린다 — 탭 강조를 여기에 맞물린다.
    /// </summary>
    public Sequence BuildFlight(IReadOnlyList<CardData> _cards, Action<int, int> _onArrived)
    {
        ClearCards();

        int t_count = _cards != null ? _cards.Count : 0;
        if (t_count <= 0)
        {
            // 날릴 카드가 없어도 호출자 시퀀스가 멈추지 않게 도착만 통지한다.
            var t_empty = DOTween.Sequence().SetLink(gameObject);
            t_empty.AppendCallback(() => _onArrived?.Invoke(1, 1));
            return t_empty;
        }

        var t_self = (RectTransform)transform;
        Vector2 t_from = this.spawnCenter != null ? UiGainBurst.ToLayerLocal(t_self, this.spawnCenter) : Vector2.zero;
        Vector2 t_to   = this.target != null ? UiGainBurst.ToLayerLocal(t_self, this.target) : Vector2.zero;

        var t_settings = new UiGainBurst.Settings(t_count, this.scatterRadius, this.scatterDuration,
                                                  this.gatherDuration, this.cardInterval, this.popDuration,
                                                  this.angleStart, this.angleSpan,
                                                  this.gatherScale, this.spinDegrees, RestScale());

        var t_seq = UiGainBurst.Build(t_self, t_from, t_to, t_settings,
                                      _spawn: _i => CreateCard(_cards[_i]),
                                      _despawn: _rt => { if (_rt != null) _rt.gameObject.SetActive(false); },
                                      _onArrived: _onArrived);
        t_seq.SetLink(gameObject);

        // 정상 종료든 스킵(Complete)이든 여기서 카드를 걷는다.
        t_seq.AppendCallback(ClearCards);
        return t_seq;
    }

    void OnDisable()
    {
        // 연출 도중 꺼지면 시퀀스의 마지막 콜백이 오지 않는다 — 남은 카드는 여기서 정리.
        ClearCards();
    }

    // 프리팹은 원본 크기를 유지한 채 축소한다 — sizeDelta를 강제하면 CardVisualView의 내부 비율 배치가 깨진다.
    float RestScale()
    {
        if (this.cardPrefab == null) return 1f;

        var t_rect = (RectTransform)this.cardPrefab.transform;
        float t_height = t_rect.sizeDelta.y;
        if (t_height <= 0f) return 1f;

        return this.cardSize.y / t_height;
    }

    RectTransform CreateCard(CardData _card)
    {
        var t_rt = this.cardPrefab != null ? CreateFromPrefab(_card) : CreateFromArt(_card);
        if (t_rt == null) return null;

        m_cards.Add(t_rt.gameObject);
        return t_rt;
    }

    RectTransform CreateFromPrefab(CardData _card)
    {
        var t_view = Instantiate(this.cardPrefab);
        t_view.Bind(_card, true);       // 이미 소유가 확정된 카드다(개봉 시점에 Grant 완료).
        BlockRaycast(t_view.gameObject);
        return (RectTransform)t_view.transform;
    }

    // 프리팹 미배선 폴백: 카드 아트 한 장으로도 "무엇이 날아갔는지"는 전달된다.
    RectTransform CreateFromArt(CardData _card)
    {
        var t_sprite = ArtOf(_card);
        if (t_sprite == null) return null;

        var t_go = new GameObject("GainCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ((RectTransform)t_go.transform).sizeDelta = this.cardSize;

        var t_img = t_go.GetComponent<Image>();
        t_img.sprite = t_sprite;
        t_img.raycastTarget = false;
        t_img.preserveAspect = true;

        return (RectTransform)t_go.transform;
    }

    // 덱 배너(deckPreview)를 먼저 쓰는 건 날아가는 물건이 "카드 한 장"이라 배너 비율이 더 맞기 때문이다.
    // 없으면 카드 아트로 폴백하되 그 판단은 CardVisualRules 하나에 맡긴다(여기서 필드를 직접 적으면 드리프트).
    static Sprite ArtOf(CardData _card)
    {
        if (_card == null) return null;
        return _card.deckPreview != null ? _card.deckPreview : CardVisualRules.PickCardArt(_card);
    }

    // 날아가는 카드가 탭 터치를 가로채지 않게.
    static void BlockRaycast(GameObject _go)
    {
        var t_group = _go.GetComponent<CanvasGroup>();
        if (t_group == null) t_group = _go.AddComponent<CanvasGroup>();
        t_group.blocksRaycasts = false;
        t_group.interactable   = false;
    }

    void ClearCards()
    {
        for (int t_i = 0; t_i < m_cards.Count; t_i++)
        {
            if (m_cards[t_i] == null) continue;
            m_cards[t_i].transform.DOKill();
            Destroy(m_cards[t_i]);
        }
        m_cards.Clear();
    }
}
