using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 로비 컬렉션 탭의 카드 상세 오버레이(CardDetailOverlay.prefab 루트에 부착).
// 카드 타일을 길게 누르면 열리고, 누른 카드의 이름·체력·키워드·시너지를 채운다.
//
// 인게임 카드 정보창(PooledCardElement)과 달리 풀드 UI가 아니라 로비 씬에 직접 배치한다 —
// 로비 전용 풀스크린 한 장이라 Addressables("UIPrefab" 라벨) 등록까지 갈 이유가 없다(PackOpenOverlay와 같은 결).
//
// 표시 규칙은 복제하지 않는다: 카드 그림 한 장은 CardVisualView.Bind, 시너지 이름은 SynergyText,
// 키워드 아이콘·표시명·설명은 KeywordIconConfig가 정본이다.
public class CardDetailOverlayView : MonoBehaviour
{
    /// <summary>미소유 카드의 이름 자리. 카드 그림 자체는 CardVisualView가 실루엣으로 가린다.</summary>
    const string LockedName  = "???";
    /// <summary>미소유 카드의 수치 자리(체력).</summary>
    const string LockedValue = "?";

    [Header("배선")]
    [SerializeField] TMP_Text       titleText;       // 상단 카드 이름
    [SerializeField] CardVisualView cardView;        // CardArea 안의 CardUIView 인스턴스
    [SerializeField] TMP_Text       powerValueText;  // 체력 수치(프리팹 목업의 "파워" 행을 체력으로 쓴다)

    [Header("키워드 섹션")]
    [SerializeField] GameObject keywordSection;      // 칩이 0개면 통째로 숨긴다
    [SerializeField] Transform  keywordChipRoot;     // 칩이 깔리는 List 노드
    [SerializeField] TMP_Text   keywordDescText;     // 칩들의 설명을 줄바꿈으로 이어 붙인다

    [Header("시너지 섹션")]
    [SerializeField] GameObject synergySection;
    [SerializeField] Transform  synergyChipRoot;
    [SerializeField] TMP_Text   synergyDescText;

    [Header("공용")]
    // 키워드/시너지 칩 공용 프리팹. 인게임 정보창의 설명 행과 같은 컴포넌트를 쓰되,
    // 칩에는 설명 줄이 없으므로 프리팹의 explainText를 미배선으로 비워둔다(Init이 null 가드).
    [SerializeField] KeywordExplainItem chipPrefab;
    [SerializeField] KeywordIconConfig  keywordIconConfig;
    [SerializeField] Button             closeButton;
    [SerializeField] PopupTransition    transition = new PopupTransition();

    static CardDetailOverlayView s_instance;
    static bool s_missingWarned;

    /// <summary>_card의 상세를 띄운다. 오버레이가 씬에 없으면 경고 1회 후 무시.</summary>
    public static void Open(CardData _card)
    {
        if (_card == null) return;

        CardDetailOverlayView t_view = Resolve();
        if (t_view == null) return;

        t_view.Show(_card);
    }

    public static void Close()
    {
        // 열린 적이 없으면 닫을 것도 없다 — 여기서 Resolve를 돌려 경고를 띄울 이유가 없다.
        if (s_instance == null) return;
        s_instance.Hide();
    }

    /// <summary>카드 타일에 탭 → 상세 열기를 배선한다.
    /// 타일 프리팹에 LongPressDetector가 아직 안 붙어 있으면 조용히 넘어간다(배선 전 상태).
    ///
    /// 탭 판정을 이 컴포넌트에 맡기는 이유는 <see cref="LongPressDetector.OnTap"/> 주석 참고 —
    /// 도감/생산 타일은 ScrollRect 안에 있어서 스크롤 드래그가 클릭으로 새면 안 된다.</summary>
    public static void BindTile(CardVisualView _tile, CardData _card)
    {
        if (_tile == null || _card == null) return;

        LongPressDetector t_press = _tile.GetComponent<LongPressDetector>();
        if (t_press == null) return;

        // 대입(+= 아님) — 타일이 재사용·재바인딩돼도 이전 카드의 콜백이 겹쳐 남지 않는다(CardElement와 같은 관용구).
        t_press.OnTap = () => Open(_card);
    }

    // 오버레이는 씬에 **비활성**으로 배치된다. 비활성 오브젝트는 Awake가 돌지 않아
    // PackOpenOverlay식 Awake 싱글턴으로는 자신을 등록할 수 없다 → 첫 호출 때 비활성 포함으로 찾아 캐시한다.
    // 씬이 바뀌면 참조가 죽으므로 아래 null 검사에서 자연히 재탐색된다.
    static CardDetailOverlayView Resolve()
    {
        if (s_instance != null) return s_instance;

        s_instance = FindFirstObjectByType<CardDetailOverlayView>(FindObjectsInactive.Include);

        if (s_instance == null && !s_missingWarned)
        {
            s_missingWarned = true;
            Debug.LogError("[CardDetailOverlayView] 현재 씬에 카드 상세 오버레이가 배치되지 않았습니다 — 카드를 길게 눌러도 열리지 않습니다.");
        }

        return s_instance;
    }

    void Awake()
    {
        s_instance = this;

        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveAllListeners();
            this.closeButton.onClick.AddListener(Hide);
        }
    }

    void OnDisable()
    {
        // 퇴장 트윈이 완료 전에 잘렸으면(부모가 먼저 꺼짐) 여기서 마무리해야 다음 열기에 유령 프레임이 안 뜬다.
        this.transition.HandleDisabled(gameObject);
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    // 켜는 것이 먼저다 — 비활성으로 시작한 오브젝트는 이 시점에 Awake가 돌아 닫기 버튼 배선이 성립한다.
    void Show(CardData _card)
    {
        this.transition.SetVisible(gameObject, true);
        Apply(_card);
    }

    void Hide()
    {
        this.transition.SetVisible(gameObject, false);
    }

    void Apply(CardData _card)
    {
        bool t_owned = OwnershipManager.IsOwned(_card);

        // 그림·이름·체력·키워드 아이콘·잠김 오버레이는 도감 타일과 같은 컴포넌트에 그대로 위임한다.
        if (this.cardView != null) this.cardView.Bind(_card, t_owned);

        if (this.titleText != null)
            this.titleText.text = t_owned ? _card.displayName : LockedName;

        // CardData에 파워 필드가 없어 프리팹 목업의 "파워" 행을 체력으로 쓴다(라벨/아이콘은 프리팹 쪽 값).
        if (this.powerValueText != null)
            this.powerValueText.text = !t_owned          ? LockedValue
                                     : _card.bonusHp > 0 ? $"{_card.maxHp} (+{_card.bonusHp})"
                                                         : _card.maxHp.ToString();

        BuildKeywordSection(_card, t_owned);
        BuildSynergySection(_card, t_owned);
    }

    void BuildKeywordSection(CardData _card, bool _owned)
    {
        ClearChildren(this.keywordChipRoot);

        var t_lines = new List<string>();
        int t_chips = 0;

        if (_owned && this.keywordIconConfig != null && this.chipPrefab != null && this.keywordChipRoot != null)
        {
            // 판정 기준은 인게임 카드 정보창(CardElement)과 같은 keywords | explainKeywords —
            // 설명 전용 키워드까지 보여주는 것이 정보창의 규약이다(카드 타일의 아이콘 줄과는 목적이 다르다).
            CardKeyword t_all = _card.keywords | _card.explainKeywords;

            // 순회 순서 = CardKeyword 선언 순. 카드 타일 아이콘 줄(CardVisualRules.CollectKeywordIcons)과 같은 순서다.
            foreach (CardKeyword t_kw in (CardKeyword[])Enum.GetValues(typeof(CardKeyword)))
            {
                if (t_kw == CardKeyword.None) continue;
                if ((t_all & t_kw) == 0) continue;
                if (!this.keywordIconConfig.TryGetEntry(t_kw, out KeywordIconConfig.Entry t_entry)) continue;

                Instantiate(this.chipPrefab, this.keywordChipRoot).Init(t_entry.icon, t_entry.displayName, null);
                t_chips++;

                if (!string.IsNullOrEmpty(t_entry.explain)) t_lines.Add(t_entry.explain);
            }
        }

        ApplySection(this.keywordSection, this.keywordDescText, t_chips, t_lines);
    }

    void BuildSynergySection(CardData _card, bool _owned)
    {
        ClearChildren(this.synergyChipRoot);

        var t_lines = new List<string>();
        int t_chips = 0;

        if (_owned && _card.synergies != null && this.chipPrefab != null && this.synergyChipRoot != null)
        {
            var t_seen = new HashSet<SynergyData>();
            foreach (SynergyData t_syn in _card.synergies)
            {
                if (t_syn == null || !t_seen.Add(t_syn)) continue;   // 중복 나열 방어

                // 마지막 인자는 시너지 PNG 투명 여백 보정 — 없으면 키워드 칩 옆에서 혼자 작아 보인다.
                Instantiate(this.chipPrefab, this.synergyChipRoot)
                    .Init(t_syn.activeIcon, SynergyText.Name(t_syn), null, SynergyIconStrip.IconPadCompensation);
                t_chips++;

                if (!string.IsNullOrEmpty(t_syn.effectDescription)) t_lines.Add(t_syn.effectDescription);
            }
        }

        ApplySection(this.synergySection, this.synergyDescText, t_chips, t_lines);
    }

    // 칩이 하나도 없는 섹션은 통째로 숨긴다(미소유 카드는 두 섹션 모두 꺼진다).
    // 판정에 chipRoot.childCount를 쓰면 안 된다 — 방금 Destroy한 이전 칩이 이 프레임엔 아직 자식으로 남아 있다.
    static void ApplySection(GameObject _section, TMP_Text _desc, int _chipCount, List<string> _lines)
    {
        if (_desc    != null) _desc.text = string.Join("\n", _lines);
        if (_section != null) _section.SetActive(_chipCount > 0);
    }

    static void ClearChildren(Transform _root)
    {
        if (_root == null) return;

        for (int t_i = _root.childCount - 1; t_i >= 0; t_i--)
            Destroy(_root.GetChild(t_i).gameObject);
    }
}
