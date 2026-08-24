using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 시너지 설명 팝업(풀드 UI). 카드 정보 창의 시너지 아이콘을 누르면 뜬다.
/// KeywordExplainPopupUI와 같은 구조·같은 배치 로직 — 키워드/시너지 팝업 동작이 갈리지 않게 맞춘다.
///
/// 프리팹은 Addressables "UIPrefab" 라벨이 있어야 DataLibrary.LoadUIPrefab이 찾는다.
/// 문구 포맷은 SynergyText가 단독 소유한다(덱 편성 툴팁과 공용).
/// </summary>
public class SynergyExplainPopupUI : PooledUIBase
{
    [SerializeField] Image    iconImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text explainText;
    [SerializeField] Image    accent;      // SynergyData.color 강조(선택)
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
        if (_data is not SynergyExplainData t_d || t_d.synergy == null) return;

        SynergyData t_s = t_d.synergy;

        if (this.iconImage != null)
        {
            this.iconImage.sprite  = t_s.activeIcon;
            this.iconImage.enabled = t_s.activeIcon != null;
            // 아이콘 PNG 투명 여백 보정 — 팝업 아이콘도 정보 창 아이콘과 같은 크기로 보이게.
            this.iconImage.rectTransform.localScale = Vector3.one * SynergyIconStrip.IconPadCompensation;
        }
        if (this.nameText    != null) this.nameText.text    = SynergyText.Name(t_s);
        // ownedCount 미지정(-1) → 카드 정보 창에는 덱 문맥이 없으므로 ●/○ 마커 없이 요구치만.
        if (this.explainText != null) this.explainText.text = SynergyText.Body(t_s, t_d.ownedCount);
        if (this.accent      != null) this.accent.color     = t_s.TintOrWhite;   // 미배정 색이면 투명해지므로 폴백

        // 배치 규칙은 PopupPlacer가 단독 소유(키워드 팝업/시너지 툴팁과 동일 동작).
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

public class SynergyExplainData : UIData
{
    public SynergyData   synergy;
    /// <summary>uGUI 아이콘 옆에 띄울 때. 인게임 배지처럼 월드 오브젝트면 null로 두고 아래를 쓴다.</summary>
    public RectTransform iconRect;

    /// <summary>인게임 카드 위 시너지 배지처럼 월드 스페이스 대상 옆에 띄울 때 사용.</summary>
    public bool    hasWorldAnchor;
    public Vector3 worldAnchor;
    public float   worldHalfWidth = 0.2f;

    /// <summary>덱에서 보유 중인 장수. 음수면 덱 문맥 없음(마커 표시 안 함).</summary>
    public int ownedCount = -1;
}
