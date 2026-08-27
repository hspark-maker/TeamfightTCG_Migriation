using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 아이콘 옆에 뜨는 설명 팝업(풀드 UI). 키워드·시너지가 **같은 타입 하나**를 공유한다 —
/// 예전에는 KeywordExplainPopupUI / SynergyExplainPopupUI 두 벌이 같은 코드를 복붙하고 있었고,
/// 배치·수명 규칙이 갈릴 때마다 두 곳을 고쳐야 했다. 새 설명 종류가 늘어도 여기에 필드를 붙이지 말고
/// <see cref="ExplainPopupData"/>를 만들어 넘겨라 — 팝업은 도메인 타입을 모른다.
///
/// 프리팹은 Addressables "UIPrefab" 라벨이 있어야 UiPrefabCache가 색인한다.
/// 풀 키가 C# 타입이라 이 타입에 대응하는 프리팹은 ExplainPopup.prefab 하나뿐이다.
/// </summary>
public class ExplainPopupUI : PooledUIBase
{
    [SerializeField] Image    iconImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text explainText;
    [SerializeField] float    iconGap = 8f;

    RectTransform     m_anchor;
    bool              m_hasWorldAnchor;
    Vector3           m_worldAnchor;
    float             m_worldHalfWidth;
    Rect              m_lastSafeArea;
    Vector2Int        m_lastScreen;
    ScreenOrientation m_lastOrientation;

    public override void Initialization(UIData _data)
    {
        if (_data is not ExplainPopupData t_d) return;

        if (this.iconImage != null)
        {
            this.iconImage.sprite  = t_d.icon;
            this.iconImage.enabled = t_d.icon != null;
            // 아이콘 PNG 투명 여백 보정. 풀드라 이전 호출의 배율이 남으므로 항상 대입한다(기본 1).
            this.iconImage.rectTransform.localScale = Vector3.one * t_d.iconScale;
        }
        if (this.nameText    != null) this.nameText.text    = t_d.displayName;
        if (this.explainText != null) this.explainText.text = t_d.explain;

        // 배치 규칙은 PopupPlacer가 단독 소유(시너지 툴팁과 동일 동작).
        this.m_anchor         = t_d.iconRect;
        this.m_hasWorldAnchor = t_d.hasWorldAnchor;
        this.m_worldAnchor    = t_d.worldAnchor;
        this.m_worldHalfWidth = t_d.worldHalfWidth;
        Reposition();
    }

    void LateUpdate()
    {
        if (!this.isShow || !ScreenStateChanged()) return;
        Reposition();
    }

    void Reposition()
    {
        if (this.m_anchor != null)
            PopupPlacer.PlaceBesideAnchor((RectTransform)transform, this.m_anchor, this.iconGap);
        else if (this.m_hasWorldAnchor)
            PopupPlacer.PlaceBesideWorldPoint((RectTransform)transform,
                this.m_worldAnchor, this.m_worldHalfWidth, this.iconGap);

        this.m_lastSafeArea    = Screen.safeArea;
        this.m_lastScreen      = new Vector2Int(Screen.width, Screen.height);
        this.m_lastOrientation = Screen.orientation;
    }

    bool ScreenStateChanged()
        => Screen.safeArea != this.m_lastSafeArea
           || Screen.width != this.m_lastScreen.x || Screen.height != this.m_lastScreen.y
           || Screen.orientation != this.m_lastOrientation;

    public override void Show()
    {
        this.contents.SetActive(true);
        this.isShow = true;
    }

    public override void Hide()
    {
        this.contents.SetActive(false);
        this.isShow = false;
    }
}

/// <summary>팝업에 넘길 **완성된 표시 데이터**. 도메인 객체를 그대로 넣지 말고
/// <see cref="ForSynergy"/> 같은 팩토리에서 문자열로 바꿔 담는다 — 포맷 규칙이 팝업 안으로 새면
/// 같은 문구가 화면마다 달라진다.</summary>
public class ExplainPopupData : UIData
{
    public Sprite icon;
    public string displayName;
    public string explain;

    /// <summary>아이콘 여백 보정 배율. 1이면 프리팹 저작값 그대로.</summary>
    public float iconScale = 1f;

    /// <summary>uGUI 아이콘 옆에 띄울 때. 인게임 배지처럼 월드 오브젝트면 null로 두고 아래를 쓴다.</summary>
    public RectTransform iconRect;

    /// <summary>인게임 카드 위 시너지 배지처럼 월드 스페이스 대상 옆에 띄울 때 사용.</summary>
    public bool    hasWorldAnchor;
    public Vector3 worldAnchor;
    public float   worldHalfWidth = 0.2f;

    /// <summary>시너지 설명 데이터. 문구 포맷은 SynergyText가 단독 소유한다(덱 편성 툴팁과 공용).
    /// <paramref name="_ownedCount"/>가 음수면 덱 문맥 없음 — ●/○ 마커 없이 요구치만 나온다.</summary>
    public static ExplainPopupData ForSynergy(SynergyData _synergy, int _ownedCount = -1)
        => _synergy == null ? null : new ExplainPopupData
        {
            icon        = _synergy.activeIcon,
            displayName = SynergyText.Name(_synergy),
            explain     = SynergyText.Body(_synergy, _ownedCount),
            iconScale   = SynergyIconStrip.IconPadCompensation,
        };
}
