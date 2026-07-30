using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 아웃게임(uGUI) 카드 한 장의 비주얼 단일 진실원.
// 도감 그리드 타일 / 도감 생산행 타일 / 덱편집 컬렉션 타일 / 덱편집 드래그 고스트가 모두 이 컴포넌트를 공유한다.
//
// 인게임 CardView는 월드스페이스 SpriteRenderer, 로비는 ScreenSpaceOverlay uGUI라 프리팹을 복사할 수 없다.
// 그래서 렌더러만 Image/TMP_Text로 바꾸고, "무엇을 어떤 순서로 몇 개까지 보이는가"라는 규칙은 복제하지 않고
// CardVisualRules(인게임 CardView와 동일 호출)에 위임한다. 규칙이 갈라지면 로비와 전투의 카드가 달라 보인다.
// 배치도 인게임 좌표를 카드 크기 대비 비율로 환산해 그대로 옮긴다(아래 "인게임 좌표 환산값" 참고).
// 여기 남은 것은 소유여부에 따른 은닉 같은 아웃게임 고유 표현뿐이다.
//
// 소유 = 정상 표시, 미소유 = 잠김 오버레이(어둡게+실루엣) + 정보(이름/HP/키워드/시너지) 은닉.
// null 카드(부분행 빈칸)는 타일 자체를 숨긴다.
public class CardVisualView : MonoBehaviour
{
    [SerializeField] Image portrait;          // 카드 아트
    [SerializeField] TMP_Text nameText;       // 카드 이름(미소유 시 숨김)
    [SerializeField] GameObject lockOverlay;  // 미소유 시 활성(어두운 오버레이 + 잠김 표시)

    [Header("인게임 미러 요소")]
    [SerializeField] Image      frame;            // 카드 프레임(인게임과 동일 스프라이트). 카드별 데이터가 아니라 프리팹 고정값.
    [SerializeField] GameObject hpPanel;          // HP 표시 묶음(우상단)
    [SerializeField] TMP_Text   hpText;           // maxHp
    [SerializeField] TMP_Text   bonusHpText;      // bonusHp > 0 일 때만 "+N"
    [SerializeField] Transform  keywordIconRoot;  // 키워드 아이콘 부모. 카드 rect 전체를 덮는 빈 컨테이너(배치는 코드가 앵커로).
    [SerializeField] Transform  synergyBadgeRoot; // 시너지 배지 부모. 인게임처럼 그 자리를 키워드가 쓰면 미배선(null)이라 배지는 안 그려진다.
    [SerializeField] CardKeywordIconView   keywordIconPrefab;
    [SerializeField] CardSynergyBadgeView  synergyBadgePrefab;
    [SerializeField] KeywordIconConfig     keywordIconConfig;

    // 프레임에 얹는 키워드별 장식 이미지. 인게임 CardView.keywordFrames와 같은 (키워드 → 오브젝트) 배선이며
    // 판정도 같은 CardVisualRules.TraitKeywords를 쓴다 — 기준이 갈리면 전투에선 뜨는 장식이 로비에선 안 뜬다.
    // 이름 매칭이 아니라 참조 배선인 이유도 인게임과 동일: 오브젝트 이름을 바꿔도 조용히 꺼지지 않게.
    [System.Serializable]
    public struct KeywordFrame
    {
        public CardKeyword keyword;
        public GameObject  overlay;
    }
    [SerializeField] KeywordFrame[] keywordFrames;

    [Header("표시 옵션")]
    // 작은 타일에서 요소를 끄기 위한 프리팹별 스위치. 소비자 코드는 Bind만 호출하고
    // "무엇을 보일지"는 프리팹이 결정한다(호출부에 표시 분기를 만들지 않기 위함).
    [SerializeField] bool showName      = true;
    [SerializeField] bool showHp        = true;
    [SerializeField] bool showKeywords  = true;
    [SerializeField] bool showSynergies = true;
    // 표시 최대 배지 수. 기본값은 인게임과 같은 코드 상수 하나에서 온다(각자 3을 적어두면 한쪽만 바뀌어도 조용히 갈라진다).
    [SerializeField] int  synergyMaxBadges = CardVisualRules.MaxSynergyBadges;

    // ── 인게임 좌표를 uGUI로 옮기는 환산값 ──────────────────────────────────
    //
    // 정적인 요소(아트·프레임·프레임 장식·HP)는 프리팹에 정규화 앵커로 박아뒀다. 코드가 필요한 건
    // 런타임 생성물(키워드 아이콘)의 자리와, 카드 rect 크기에 따라 달라지는 폰트 크기 둘뿐이다.
    //
    // 카드 셀 크기가 화면마다 다르다(도감 290x386, 덱편집 270x360, 팩개봉 700x930…). 픽셀 상수를 박으면
    // 한 곳에서만 맞고 나머지는 어긋나므로, 전부 "인게임 카드 크기 대비 비율"로 계산한다.

    /// <summary>인게임 카드 한 장의 월드 크기 = Frame.png(1024x1361 @PPU100) × CardView Frame localScale 0.233245.
    /// 인게임 프레임 스케일이 바뀌면 여기도 같이 바꿔야 로비 카드가 따라간다.</summary>
    const float IngameCardWidth  = 2.388429f;
    const float IngameCardHeight = 3.174464f;

    // TextMeshPro(월드)는 렌더 시 fontSize에 0.1을 곱하고 TextMeshProUGUI는 그대로 쓴다(m_isOrthographic 차이).
    // 그래서 인게임 fontSize를 uGUI로 옮기려면 0.1 × (카드 rect 높이 / 인게임 카드 높이)를 곱한다.
    const float WorldFontToUnit  = 0.1f;
    const float IngameHpFontSize      = 5f;   // CardView HPText
    const float IngameBonusHpFontSize = 3f;   // CardView AdditionalHPText
    const float IngameNameFontSize    = 2f;   // CardView NameText

    // 키워드 아이콘 세로열. 인게임 CardView의 synergyBadgeXPos(-0.6) / YStart(-1) / YStep(-0.5)와
    // kewordIcon 크기(0.6x0.6)를 위 카드 크기로 나눈 값 = 카드 좌하단 기준 정규화 좌표.
    const float KeywordIconCenterX = 0.5f - 0.6f / IngameCardWidth;
    const float KeywordIconStartY  = 0.5f - 1.0f / IngameCardHeight;
    const float KeywordIconStepY   =      -0.5f / IngameCardHeight;
    const float KeywordIconWidth   =       0.6f / IngameCardWidth;
    const float KeywordIconHeight  =       0.6f / IngameCardHeight;

    // 카드 데이터·소유여부로 타일을 바인딩. _card가 null이면 빈칸으로 숨긴다.
    // 배선이 null인 필드는 조용히 건너뛴다 — 프리팹마다 일부 노드만 가질 수 있다(고스트/작은 타일).
    public void Bind(CardData _card, bool _owned)
    {
        if (_card == null)
        {
            gameObject.SetActive(false);
            return;
        }
        gameObject.SetActive(true);

        if (this.portrait != null)
        {
            // 아트 선택(폴백 체인)은 표시 규칙이라 CardVisualRules가 정본이다 — 소비자(덱편집 칸/개봉 카드)가
            // 늘어난 뒤 각자 폴백을 적어두면 같은 카드가 화면마다 다른 그림으로 뜬다.
            Sprite t_art = CardVisualRules.PickCardArt(_card);
            this.portrait.sprite  = t_art;
            this.portrait.enabled = t_art != null;
        }

        // 프레임은 카드별로 바뀌지 않는다. 스프라이트 미배선 시 흰 사각형이 뜨는 것만 막는다.
        if (this.frame != null) this.frame.enabled = this.frame.sprite != null;

        if (this.nameText != null)
        {
            // 미소유는 이름을 숨겨 실루엣만 노출.
            bool t_showName = _owned && this.showName;
            this.nameText.gameObject.SetActive(t_showName);
            if (t_showName) this.nameText.text = _card.displayName;   // 표시명 정본은 displayName(에셋 name 아님)
        }

        // 미소유는 실루엣만 노출하는 게 기존 의도 → 이름뿐 아니라 HP/키워드/시너지 같은 "정보"도 전부 숨긴다.
        SetHpDisplay(_card, _owned && this.showHp);
        RefreshKeywordIcons(_card, _owned && this.showKeywords);
        RefreshKeywordFrames(_card, _owned && this.showKeywords);
        RefreshSynergyBadges(_card, _owned && this.showSynergies);

        // 폰트 크기는 카드 rect에 비례한다 → 바인드 시점에 한 번 맞춘다(셀 크기가 화면마다 다르다).
        ApplyIngameFontScale();

        // 미소유 = 잠김 오버레이 on(아트를 어둡게 덮어 실루엣화).
        if (this.lockOverlay != null) this.lockOverlay.SetActive(!_owned);
    }

    // HP 표시. 인게임 CardView.SetHpDisplay 규약과 동일 — bonus는 값이 있을 때만 오브젝트를 켠다.
    // 아웃게임엔 전투 인스턴스(CardInstance.hp)가 없으므로 마스터 데이터의 maxHp를 그린다.
    void SetHpDisplay(CardData _card, bool _show)
    {
        if (this.hpPanel != null) this.hpPanel.SetActive(_show);

        if (this.hpText != null)
        {
            this.hpText.gameObject.SetActive(_show);
            if (_show) this.hpText.text = _card.maxHp.ToString();
        }

        if (this.bonusHpText != null)
        {
            bool t_hasBonus = _show && _card.bonusHp > 0;
            this.bonusHpText.gameObject.SetActive(t_hasBonus);
            if (t_hasBonus) this.bonusHpText.text = $"+{_card.bonusHp}";
        }
    }

    // 키워드 아이콘 갱신. 표시 대상·순서는 인게임과 같은 CardVisualRules 호출로 얻는다.
    // 아웃게임엔 런타임 부여 키워드(CardInstance.runtimeKeywords)가 없으므로 마스터 데이터의 keywords만 넘긴다.
    void RefreshKeywordIcons(CardData _card, bool _show)
    {
        if (this.keywordIconRoot == null) return;
        ClearChildren(this.keywordIconRoot);

        if (!_show || this.keywordIconPrefab == null || this.keywordIconConfig == null) return;

        // 아웃게임엔 전투 런타임 상태가 없다 → 상태 전용 키워드(무적·추가체력)는 애초에 제외.
        // 판정은 인게임 특성 줄과 같은 CardVisualRules.IconKeywords 하나. 로비/전투 표시가 갈리지 않는다.
        // (아이콘 줄 전용 제외분 = 표식. 프레임 장식은 아래 RefreshKeywordFrames가 TraitKeywords로 그대로 띄운다.)
        int t_index = 0;
        foreach (CardVisualRules.KeywordIcon t_entry in
                 CardVisualRules.CollectKeywordIcons(CardVisualRules.IconKeywords(_card), this.keywordIconConfig))
        {
            CardKeywordIconView t_view = Instantiate(this.keywordIconPrefab, this.keywordIconRoot);
            t_view.SetIcon(t_entry.Icon);
            PlaceKeywordIcon(t_view.transform as RectTransform, t_index++);
        }
    }

    // 인게임은 세로열 좌표(synergyBadgeXPos/YStart/YStep)로 아이콘을 직접 찍는다. uGUI 미러도 LayoutGroup에
    // 맡기지 않고 같은 좌표를 정규화 앵커로 옮긴다 — LayoutGroup은 간격·크기를 픽셀로 잡아서 카드 셀 크기가
    // 바뀌면(도감 386px vs 팩개봉 930px) 인게임과 비율이 어긋난다. 앵커는 부모 rect 비율이라 어긋나지 않는다.
    static void PlaceKeywordIcon(RectTransform _rect, int _index)
    {
        if (_rect == null) return;

        var t_center = new Vector2(KeywordIconCenterX, KeywordIconStartY + KeywordIconStepY * _index);
        var t_half   = new Vector2(KeywordIconWidth, KeywordIconHeight) * 0.5f;

        _rect.anchorMin        = t_center - t_half;
        _rect.anchorMax        = t_center + t_half;
        _rect.sizeDelta        = Vector2.zero;
        _rect.anchoredPosition = Vector2.zero;
        _rect.localScale       = Vector3.one;
    }

    // 프레임 키워드 장식(처형·도발·힐러·원거리·교활·무쌍·표식). 인게임 CardView.RefreshKeywordFrames와 같은 규약:
    // 기준은 TraitKeywords(아이콘 줄만 IconKeywords로 표식을 더 뺀다), 미소유/빈 카드는 전부 끈다(정보 은닉).
    void RefreshKeywordFrames(CardData _card, bool _show)
    {
        if (this.keywordFrames == null) return;

        CardKeyword t_keywords = _show ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;

        foreach (KeywordFrame t_frame in this.keywordFrames)
        {
            if (t_frame.overlay == null) continue;
            // None 배선은 항상 꺼짐 — HasFlag(None)은 늘 true라 그대로 두면 모든 카드에서 켜진다.
            bool t_on = t_frame.keyword != CardKeyword.None && (t_keywords & t_frame.keyword) != 0;
            t_frame.overlay.SetActive(t_on);
        }
    }

    // 카드 rect 높이에 비례해 폰트 크기를 인게임 값으로 환산한다. 프리팹에 픽셀 크기를 박아두면
    // 그 프리팹을 쓰는 셀 하나에서만 맞고 나머지(도감/덱편집/팩개봉이 서로 다른 셀 크기)는 어긋난다.
    void ApplyIngameFontScale()
    {
        if (!(this.transform is RectTransform t_rect)) return;

        float t_scale = t_rect.rect.height * WorldFontToUnit / IngameCardHeight;
        if (t_scale <= 0f) return;   // 레이아웃 전(높이 0)이면 건너뛴다 — 확정 후 아래 콜백이 다시 부른다.

        if (this.hpText      != null) this.hpText.fontSize      = IngameHpFontSize      * t_scale;
        if (this.bonusHpText != null) this.bonusHpText.fontSize = IngameBonusHpFontSize * t_scale;
        if (this.nameText    != null) this.nameText.fontSize    = IngameNameFontSize    * t_scale;
    }

    // 그리드 셀 크기가 Bind 이후에 확정되는 경우(GridLayoutGroup 첫 프레임)를 위해 rect가 바뀔 때마다 재적용.
    void OnRectTransformDimensionsChange() => ApplyIngameFontScale();

    // 시너지 배지 갱신. 표시 대상·순서는 인게임과 같은 CardVisualRules 호출로 얻는다.
    void RefreshSynergyBadges(CardData _card, bool _show)
    {
        if (this.synergyBadgeRoot == null) return;
        ClearChildren(this.synergyBadgeRoot);

        if (!_show || this.synergyBadgePrefab == null) return;

        // 아웃게임엔 전투 스냅샷(SynergyState)이 없어 활성 판정의 진실원이 없다 → null을 넘긴다.
        // 활성 판정은 전부 false가 되지만 requiredCount 내림차순 정렬은 그대로 성립한다
        // (GetBadgeRequiredCount가 스냅샷이 없으면 tiers 최고값으로 폴백) → 배지 세로 순서가 전투와 일치한다.
        List<SynergyData> t_tags = CardVisualRules.CollectSynergyBadges(_card.synergies, null, this.synergyMaxBadges);

        foreach (SynergyData t_syn in t_tags)
        {
            CardSynergyBadgeView t_badge = Instantiate(this.synergyBadgePrefab, this.synergyBadgeRoot);
            // 아이콘만은 활성(active=true)으로 그린다 — 도감/덱편집은 "이 카드가 가진 시너지" 소개가 목적이라
            // 전투 스냅샷이 없다는 이유로 전부 흐린 inactiveIcon을 보여줄 이유가 없다. 정렬만 인게임 규칙을 따른다.
            t_badge.Set(t_syn, true);
        }
    }

    // 재바인딩 시 이전 아이콘/배지를 제거. 인게임은 파괴 전 DOKill로 tween을 정리하지만
    // 아웃게임 타일은 CardAnimator 페이드 대상이 아니라 자식에 걸린 tween 자체가 없다 → DOKill 불필요.
    static void ClearChildren(Transform _root)
    {
        for (int t_i = _root.childCount - 1; t_i >= 0; t_i--)
            Destroy(_root.GetChild(t_i).gameObject);
    }
}
