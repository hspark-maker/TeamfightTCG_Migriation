using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 정점 도전 확인 팝업. "누구와 싸우는가 · 얼마나 센가 · 무엇을 받는가"를 한 화면에 세우고 도전 여부만 묻는다.
// 진입 자격과 전투 연결은 맵(TournamentMapOverlayView)이 계속 쥔다 — 여기서는 [전투]를 콜백으로 올릴 뿐이다.
// 그래서 이 팝업은 정점의 인덱스만 알면 되고, 씬 전환도 보상 지급도 모른다.
public class TournamentNodePopup : PooledUIBase
{
    [SerializeField] TMP_Text titleText;

    [Tooltip("상대 초상. TournamentNodeDef.avatar가 비어 있으면 프리팹에 저작된 그림을 그대로 둔다(정점 표현과 같은 규약).")]
    [SerializeField] Image portraitImage;

    [Tooltip("권장 전투력 한 줄. 값은 적 덱에서 파생되므로 저작할 것이 없다(DeckPower.OfAtLevel).")]
    [SerializeField] TMP_Text powerText;

    [Tooltip("보상 묶음 전체. 보상이 0건인 정점에서는 이 묶음이 통째로 꺼진다 — 빈 칸 세 개가 남으면 '못 받았다'로 읽힌다.")]
    [SerializeField] GameObject rewardSection;

    [Tooltip("보상 칸(아이콘 + 수량). 저작한 보상이 칸 수보다 적으면 남는 칸은 꺼지고, 가로 정렬이 남은 칸을 가운데로 모은다.")]
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;

    [SerializeField] Button battleButton;
    [SerializeField] Button backButton;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("권장 전투력 표기. {0}에 수가 들어간다.")]
    [SerializeField] string powerFormat = "권장 전투력 {0:N0}";

    static bool s_overflowWarned;

    // [전투] 콜백. 팝업이 닫힌 뒤 불려야 전환 연출과 겹치지 않는다.
    Action m_onBattle;

    // 표시용 보상 줄. 매번 새로 담아 쓰는 버퍼다(정점마다 건수가 갈린다).
    readonly List<RewardLine> m_rewards = new List<RewardLine>();

    public override void Initialization(UIData _data)
    {
        this.data = _data;
        this.m_onBattle = null;

        if (_data is TournamentNodePopupData t_d)
        {
            this.m_onBattle = t_d.onBattle;
            this.Bind(t_d.nodeIndex);
        }

        // 재표시마다 중복 등록 방지 — 풀이 같은 인스턴스를 되돌려준다.
        if (this.battleButton != null)
        {
            this.battleButton.onClick.RemoveAllListeners();
            this.battleButton.onClick.AddListener(this.OnBattlePressed);
        }

        if (this.backButton != null)
        {
            this.backButton.onClick.RemoveAllListeners();
            this.backButton.onClick.AddListener(this.OnBackPressed);
        }
    }

    public override void Show()
    {
        this.transition.SetVisible(this.contents, true);
        this.isShow = true;
        this.data?.showCustomMethod?.Invoke();
    }

    public override void Hide()
    {
        this.transition.SetVisible(this.contents, false);
        this.isShow = false;
        this.data?.onHide?.Invoke();
    }

    // 퇴장이 끝나기 전에 부모가 꺼지면 contents가 켜진 채 남는다 — 다음 열기의 유령 프레임을 막는다.
    void OnDisable() => this.transition.HandleDisabled(this.contents);

    void OnBattlePressed()
    {
        Action t_go = this.m_onBattle;

        // 한 번 쓰면 비운다. 전환이 시작된 뒤 연타가 들어와도 두 번 나가지 않게.
        this.m_onBattle = null;

        this.Hide();
        t_go?.Invoke();
    }

    void OnBackPressed() => this.Hide();

    void Bind(int _index)
    {
        if (!TournamentProgress.TryGetNode(_index, out TournamentNodeDef t_node)) return;

        if (this.titleText != null) this.titleText.text = t_node.displayName;

        // 미저작(null)이면 프리팹 그림을 남긴다 — 덮어쓰면 24정점이 전부 빈 사각이 된다.
        if (this.portraitImage != null && t_node.avatar != null) this.portraitImage.sprite = t_node.avatar;

        if (this.powerText != null)
            this.powerText.text = string.Format(this.powerFormat,
                DeckPower.OfAtLevel(t_node.enemyDeck, t_node.AiCardLevelOrBase));

        this.m_rewards.Clear();
        TournamentProgress.FillRewards(_index, this.m_rewards);
        this.BindRewardSlots();
    }

    void BindRewardSlots()
    {
        if (this.rewardSection != null) this.rewardSection.SetActive(this.m_rewards.Count > 0);

        if (this.rewardSlots == null) return;

        for (int t_i = 0; t_i < this.rewardSlots.Length; t_i++)
        {
            if (this.rewardSlots[t_i] == null) continue;

            if (t_i < this.m_rewards.Count)
                this.rewardSlots[t_i].Bind(this.m_rewards[t_i].Icon, this.m_rewards[t_i].Gain.Amount);
            else
                this.rewardSlots[t_i].Hide();
        }

        // 저작보다 보상이 많으면 조용히 잘린다 — 정점 보상을 늘렸을 때 알아채라고 한 번만 알린다.
        if (this.m_rewards.Count > this.rewardSlots.Length && !s_overflowWarned)
        {
            s_overflowWarned = true;
            Debug.LogWarning($"[TournamentNodePopup] 보상 {this.m_rewards.Count}건이 슬롯 {this.rewardSlots.Length}칸을 초과");
        }
    }
}

public class TournamentNodePopupData : UIData
{
    public int nodeIndex;

    /// <summary>[전투]에서 불린다. 팝업이 닫힌 뒤에 온다.</summary>
    public Action onBattle;
}
