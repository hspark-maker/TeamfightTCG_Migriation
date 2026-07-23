using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시너지 설명 툴팁. <see cref="DeckSynergyStrip"/>이 소유하고 롱프레스 때만 띄운다.
///
/// UIPoolManager 팝업이 아니라 패널 자식으로 둔 이유: 이 툴팁은 특정 행 옆에 붙어야 하는데
/// 전역 캔버스로 띄우면 좌표 변환이 늘고, Addressables "UIPrefab" 라벨 등록도 필요해진다.
/// 패널과 생명주기를 같이 하는 편이 단순하다.
/// </summary>
public class SynergyTooltip : MonoBehaviour
{
    [SerializeField] RectTransform root;      // 실제로 켜고 끄는 오브젝트(미배선 시 자기 자신)
    [SerializeField] TMP_Text      titleText; // 시너지 이름
    [SerializeField] TMP_Text      bodyText;  // 설명 + 티어 목록
    [SerializeField] Image         accent;    // SynergyData.color 강조바(선택)

    [Header("Placement")]
    [Tooltip("아이콘과 툴팁 사이 여백(px).")]
    [SerializeField] float xOffset = 16f;
    [Tooltip("화면 가장자리 여백(px).")]
    [SerializeField] float edgePadding = 8f;

    RectTransform Root => this.root != null ? this.root : (RectTransform)transform;

    void Awake() => Hide();

    /// <summary>_anchor(누른 행) 옆에 붙여서 표시한다.</summary>
    public void Show(SynergyProgress _p, RectTransform _anchor)
    {
        if (_p == null || _p.Synergy == null) { Hide(); return; }

        SynergyData t_data = _p.Synergy;

        if (this.titleText != null)
            this.titleText.text = SynergyText.Name(t_data);

        // 보유 수를 넘겨 열림(●)/미달(○) 마커까지 나오게 한다.
        if (this.bodyText != null)
            this.bodyText.text = SynergyText.Body(t_data, _p.Count);

        if (this.accent != null)
            this.accent.color = t_data.TintOrWhite;   // 미배정 색이면 투명해지므로 폴백

        this.Root.gameObject.SetActive(true);
        this.Root.SetAsLastSibling();   // 다른 행 위로
        Place(_anchor);
    }

    public void Hide()
    {
        if (this.Root != null) this.Root.gameObject.SetActive(false);
    }

    /// <summary>아이콘 옆에 배치. 오른쪽이 화면을 넘치면 왼쪽으로 넘어간다(PopupPlacer가 규칙 소유).</summary>
    void Place(RectTransform _anchor)
        => PopupPlacer.PlaceBesideAnchor(this.Root, _anchor, this.xOffset, this.edgePadding);
}
