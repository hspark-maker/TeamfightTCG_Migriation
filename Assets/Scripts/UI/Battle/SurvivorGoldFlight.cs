using System;
using System.Collections.Generic;
using Coffee.UIEffects;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 이번 판에 데리고 나간 카드가 통째로 한 줄에 서고, 그중 살아남은 카드만 골드로 빨려드는 안무.
// "보상 = 남은 카드 수"라는 인과를 세는 리듬으로 보여주는 것이 목적이라, 등장과 흡수 사이에
// 반드시 멈추는 구간이 있다(몇 장인지 세어질 시간). UiGainBurst는 그 정지를 넣을 수 없어 궤적을 직접 짓는다.
//
// 전사한 카드를 같은 줄에 흑백으로 세우는 이유는 분모를 보여주기 위해서다 — 생존만 서면 "4장 받았다"까지만
// 읽히고 "6장 중 4장"이 안 읽힌다. 그래서 이 줄은 보상 표시가 아니라 그 판의 성적표다.
// 전사 카드는 흡수에 참여하지 않고(계단의 분모는 생존 장수 그대로), 흡수가 끝나면 페이드로 걷힌다 —
// 승리 화면의 마지막 그림이 시체로 남지 않게.
//
// MonoBehaviour가 아니라 뷰가 필드로 소유한다(RewardRevealFx·ScreenDimTint와 같은 계열).
[Serializable]
public class SurvivorGoldFlight
{
    // ⚠ 모든 값에 C# 이니셜라이저 기본값을 둔다 — 기존 프리팹 YAML에 이 필드가 없어도 살아남게.

    [Header("배선(전부 옵션)")]
    [Tooltip("타일이 날아다닐 레이어. 미배선이면 팝업 루트 맨 아래에 자동으로 만든다(다른 UI 위에 그려진다).")]
    [SerializeField] RectTransform layerOverride;

    [Tooltip("카드 줄이 서는 자리. 미배선이면 골드 아이콘에서 rowRise만큼 위.")]
    [SerializeField] RectTransform rowCenter;

    [Tooltip("날아갈 카드 프리팹(CardVisualView). 카드 생김새의 단일 진실원이라 여기로 그리는 쪽이 정본이다. "
           + "미배선이면 아래 아트+테두리로 흉내 낸다.")]
    [SerializeField] CardVisualView cardPrefab;

    [Tooltip("카드 테두리 스프라이트(아트 위에 얹힌다). cardPrefab 미배선일 때만 쓰는 폴백. "
           + "미배선이면 카드 아트 한 장만 날아간다.")]
    [SerializeField] Sprite tileFrame;

    [Header("배치")]
    [SerializeField] Vector2 tileSize = new Vector2(150f, 200f);
    [SerializeField] float tileSpacing = 24f;
    [Tooltip("rowCenter 미배선일 때 골드 아이콘 위로 띄우는 높이.")]
    [SerializeField] float rowRise = 260f;
    [Tooltip("줄 전체 폭의 상한. 넘치면 줄째로 축소한다(카드가 늘어도 화면 밖으로 안 나가게).")]
    [SerializeField] float maxRowWidth = 1000f;

    [Header("등장")]
    [SerializeField] float enterStagger = 0.06f;
    [SerializeField] float enterDuration = 0.18f;
    [Tooltip("카드가 이만큼 아래에서 떠오른다.")]
    [SerializeField] float enterRise = 90f;
    [Tooltip("등장 전체가 이 시간을 넘으면 간격을 접는다.")]
    [SerializeField] float maxEnterSpan = 0.45f;

    [Header("정지(세어지는 시간)")]
    [SerializeField] float holdBase = 0.3f;
    [SerializeField] float holdPerCard = 0.03f;

    [Header("흡수")]
    [Tooltip("한 장씩 빨려드는 간격. goldRollDuration보다 커야 수치가 한 칸씩 멈춰 읽힌다 — "
           + "작으면 이전 롤링이 중간값에서 끊겨 그냥 빠르게 오르는 숫자가 된다.")]
    [SerializeField] float flyStagger = 0.18f;
    [SerializeField] float flyDuration = 0.26f;
    [Tooltip("골드에 닿을 때의 배율 — 아이콘 안으로 삼켜지는 느낌.")]
    [SerializeField] float flyScale = 0.18f;
    [Tooltip("날아가는 동안 좌우로 기우는 각(도).")]
    [SerializeField] float flySpin = 12f;
    [Tooltip("흡수 전체가 이 시간을 넘으면 간격을 접는다. 실제로 접히기 시작하면 위 flyStagger 규약이 "
           + "깨지므로 goldRollDuration도 함께 줄여야 한다.")]
    [SerializeField] float maxFlySpan = 1f;

    [Header("전사 카드")]
    [Tooltip("전사 카드에 곱하는 색(흑백 위에 곱한다). 낮을수록 어둡다. 카드 위에 글자·배지는 얹지 않는다 — "
           + "장수는 줄 밖 라벨이 말한다.")]
    [SerializeField] Color fallenTint = new Color(0.42f, 0.42f, 0.48f, 1f);

    [Tooltip("생존이 다 빨려든 뒤 전사 카드가 사라지는 시간. 0이면 남긴 채로 끝난다.")]
    [SerializeField] float fallenFadeDuration = 0.2f;

    readonly List<GameObject> m_tiles = new List<GameObject>();
    RectTransform m_layer;   // 자동 생성한 레이어. 타일만 걷고 레이어는 재사용한다.

    public float EnterSpan(int _count) => Stagger(this.enterStagger, this.maxEnterSpan, _count) * Mathf.Max(0, _count - 1)
                                        + this.enterDuration;

    public float HoldDuration(int _count) => this.holdBase + this.holdPerCard * Mathf.Max(0, _count);

    public float FlySpan(int _count) => Stagger(this.flyStagger, this.maxFlySpan, _count) * Mathf.Max(0, _count - 1)
                                      + this.flyDuration;

    /// <summary>
    /// 등장 → 정지 → 순차 흡수 시퀀스를 만들어 돌려준다(재생은 호출자 시퀀스에 맡긴다).
    /// 줄은 <paramref name="_cards"/>(생존, 왼쪽) + <paramref name="_fallen"/>(전사, 오른쪽) 순으로 선다 —
    /// 흡수가 왼→오로 진행되므로 앞에서부터 착착 빠지고 오른쪽에 잃은 만큼이 남는다.
    /// _onArrived(도착 장수, <b>생존</b> 장수)는 카드가 목적지에 닿을 때마다 — 골드 계단을 여기 맞문다.
    /// 전사 카드는 이 분모에 들어가지 않는다(보상을 만든 것은 생존뿐이다).
    /// _onEachArrived는 같은 순간의 화면 반응(아이콘 펀치)용.
    /// 날릴 것이 없거나 레이어를 확보하지 못하면 null — 호출자가 이 축을 통째로 건너뛴다.
    /// </summary>
    public Sequence Build(IReadOnlyList<CardData> _cards, IReadOnlyList<CardData> _fallen,
                          RectTransform _root, RectTransform _target,
                          Action<int, int> _onArrived, Action _onEachArrived = null)
    {
        Reset();

        int t_live  = _cards  != null ? _cards.Count  : 0;
        int t_dead  = _fallen != null ? _fallen.Count : 0;
        int t_count = t_live + t_dead;

        // 생존이 없으면 축 자체가 없다 — 시체만 선 줄은 승리 화면이 아니다(호출자가 코인 폴백으로 간다).
        if (t_live <= 0 || _root == null || _target == null) return null;

        RectTransform t_layer = EnsureLayer(_root);
        if (t_layer == null) return null;

        Vector2 t_to  = UiGainBurst.ToLayerLocal(t_layer, _target);
        Vector2 t_row = this.rowCenter != null
                      ? UiGainBurst.ToLayerLocal(t_layer, this.rowCenter)
                      : t_to + Vector2.up * this.rowRise;

        // 줄이 화면을 넘지 않게 줄째로 축소한다 — 칸마다 크기를 다르게 하면 "장수"가 아니라 "크기"가 읽힌다.
        // 배치는 언제나 tileSize 기준이다(프리팹을 써도 화면에 보이는 크기는 같게 맞춘다 → RestScale).
        // 폭 계산의 분모는 전사까지 포함한 전체 장수다 — 생존만 세면 덱이 반쯤 죽은 판에서 줄이 화면을 넘는다.
        float t_span  = this.tileSize.x * t_count + this.tileSpacing * Mathf.Max(0, t_count - 1);
        float t_scale = t_span > this.maxRowWidth && t_span > 0f ? this.maxRowWidth / t_span : 1f;
        float t_rest  = t_scale * RestScale();
        float t_step  = (this.tileSize.x + this.tileSpacing) * t_scale;
        float t_left  = t_row.x - t_step * (t_count - 1) * 0.5f;

        // 등장·정지는 줄 전체(전사 포함)를 기준으로, 흡수만 생존 장수를 기준으로 잰다.
        float t_enterStagger = Stagger(this.enterStagger, this.maxEnterSpan, t_count);
        float t_flyStagger   = Stagger(this.flyStagger, this.maxFlySpan, t_live);
        float t_flyStart     = EnterSpan(t_count) + HoldDuration(t_count);
        float t_fadeAt       = t_flyStart + FlySpan(t_live);

        var t_seq = DOTween.Sequence();

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            bool     t_alive = t_i < t_live;
            CardData t_card  = t_alive ? _cards[t_i] : _fallen[t_i - t_live];

            RectTransform t_tile = CreateTile(t_card, t_layer);
            if (t_tile == null) continue;

            Vector2 t_home = new Vector2(t_left + t_step * t_i, t_row.y);
            t_tile.anchoredPosition = t_home - Vector2.up * this.enterRise;
            t_tile.localScale       = Vector3.zero;

            float t_enterAt = t_enterStagger * t_i;
            t_seq.Insert(t_enterAt, t_tile.DOAnchorPos(t_home, this.enterDuration).SetEase(Ease.OutCubic));
            t_seq.Insert(t_enterAt, t_tile.DOScale(t_rest, this.enterDuration).SetEase(Ease.OutBack));

            if (!t_alive)
            {
                // 전사는 처음부터 흑백으로 뜬다 — 여기서 깨뜨리는 연출은 이미 전투에서 본 장면이고,
                // 매 판 보는 화면이라 한 박자도 더 늘리지 않는다.
                ApplyFallen(t_tile);

                if (this.fallenFadeDuration > 0f)
                    t_seq.Insert(t_fadeAt, EnsureGroup(t_tile.gameObject).DOFade(0f, this.fallenFadeDuration));

                continue;
            }

            float t_flyAt = t_flyStart + t_flyStagger * t_i;
            t_seq.Insert(t_flyAt, t_tile.DOAnchorPos(t_to, this.flyDuration).SetEase(Ease.InBack));
            t_seq.Insert(t_flyAt, t_tile.DOScale(t_rest * this.flyScale, this.flyDuration).SetEase(Ease.InQuad));

            if (!Mathf.Approximately(this.flySpin, 0f))
            {
                // 좌우 번갈아 — 난수를 쓰면 같은 결과가 매번 다르게 보인다.
                float t_spin = this.flySpin * (t_i % 2 == 0 ? 1f : -1f);
                t_seq.Insert(t_flyAt, t_tile.DOLocalRotate(new Vector3(0f, 0f, t_spin), this.flyDuration));
            }

            var t_item  = t_tile;   // 클로저가 루프 변수를 붙잡지 않게 복사.
            int t_index = t_i + 1;
            t_seq.InsertCallback(t_flyAt + this.flyDuration, () =>
            {
                if (t_item != null) t_item.gameObject.SetActive(false);
                _onArrived?.Invoke(t_index, t_live);
                _onEachArrived?.Invoke();
            });
        }

        // 정상 종료든 스킵(Complete)이든 여기서 타일을 걷는다.
        t_seq.AppendCallback(ClearTiles);
        return t_seq;
    }

    /// <summary>남은 타일을 걷는다(재진입·스킵·비활성 공용).</summary>
    public void Reset()
    {
        ClearTiles();
    }

    static float Stagger(float _base, float _maxSpan, int _count)
        => _count <= 1 ? 0f : Mathf.Min(_base, _maxSpan / (_count - 1));

    // CoinBurst·StarBurst와 같은 자리(팝업 루트 직하)에 둔다. 맨 뒤 자식이라 보상 줄 위에 그려진다.
    RectTransform EnsureLayer(RectTransform _root)
    {
        if (this.layerOverride != null) return this.layerOverride;
        if (this.m_layer != null) return this.m_layer;

        var t_go = new GameObject("SurvivorFlightLayer", typeof(RectTransform));
        var t_rt = (RectTransform)t_go.transform;
        t_rt.SetParent(_root, false);
        t_rt.anchorMin = Vector2.zero;
        t_rt.anchorMax = Vector2.one;
        t_rt.offsetMin = Vector2.zero;
        t_rt.offsetMax = Vector2.zero;
        t_rt.SetAsLastSibling();

        this.m_layer = t_rt;
        return t_rt;
    }

    // 프리팹은 원본 크기(420x558)를 유지한 채 배율만 줄인다 — sizeDelta를 강제하면 내부 비율 배치가 깨진다.
    // CardGainFlightEffect.RestScale과 같은 관용구. 프리팹을 안 쓰면 타일이 이미 tileSize라 1.
    float RestScale()
    {
        if (this.cardPrefab == null) return 1f;

        float t_height = ((RectTransform)this.cardPrefab.transform).sizeDelta.y;
        return t_height > 0f ? this.tileSize.y / t_height : 1f;
    }

    // 카드를 못 그려도 자리는 지킨다 — 리스트에서 빼면 계단의 분모가 어긋나 마지막 한 장이 남은 금액을 다 실어 나른다.
    RectTransform CreateTile(CardData _card, RectTransform _layer)
    {
        RectTransform t_rt = this.cardPrefab != null && _card != null
                           ? CreateFromPrefab(_card)
                           : CreateFromArt(_card);

        t_rt.SetParent(_layer, false);
        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = new Vector2(0.5f, 0.5f);

        this.m_tiles.Add(t_rt.gameObject);
        return t_rt;
    }

    // 전사 카드를 흑백 + 어둡게. UIEffect는 Graphic 하나에 붙는 컴포넌트라 타일 안의 그래픽 전부에 건다 —
    // CardUIView 프리팹에는 UIEffectReplica 저작이 없어서(PackCard와 다르다) 한 곳에만 걸면 그 노드만 빠진다.
    // 타일은 매번 새로 Instantiate하는 일회용이라 런타임 AddComponent가 안전하다(UiAdditive와 같은 관용구).
    void ApplyFallen(RectTransform _tile)
    {
        // 활성 노드만 — SetArtOnly가 이름·HP·잠김 판을 이미 껐고, 꺼진 그래픽에 거는 효과는 화면에 없다.
        Graphic[] t_graphics = _tile.GetComponentsInChildren<Graphic>();

        for (int t_i = 0; t_i < t_graphics.Length; t_i++)
        {
            var t_fx = t_graphics[t_i].GetComponent<UIEffect>();
            if (t_fx == null) t_fx = t_graphics[t_i].gameObject.AddComponent<UIEffect>();

            t_fx.toneFilter     = ToneFilter.Grayscale;
            t_fx.toneIntensity  = 1f;
            t_fx.colorFilter    = ColorFilter.Multiply;
            t_fx.color          = this.fallenTint;
            t_fx.colorIntensity = 1f;
        }
    }

    static CanvasGroup EnsureGroup(GameObject _go)
    {
        var t_group = _go.GetComponent<CanvasGroup>();
        return t_group != null ? t_group : _go.AddComponent<CanvasGroup>();
    }

    // 카드 생김새의 정본 경로. 이 줄은 장수를 세는 물건이라 이름·HP는 잔글씨로 뭉갠다 →
    // 아트와 프레임만 남긴다(SetArtOnly는 값만 세우므로 Bind가 뒤에 와야 실제로 반영된다).
    RectTransform CreateFromPrefab(CardData _card)
    {
        var t_view = UnityEngine.Object.Instantiate(this.cardPrefab);
        t_view.SetArtOnly(true);
        t_view.Bind(_card, true);   // 내가 전투에 들고 나온 카드다 — 소유는 확정.

        // 팝업의 전체화면 터치(스킵·메인 이동)를 날아가는 카드가 가로채지 않게.
        CanvasGroup t_group = EnsureGroup(t_view.gameObject);
        t_group.blocksRaycasts = false;
        t_group.interactable   = false;

        return (RectTransform)t_view.transform;
    }

    // 프리팹 미배선 폴백: 아트 한 장 + 테두리로 카드를 흉내 낸다.
    RectTransform CreateFromArt(CardData _card)
    {
        var t_go = new GameObject("SurvivorTile", typeof(RectTransform));
        var t_rt = (RectTransform)t_go.transform;
        t_rt.sizeDelta = this.tileSize;

        Sprite t_art = CardVisualRules.PickCardArt(_card);
        if (t_art != null) AddImage(t_go, t_art, this.tileSize);

        if (this.tileFrame != null)
        {
            // 테두리는 아트 위에 얹힌다(카드 프리팹과 같은 순서) — 뒤에 깔면 아트에 가려 안 보인다.
            GameObject t_frameHost = t_art != null ? NewChild(t_rt) : t_go;
            AddImage(t_frameHost, this.tileFrame, this.tileSize);
        }

        return t_rt;
    }

    GameObject NewChild(RectTransform _parent)
    {
        var t_go = new GameObject("Frame", typeof(RectTransform));
        var t_rt = (RectTransform)t_go.transform;
        t_rt.SetParent(_parent, false);
        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = new Vector2(0.5f, 0.5f);
        return t_go;
    }

    static void AddImage(GameObject _go, Sprite _sprite, Vector2 _size)
    {
        ((RectTransform)_go.transform).sizeDelta = _size;

        var t_img = _go.AddComponent<Image>();
        t_img.sprite         = _sprite;
        t_img.raycastTarget  = false;   // 팝업의 전체화면 터치(스킵)를 가로채지 않게.
        t_img.preserveAspect = true;
    }

    void ClearTiles()
    {
        for (int t_i = 0; t_i < this.m_tiles.Count; t_i++)
        {
            if (this.m_tiles[t_i] == null) continue;

            this.m_tiles[t_i].transform.DOKill();

            // 전사 페이드는 트랜스폼이 아니라 CanvasGroup을 타깃으로 잡는다 — 위 한 줄로는 안 죽는다.
            var t_group = this.m_tiles[t_i].GetComponent<CanvasGroup>();
            if (t_group != null) t_group.DOKill();

            UnityEngine.Object.Destroy(this.m_tiles[t_i]);
        }
        this.m_tiles.Clear();
    }
}
