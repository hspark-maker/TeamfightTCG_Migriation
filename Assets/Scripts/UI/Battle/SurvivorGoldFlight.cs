using System;
using System.Collections.Generic;
using Coffee.UIEffects;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 이번 판에 데리고 나간 카드가 통째로 한 줄에 서고, 그중 살아남은 카드만 골드로 빨려드는 안무.
// "보상 = 남은 카드 수"라는 인과를 보여주는 것이 목적이라, 등장과 흡수 사이에 반드시 멈추는 구간이
// 있다(몇 장인지 세어질 시간). UiGainBurst는 그 정지를 넣을 수 없어 궤적을 직접 짓는다.
//
// 줄은 한가운데에 겹쳐 나타나 좌우로 밀려 자리를 잡는다. 최종 배치는 그냥 중앙 정렬 한 줄이고,
// 중앙에서 퍼지는 것은 등장의 몫이다 — 왼쪽부터 한 장씩 놓이면 "줄 세우기"로 읽히지만,
// 한가운데가 갈라지면 한 덩어리가 펼쳐지는 것으로 읽힌다.
//
// 전사한 카드를 같은 줄에 흑백으로 세우는 이유는 분모를 보여주기 위해서다 — 생존만 서면 "4장 받았다"까지만
// 읽히고 "6장 중 4장"이 안 읽힌다. 그래서 이 줄은 보상 표시가 아니라 그 판의 성적표다.
// 생존은 왼쪽부터 한 장씩 시차를 두고 빨려들고, 닿을 때마다 골드 수치가 그만큼 오른다 — 랭크 줄이
// 별 하나하나에 맞춰 계단으로 오르는데 골드만 한 박에 확정값으로 뛰면 같은 화면에서 리듬이 어긋나고,
// 무엇보다 그 숫자가 어느 카드에서 왔는지 세어지지 않는다. 시차 전체는 flyStagger·maxFlySpan이
// 가두므로 장수가 늘어도 결과 화면이 그만큼 길어지지는 않는다.
// 반면 파괴는 여전히 한 박이다 — 첫 흡수가 시작되는 순간 오른쪽 무리 전체가 함께 무너진다.
// 잃은 것까지 한 장씩 세면 승리 화면이 손실을 헤아리는 시간이 된다.
// 승리 화면의 마지막 그림이 시체로 남지 않게. 조용히 페이드로 걷히면 "지워졌다"로 읽히므로,
// 잃은 것은 잃은 것처럼 무너뜨린다(UiCrumble). 전투의 사망 연출(흰 플래시 → 부풀었다 터짐)과 일부러 다른
// 언어를 쓴다 — 같은 그림을 두 번 보여주면 결과 화면이 전투의 재방송이 된다.
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
    [Tooltip("한가운데에서 바깥으로 한 겹씩 벌어지는 간격. 0이면 줄 전체가 한 박에 펼쳐진다.")]
    [SerializeField] float enterStagger = 0.06f;
    [SerializeField] float enterDuration = 0.18f;
    [Tooltip("등장 전체가 이 시간을 넘으면 간격을 접는다.")]
    [SerializeField] float maxEnterSpan = 0.45f;

    [Header("정지(세어지는 시간)")]
    [SerializeField] float holdBase = 0.3f;
    [SerializeField] float holdPerCard = 0.03f;

    [Header("흡수")]
    [Tooltip("카드 한 장이 골드까지 날아가는 시간. 장마다 아래 간격만큼 어긋나 출발하므로, 골드 수치는 "
           + "닿는 장수만큼 계단으로 오른다 — GameResultPopup의 goldRollDuration이 이 시간보다 많이 "
           + "짧으면 숫자가 카드보다 먼저 도착해 인과가 끊긴다.")]
    [SerializeField] float flyDuration = 0.26f;

    [Tooltip("생존 카드가 왼쪽부터 한 장씩 어긋나 출발하는 간격. 0이면 전체가 한 박에 빨려들고 "
           + "골드 수치도 계단 없이 한 번에 확정값까지 굴러간다(옛 동작).")]
    [SerializeField] float flyStagger = 0.06f;

    [Tooltip("시차 전체의 상한. 넘치면 간격을 접는다 — 생존이 많은 판에서 결과 화면이 장수만큼 길어지지 않게.")]
    [SerializeField] float maxFlySpan = 0.36f;
    [Tooltip("골드에 닿을 때의 배율 — 아이콘 안으로 삼켜지는 느낌.")]
    [SerializeField] float flyScale = 0.18f;
    [Tooltip("날아가는 동안 좌우로 기우는 각(도).")]
    [SerializeField] float flySpin = 12f;

    [Header("전사 카드")]
    [Tooltip("전사 카드에 곱하는 색(흑백 위에 곱한다). 낮을수록 어둡다. 카드 위에 글자·배지는 얹지 않는다 — "
           + "장수는 줄 밖 라벨이 말한다.")]
    [SerializeField] Color fallenTint = new Color(0.42f, 0.42f, 0.48f, 1f);

    [Tooltip("전사 카드가 사라지는 시간. 0이면 남긴 채로 끝난다. 아래 파괴 시간이 0일 때만 쓰는 폴백이며, "
           + "파괴와 같은 자리에서 돈다 — 흡수가 시작되는 순간 함께 걷힌다.")]
    [SerializeField] float fallenFadeDuration = 0.2f;

    [Header("전사 카드 파괴")]
    [Tooltip("전사 카드가 아래부터 삭아 부서지는 시간. 0이면 파괴 없이 위의 알파 페이드로 걷힌다. "
           + "전사 전체가 한 박에 함께 부서지고 흡수와 같은 순간에 시작하므로, flyDuration보다 짧으면 "
           + "결과 화면이 길어지지 않는다 — 그보다 길어지는 순간부터는 이 값이 곧 결과 화면 길이다.")]
    [SerializeField] float crumbleDuration = 0.35f;

    [Tooltip("삭는 경계에 이는 빛. ⚠ 따뜻한 색을 넣으면 '불타 없어진다'로 읽혀 강화 연출(잉걸→백열)과 섞인다 — "
           + "차가운 색이어야 '삭아 부서진다'로 읽힌다.")]
    [SerializeField] Color crumbleEdgeColor = new Color(0.62f, 0.72f, 0.95f, 1f);

    [Tooltip("삭는 경계의 폭(0~1). 키우면 빛나는 띠가 두꺼워지고, 0에 가까우면 빛 없이 툭툭 끊겨 사라진다.")]
    [Range(0f, 1f)]
    [SerializeField] float crumbleEdgeWidth = 0.2f;

    [Tooltip("부서지며 내려앉는 거리(px). 삭는 그림만으로는 '지워진다'에 가깝다 — 무게가 있어야 무너짐이 된다.")]
    [SerializeField] float crumbleSink = 8f;

    [Tooltip("부서지며 기우는 각(도). 좌우 번갈아 — 난수를 쓰면 같은 결과가 매번 다르게 보인다.")]
    [SerializeField] float crumbleTilt = 3f;

    readonly List<GameObject> m_tiles = new List<GameObject>();
    RectTransform m_layer;   // 자동 생성한 레이어. 타일만 걷고 레이어는 재사용한다.

    float EnterSpan(int _count) => EnterStagger(_count) * EnterSteps(_count) + this.enterDuration;

    float HoldDuration(int _count) => this.holdBase + this.holdPerCard * Mathf.Max(0, _count);

    /// <summary>
    /// 등장 → 정지 → 흡수 시퀀스를 만들어 돌려준다(재생은 호출자 시퀀스에 맡긴다).
    /// 줄은 <paramref name="_cards"/>(생존, 왼쪽) + <paramref name="_fallen"/>(전사, 오른쪽) 순으로 서고,
    /// 한가운데에 겹쳐 나타나 좌우로 밀려 자리를 잡는다.
    /// 생존은 왼쪽부터 한 장씩 어긋나 골드로 빨려들고, 전사는 첫 흡수와 같은 순간에 통째로 부서진다.
    /// _onArrived(도착 장수, <b>생존</b> 장수)는 생존 한 장이 목적지에 닿을 때마다 온다 —
    /// 골드가 랭크 줄처럼 계단으로 오르고, 마지막 한 장에서 확정값에 안착한다.
    /// 전사 카드는 이 분모에 들어가지 않는다(보상을 만든 것은 생존뿐이다).
    /// _onEachArrived는 그때마다의 화면 반응(아이콘 펀치)용.
    /// 날릴 것이 없거나 레이어를 확보하지 못하면 null — 호출자가 이 축을 통째로 건너뛴다.
    /// </summary>
    public Sequence Build(IReadOnlyList<int> _cards, IReadOnlyList<int> _fallen,
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

        float t_enterStagger = EnterStagger(t_count);

        // 생존은 여기서부터 한 장씩 어긋나 출발하고, 파괴는 이 한 시각에 통째로 벌어진다.
        float t_flyStagger = FlyStagger(t_live);
        float t_flyStart   = EnterSpan(t_count) + HoldDuration(t_count);

        var t_seq = DOTween.Sequence();

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            bool     t_alive = t_i < t_live;
            int t_card  = t_alive ? _cards[t_i] : _fallen[t_i - t_live];

            RectTransform t_tile = CreateTile(t_card, t_layer);
            if (t_tile == null) continue;

            Vector2 t_home = new Vector2(t_left + t_step * t_i, t_row.y);

            // 한가운데에 겹쳐서 나타나 자기 자리로 밀려난다 — 가운데 카드는 제자리에서 커지고 바깥일수록 멀리 간다.
            // 위아래로도 움직이면 방향이 둘이 되어 좌우로 갈라지는 그림이 흐려진다.
            t_tile.anchoredPosition = new Vector2(t_row.x, t_home.y);
            t_tile.localScale       = Vector3.zero;

            float t_enterAt = t_enterStagger * CenterRank(t_i, t_count);
            t_seq.Insert(t_enterAt, t_tile.DOAnchorPos(t_home, this.enterDuration).SetEase(Ease.OutCubic));
            t_seq.Insert(t_enterAt, t_tile.DOScale(t_rest, this.enterDuration).SetEase(Ease.OutBack));

            if (!t_alive)
            {
                // 전사는 처음부터 흑백으로 뜬다 — 줄이 한눈에 성적표로 읽히려면 생존·전사 구분이
                // 등장 순간부터 서 있어야 한다. 파괴는 등장이 아니라 퇴장의 몫이다(아래).
                UIEffect[] t_fx = ApplyFallen(t_tile);

                if (this.crumbleDuration > 0f && t_fx.Length > 0)
                {
                    // 생존이 빨려드는 바로 그 순간, 전사 전체가 함께.
                    t_seq.Insert(t_flyStart, UiCrumble.BuildTween(t_fx, this.crumbleDuration));

                    // 삭는 것과 같은 시간 안에서 내려앉고 기운다 — 무너짐은 한 사건이라 축을 나누지 않는다.
                    if (!Mathf.Approximately(this.crumbleSink, 0f))
                        t_seq.Insert(t_flyStart, t_tile.DOAnchorPosY(t_home.y - this.crumbleSink, this.crumbleDuration)
                                                       .SetEase(Ease.InQuad));

                    if (!Mathf.Approximately(this.crumbleTilt, 0f))
                    {
                        // 좌우 번갈아 — 한 박에 다 부서지므로 각이 같으면 줄이 통째로 기운 판처럼 보인다.
                        float t_tilt = this.crumbleTilt * (t_i % 2 == 0 ? 1f : -1f);
                        t_seq.Insert(t_flyStart, t_tile.DOLocalRotate(new Vector3(0f, 0f, t_tilt), this.crumbleDuration)
                                                       .SetEase(Ease.InOutSine));
                    }
                }
                else if (this.fallenFadeDuration > 0f)
                {
                    t_seq.Insert(t_flyStart, EnsureGroup(t_tile.gameObject).DOFade(0f, this.fallenFadeDuration));
                }

                continue;
            }

            // 왼쪽에 선 카드부터 차례로 떠난다 — 줄에 선 순서와 빨려드는 순서가 같아야 몇 장째인지 세어진다.
            float t_liftAt = t_flyStart + t_flyStagger * t_i;
            float t_landAt = t_liftAt + this.flyDuration;

            t_seq.Insert(t_liftAt, t_tile.DOAnchorPos(t_to, this.flyDuration).SetEase(Ease.InBack));
            t_seq.Insert(t_liftAt, t_tile.DOScale(t_rest * this.flyScale, this.flyDuration).SetEase(Ease.InQuad));

            if (!Mathf.Approximately(this.flySpin, 0f))
            {
                // 좌우 번갈아 — 난수를 쓰면 같은 결과가 매번 다르게 보인다.
                float t_spin = this.flySpin * (t_i % 2 == 0 ? 1f : -1f);
                t_seq.Insert(t_liftAt, t_tile.DOLocalRotate(new Vector3(0f, 0f, t_spin), this.flyDuration));
            }

            var t_item    = t_tile;   // 클로저가 루프 변수를 붙잡지 않게 복사.
            int t_ordinal = t_i + 1;  // 생존 기준 몇 장째가 닿았는가(마지막 장에서 확정값에 안착한다).
            t_seq.InsertCallback(t_landAt, () =>
            {
                if (t_item != null) t_item.gameObject.SetActive(false);

                _onArrived?.Invoke(t_ordinal, t_live);
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

    // 한가운데에서 바깥으로 몇 겹째인가. 짝수 장이면 가운데 두 장이 같은 0겹이다.
    static int CenterRank(int _i, int _count)
    {
        float t_mid = (_count - 1) * 0.5f;
        return Mathf.RoundToInt(Mathf.Abs(_i - t_mid) - (_count % 2 == 0 ? 0.5f : 0f));
    }

    // 가장 바깥 겹의 번호 = 등장이 벌어지는 단계 수(한 겹이 좌우 두 장을 함께 세운다).
    static int EnterSteps(int _count) => Mathf.Max(0, (_count - 1) / 2);

    // 겹이 늘면 간격을 접어 등장 전체를 maxEnterSpan 안에 가둔다.
    float EnterStagger(int _count)
    {
        int t_steps = EnterSteps(_count);
        return t_steps <= 0 ? 0f : Mathf.Min(this.enterStagger, this.maxEnterSpan / t_steps);
    }

    // 생존이 늘면 간격을 접어 흡수 전체를 maxFlySpan 안에 가둔다(등장의 maxEnterSpan과 같은 규약).
    // 등장과 달리 한 장이 한 단계다 — 좌우로 갈라지는 것이 아니라 왼쪽부터 차례로 떠나기 때문이다.
    float FlyStagger(int _live)
    {
        int t_steps = Mathf.Max(0, _live - 1);
        return t_steps <= 0 ? 0f : Mathf.Min(this.flyStagger, this.maxFlySpan / t_steps);
    }

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
    RectTransform CreateTile(int _card, RectTransform _layer)
    {
        RectTransform t_rt = this.cardPrefab != null && _card > 0
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
    // 돌려주는 묶음은 부르는 쪽이 파괴 축을 한 값으로 밀기 위한 것이다 — 조각마다 따로 밀면 카드가 갈라져 부서진다.
    UIEffect[] ApplyFallen(RectTransform _tile)
    {
        // 활성 노드만 — SetArtOnly가 이름·HP·잠김 판을 이미 껐고, 꺼진 그래픽에 거는 효과는 화면에 없다.
        Graphic[] t_graphics = _tile.GetComponentsInChildren<Graphic>();
        var       t_effects  = new UIEffect[t_graphics.Length];

        for (int t_i = 0; t_i < t_graphics.Length; t_i++)
        {
            var t_fx = t_graphics[t_i].GetComponent<UIEffect>();
            if (t_fx == null) t_fx = t_graphics[t_i].gameObject.AddComponent<UIEffect>();

            t_fx.toneFilter     = ToneFilter.Grayscale;
            t_fx.toneIntensity  = 1f;
            t_fx.colorFilter    = ColorFilter.Multiply;
            t_fx.color          = this.fallenTint;
            t_fx.colorIntensity = 1f;

            // 타일 뿌리를 공유해야 아트·프레임이 카드 한 장으로 부서진다(UiCrumble 머리말 참고).
            UiCrumble.Arm(t_fx, _tile, this.crumbleEdgeColor, this.crumbleEdgeWidth);

            t_effects[t_i] = t_fx;
        }

        return t_effects;
    }

    static CanvasGroup EnsureGroup(GameObject _go)
    {
        var t_group = _go.GetComponent<CanvasGroup>();
        return t_group != null ? t_group : _go.AddComponent<CanvasGroup>();
    }

    // 카드 생김새의 정본 경로. 이 줄은 장수를 세는 물건이라 이름·HP는 잔글씨로 뭉갠다 →
    // 아트와 프레임만 남긴다(SetArtOnly는 값만 세우므로 Bind가 뒤에 와야 실제로 반영된다).
    RectTransform CreateFromPrefab(int _card)
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
    RectTransform CreateFromArt(int _card)
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
