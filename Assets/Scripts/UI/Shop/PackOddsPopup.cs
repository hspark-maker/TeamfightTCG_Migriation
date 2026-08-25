using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 카드팩 등장 확률 고지 팝업. 확률 계산은 PackOdds 단독 — 여기선 표시만 한다.
public class PackOddsPopup : PooledUIBase
{
    [SerializeField] TextMeshProUGUI titleText;
    [SerializeField] TextMeshProUGUI footerText;    // 고지 문구(옵션 — 미배선 무시).
    [SerializeField] Button closeButton;
    [SerializeField] Button dimCloseButton;         // 바깥 어둡게 영역 탭으로도 닫기(옵션).

    [Header("목록")]
    [SerializeField] RectTransform rowRoot;         // 행이 쌓이는 부모(ScrollRect content).
    [SerializeField] PackOddsRow rowPrefab;

    // 팩마다 카드 수가 달라 행은 재사용한다 — 매번 파괴/생성하면 스크롤이 튄다.
    readonly List<PackOddsRow> m_rows = new List<PackOddsRow>();

    public override void Initialization(UIData _data)
    {
        this.data = _data;
        if (_data is PackOddsData t_d) Bind(t_d.pack);
        Show();
    }

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

    void OnEnable()
    {
        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveListener(OnClosePressed);
            this.closeButton.onClick.AddListener(OnClosePressed);
        }
        if (this.dimCloseButton != null)
        {
            this.dimCloseButton.onClick.RemoveListener(OnClosePressed);
            this.dimCloseButton.onClick.AddListener(OnClosePressed);
        }
    }

    void OnDisable()
    {
        if (this.closeButton != null) this.closeButton.onClick.RemoveListener(OnClosePressed);
        if (this.dimCloseButton != null) this.dimCloseButton.onClick.RemoveListener(OnClosePressed);
    }

    void OnClosePressed() => Hide();

    void Bind(CardPackData _pack)
    {
        if (this.titleText != null)
            this.titleText.text = _pack != null ? _pack.DisplayName : string.Empty;

        List<PackOddsEntry> t_entries = PackOdds.Resolve(_pack);
        FillRows(t_entries);

        if (this.footerText != null)
            this.footerText.text = FooterFor(_pack, t_entries.Count);
    }

    void FillRows(List<PackOddsEntry> _entries)
    {
        if (this.rowRoot == null || this.rowPrefab == null) return;

        for (int t_i = 0; t_i < _entries.Count; t_i++)
        {
            if (t_i >= m_rows.Count) m_rows.Add(Instantiate(this.rowPrefab, this.rowRoot));
            m_rows[t_i].gameObject.SetActive(true);
            m_rows[t_i].Bind(_entries[t_i]);
        }

        // 남는 행은 끄기만 한다(다음 팩이 더 길면 그대로 다시 쓴다).
        for (int t_i = _entries.Count; t_i < m_rows.Count; t_i++)
            m_rows[t_i].gameObject.SetActive(false);
    }

    // 뽑는 장수·중복 규칙까지 함께 고지한다 — 확률만 적으면 "6장 뽑는데 왜 같은 게 나오냐"가 남는다.
    static string FooterFor(CardPackData _pack, int _count)
    {
        if (_pack == null) return string.Empty;

        string t_unique = _pack.UniqueDraw
            ? "한 팩 안에서 같은 카드는 나오지 않습니다."
            : "한 팩 안에서 같은 카드가 중복될 수 있습니다.";

        return $"1회 {_pack.DrawCount}장 획득 · 총 {_count}종\n"
             + $"{t_unique}\n"
             + "표기 확률은 1장 뽑을 때의 확률이며, 소수점 셋째 자리에서 반올림했습니다.";
    }
}

public class PackOddsData : UIData
{
    public CardPackData pack;
}
