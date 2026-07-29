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
// 여기 남은 것은 배치(LayoutGroup)·소유여부에 따른 은닉 같은 아웃게임 고유 표현뿐이다.
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
    [SerializeField] Transform  keywordIconRoot;  // 우하단, 키워드 아이콘 부모(정렬은 LayoutGroup 담당)
    [SerializeField] Transform  synergyBadgeRoot; // 좌하단, 시너지 배지 부모(정렬은 LayoutGroup 담당)
    [SerializeField] CardKeywordIconView   keywordIconPrefab;
    [SerializeField] CardSynergyBadgeView  synergyBadgePrefab;
    [SerializeField] KeywordIconConfig     keywordIconConfig;

    [Header("표시 옵션")]
    // 작은 타일에서 요소를 끄기 위한 프리팹별 스위치. 소비자 코드는 Bind만 호출하고
    // "무엇을 보일지"는 프리팹이 결정한다(호출부에 표시 분기를 만들지 않기 위함).
    [SerializeField] bool showName      = true;
    [SerializeField] bool showHp        = true;
    [SerializeField] bool showKeywords  = true;
    [SerializeField] bool showSynergies = true;
    // 표시 최대 배지 수. 기본값은 인게임과 같은 코드 상수 하나에서 온다(각자 3을 적어두면 한쪽만 바뀌어도 조용히 갈라진다).
    [SerializeField] int  synergyMaxBadges = CardVisualRules.MaxSynergyBadges;

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
        RefreshSynergyBadges(_card, _owned && this.showSynergies);

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
        // 판정은 인게임 특성 줄과 같은 CardVisualRules.TraitKeywords 하나. 로비/전투 표시가 갈리지 않는다.
        foreach (CardVisualRules.KeywordIcon t_entry in
                 CardVisualRules.CollectKeywordIcons(CardVisualRules.TraitKeywords(_card), this.keywordIconConfig))
        {
            CardKeywordIconView t_view = Instantiate(this.keywordIconPrefab, this.keywordIconRoot);
            t_view.SetIcon(t_entry.Icon);
        }
        // 인게임은 월드좌표 iconSpacing으로 직접 배치하지만, uGUI 미러는 배치를 keywordIconRoot의
        // LayoutGroup에 맡긴다(셀 크기·해상도가 바뀌어도 좌표 재계산이 필요 없다).
    }

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
