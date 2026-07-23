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

    public override void Initialization(UIData _data)
    {
        if (_data is not SynergyExplainData t_d || t_d.synergy == null) return;

        SynergyData t_s = t_d.synergy;

        if (this.iconImage != null)
        {
            this.iconImage.sprite  = t_s.icon;
            this.iconImage.enabled = t_s.icon != null;
        }
        if (this.nameText    != null) this.nameText.text    = SynergyText.Name(t_s);
        // ownedCount 미지정(-1) → 카드 정보 창에는 덱 문맥이 없으므로 ●/○ 마커 없이 요구치만.
        if (this.explainText != null) this.explainText.text = SynergyText.Body(t_s, t_d.ownedCount);
        if (this.accent      != null) this.accent.color     = t_s.TintOrWhite;   // 미배정 색이면 투명해지므로 폴백

        if (t_d.iconRect != null) PositionNearIcon(t_d.iconRect);
    }

    // 배치 규칙은 PopupPlacer가 단독 소유(키워드 팝업/시너지 툴팁과 동일 동작).
    void PositionNearIcon(RectTransform _iconRect)
        => PopupPlacer.PlaceBesideAnchor((RectTransform)transform, _iconRect, this.iconGap);

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
    public RectTransform iconRect;
    /// <summary>덱에서 보유 중인 장수. 음수면 덱 문맥 없음(마커 표시 안 함).</summary>
    public int           ownedCount = -1;
}
