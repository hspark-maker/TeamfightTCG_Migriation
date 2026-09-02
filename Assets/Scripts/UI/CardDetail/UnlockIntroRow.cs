using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>해금 안내 한 줄. 글자는 KeywordExplainItem에 위임하고, 데모 띠와 시너지 배지,
/// 그리고 시너지 줄에 서는 티어 줄들을 맡는다.
///
/// 세로 자리와 띠 크기의 주인은 저작이다 — 줄 묶음은 ExplainText의 VerticalLayoutGroup이,
/// 띠의 높이는 DemoStrip의 AspectRatioFitter가 정한다. 여기는 무엇을 채우고 무엇을 끌지만 정한다.</summary>
[RequireComponent(typeof(RectTransform))]
public class UnlockIntroRow : MonoBehaviour
{
    [Tooltip("아이콘·이름·설명. 미배선이면 글자 없이 띠만 뜬다.")]
    [SerializeField] KeywordExplainItem item;

    [Tooltip("데모 무대가 그려지는 띠. 미배선이면 이 축만 빠지고 행은 글자로 성립한다.")]
    [SerializeField] RawImage demoStrip;

    [Tooltip("데모 띠의 AspectRatioFitter(선택). 맞춤 모드는 저작이 갖고 여기서는 실측 비율만 넣는다 —\n" +
             "저작한 비율과 무대 텍스처의 크기가 갈라져도 띠가 찌그러지지 않게 하는 것이 목적이다.")]
    [SerializeField] AspectRatioFitter demoFitter;

    [Tooltip("데모 무대를 못 세운 시너지 칸이 대신 세우는 가운데 배지. 미배선이면 이 축만 빠지고 행은 글자로 성립한다.")]
    [SerializeField] Image synergyBadge;

    [Tooltip("데모 띠 안쪽 좌상단에 서는 전투 시너지 패널 조각(밑판째). 크기·여백의 주인은 저작이다 —\n" +
             "밑판의 HorizontalLayoutGroup·ContentSizeFitter가 정하므로 여기서 재지 않는다.\n" +
             "미배선이면 이 축만 빠지고 행은 글자와 띠로 성립한다.")]
    [SerializeField] GameObject demoSynergyPanel;

    [Tooltip("그 밑판에 한 장 서는 아이콘. 전투와 같은 SynergyIcon 프리팹 인스턴스여야 한다.")]
    [SerializeField] SynergyIconView demoSynergyIcon;

    [Header("티어 줄")]
    [Tooltip("미리 깔아 둔 티어 줄. 세우는 순서는 배선 순서가 아니라 계층 순서다 —\n" +
             "줄을 쌓는 VerticalLayoutGroup이 계층을 보므로 그쪽에 맞춰야 화면과 어긋나지 않는다.\n" +
             "런타임 Instantiate 없음 — 개수가 모자라면 뒷 단계부터 잘린다.")]
    [SerializeField] UnlockIntroTierRow[] tierRows;

    // 이 줄이 시너지면 여기에 아이콘이 들어온다. 실제로 세울지는 SetDemo가 정한다 —
    // 배지와 데모 띠는 같은 자리를 쓰므로 둘 중 하나만 서야 한다.
    Sprite m_badgeIcon;

    // 이 줄이 시너지면 여기에 그 시너지가 들어온다. 실제로 세울지는 SetDemo가 정한다 —
    // 조각은 띠 **안쪽**에 서므로 띠가 꺼진 자리에는 설 곳 자체가 없다.
    SynergyData m_demoSynergy;

    // tierRows를 계층 순서로 다시 세운 것. 계층은 런타임에 바뀌지 않아 한 번만 만든다.
    UnlockIntroTierRow[] m_rowsInOrder;

    // 깔린 티어 줄이 모자란 것은 저작 문제다 — 매 표시마다 경고하면 로그가 묻힌다.
    static bool s_tierShortageWarned;

    /// <summary>글자를 채우고 배지 후보를 받아 둔다. 가운데 자리의 주인은 <see cref="SetDemo"/>가 정한다.</summary>
    public void Bind(UnlockIntro _intro)
    {
        this.m_badgeIcon   = _intro.IsSynergy ? _intro.Icon    : null;
        this.m_demoSynergy = _intro.IsSynergy ? _intro.Synergy : null;

        // 데모 여부가 아직 안 정해졌다 — 일단 둘 다 비우고 SetDemo를 기다린다
        SetSynergyBadge(null);
        SetDemoSynergyPanel(null);

        if (this.item != null)
            this.item.Init(_intro.Icon, _intro.Name, _intro.Body, _intro.IconScale);

        FillTiers(_intro.Synergy);
    }

    /// <summary>데모 띠에 그림을 물린다. null이면 띠를 끄고, 시너지 줄이면 그 자리에 배지를 대신 세운다
    /// (저작이 덜 됐거나 무대를 못 세운 경우의 폴백).
    ///
    /// 띠가 서는 시너지 줄에서는 띠 안쪽 좌상단에 전투와 같은 시너지 패널 조각도 함께 세운다.</summary>
    public void SetDemo(Texture _texture)
    {
        if (this.demoStrip != null)
        {
            this.demoStrip.texture = _texture;
            this.demoStrip.gameObject.SetActive(_texture != null);

            if (_texture != null && _texture.height > 0 && this.demoFitter != null)
                this.demoFitter.aspectRatio = (float)_texture.width / _texture.height;
        }

        SetSynergyBadge(_texture == null ? this.m_badgeIcon : null);
        SetDemoSynergyPanel(_texture != null ? this.m_demoSynergy : null);
    }

    // 티어 줄을 위에서부터 채우고 남는 줄은 끈다. 자리를 잡는 것은 VerticalLayoutGroup이라
    // 여기서는 켜고 끄는 것만 정한다 — 키워드 줄은 티어가 없어 전부 꺼진다.
    void FillTiers(SynergyData _synergy)
    {
        EnsureRowOrder();

        int t_slots = this.m_rowsInOrder.Length;
        if (t_slots == 0) return;

        SynergyTier[] t_tiers = _synergy != null ? _synergy.tiers : null;
        int           t_count = CountTiers(t_tiers);

        if (t_count > t_slots)
        {
            if (!s_tierShortageWarned)
            {
                s_tierShortageWarned = true;
                Debug.LogWarning($"[UnlockIntroRow] 깔린 티어 줄이 {t_slots}개뿐이라 "
                               + $"{t_count}단계를 다 세우지 못했습니다(프리팹에 줄을 더 깔 것).");
            }
            t_count = t_slots;
        }

        for (int t_i = 0; t_i < t_slots; t_i++)
        {
            UnlockIntroTierRow t_row = this.m_rowsInOrder[t_i];

            if (t_i >= t_count) { t_row.gameObject.SetActive(false); continue; }

            SynergyTier t_tier = TierAt(t_tiers, t_i);
            t_row.Bind(SynergyText.TierRequirement(t_tier), SynergyText.TierEffect(_synergy, t_tier));
            t_row.gameObject.SetActive(true);
        }
    }

    // 배선 순서와 계층 순서가 어긋나 있어도 화면과 어긋나지 않도록, 줄이 쌓이는 순서 그대로 세워 둔다.
    void EnsureRowOrder()
    {
        if (this.m_rowsInOrder != null) return;

        List<UnlockIntroTierRow> t_ordered = new List<UnlockIntroTierRow>();

        if (this.tierRows != null)
            foreach (UnlockIntroTierRow t_row in this.tierRows)
                if (t_row != null) t_ordered.Add(t_row);

        t_ordered.Sort((_a, _b) => _a.transform.GetSiblingIndex().CompareTo(_b.transform.GetSiblingIndex()));
        this.m_rowsInOrder = t_ordered.ToArray();
    }

    static int CountTiers(SynergyTier[] _tiers)
    {
        if (_tiers == null) return 0;

        int t_count = 0;
        foreach (SynergyTier t_tier in _tiers)
            if (t_tier != null) t_count++;

        return t_count;
    }

    // 빈 칸이 섞여 있어도 순서가 밀리지 않게, null을 건너뛰며 _index번째 유효 티어를 꺼낸다.
    static SynergyTier TierAt(SynergyTier[] _tiers, int _index)
    {
        if (_tiers == null) return null;

        int t_seen = 0;
        foreach (SynergyTier t_tier in _tiers)
        {
            if (t_tier == null) continue;
            if (t_seen == _index) return t_tier;
            t_seen++;
        }

        return null;
    }

    void SetSynergyBadge(Sprite _icon)
    {
        if (this.synergyBadge == null) return;

        this.synergyBadge.sprite = _icon;
        this.synergyBadge.gameObject.SetActive(_icon != null);
    }

    // 시너지 줄이면서 데모 띠가 실제로 켜진 경우에만 선다. 조건이 SetSynergyBadge와 뒤집혀 있는 것이 규약이다 —
    // 배지는 띠를 못 세웠을 때의 폴백이고 이 조각은 띠 안에 서는 것이라, 둘이 같이 뜨면 같은 아이콘이 두 장이 된다.
    void SetDemoSynergyPanel(SynergyData _synergy)
    {
        if (this.demoSynergyPanel == null) return;

        // 아이콘이 미배선이면 밑판만 뜬 빈 접시가 된다 — 그럴 바에 조각째 접는다.
        bool t_show = _synergy != null && this.demoSynergyIcon != null;

        if (t_show) this.demoSynergyIcon.Bind(_synergy);   // 필드가 없는 칸이라 소속 카드 수는 기본값(문맥 없음)

        this.demoSynergyPanel.SetActive(t_show);
    }
}
