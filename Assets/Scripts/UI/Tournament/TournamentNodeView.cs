using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보상 토너먼트 경로의 정점 하나(TournamentNode 프리팹 루트에 부착).
// 인덱스만 들고 표시값은 매번 TournamentProgress에서 다시 받는다(스냅샷을 캐싱하면 클리어 후 stale).
public class TournamentNodeView : MonoBehaviour
{
    [SerializeField] Image avatarImage;      // 상대 초상(저작 미배선이면 프리팹 기본 유지)
    [SerializeField] TMP_Text nameText;      // 상대 표시명
    [SerializeField] Button tapButton;       // 정점 = 도전 버튼

    [Tooltip("보상 미리보기 칸(아이콘 + 수량). 저작한 보상이 칸 수보다 적으면 남는 칸은 꺼진다.")]
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;

    [Header("상태 노드(선택 — 미배선 시 null 가드)")]
    [SerializeField] GameObject lockedMark;      // 잠김(자물쇠)
    [SerializeField] GameObject clearedMark;     // 클리어(체크)
    [SerializeField] GameObject currentMark;     // 지금 도전할 정점 강조
    [SerializeField] CanvasGroup canvasGroup;

    [Tooltip("잠긴 정점의 알파. 클리어는 따로 딤하지 않는다(체크 표식이 상태를 말한다).")]
    [SerializeField] float lockedAlpha = 0.45f;

    // 보상 조회용 공용 버퍼 — 칸이 값을 즉시 복사하므로 뷰마다 리스트를 들 이유가 없다.
    static readonly List<RewardLine> s_rewardBuffer = new List<RewardLine>();

    static bool s_overflowWarned;

    // 표시 대상 정점. -1 = 미바인딩(Refresh 무시).
    int m_index = -1;

    Action<int> m_onTap;

    // 정점 인덱스 배선 + 리스너 1회 등록(재빌드마다 중복 방지).
    public void Bind(int _index, Action<int> _onTap)
    {
        this.m_index = _index;
        this.m_onTap = _onTap;

        if (this.tapButton != null)
        {
            this.tapButton.onClick.RemoveAllListeners();
            this.tapButton.onClick.AddListener(this.OnTapped);
        }

        this.Refresh();
    }

    // 상태 표시 갱신. 진행 통지(OnChanged)마다 맵이 전 정점에 호출한다.
    public void Refresh()
    {
        if (this.m_index < 0) return;

        TournamentProgress.TryGetNode(this.m_index, out TournamentNodeDef t_node);
        ETournamentNodeState t_state = TournamentProgress.StateOf(this.m_index);

        // 초상·표시명 미저작이면 프리팹에 저작된 값을 그대로 둔다(빈 값으로 덮으면 목업이 사라진다).
        if (this.avatarImage != null && t_node.avatar != null) this.avatarImage.sprite = t_node.avatar;
        if (this.nameText != null && !string.IsNullOrEmpty(t_node.displayName)) this.nameText.text = t_node.displayName;

        this.BindRewardSlots();

        bool t_playable = t_state == ETournamentNodeState.Playable;
        bool t_cleared = t_state == ETournamentNodeState.Cleared;
        bool t_locked = t_state == ETournamentNodeState.Locked;

        if (this.tapButton != null) this.tapButton.interactable = t_playable;
        if (this.lockedMark != null) this.lockedMark.SetActive(t_locked);
        if (this.clearedMark != null) this.clearedMark.SetActive(t_cleared);
        if (this.currentMark != null) this.currentMark.SetActive(t_playable);
        if (this.canvasGroup != null) this.canvasGroup.alpha = t_locked ? this.lockedAlpha : 1f;
    }

    void BindRewardSlots()
    {
        if (this.rewardSlots == null || this.rewardSlots.Length == 0) return;

        TournamentProgress.FillRewards(this.m_index, s_rewardBuffer);

        for (int t_i = 0; t_i < this.rewardSlots.Length; t_i++)
        {
            if (this.rewardSlots[t_i] == null) continue;

            if (t_i < s_rewardBuffer.Count)
                this.rewardSlots[t_i].Bind(s_rewardBuffer[t_i].Icon, s_rewardBuffer[t_i].Gain.Amount);
            else
                this.rewardSlots[t_i].Hide();
        }

        // 저작 문제라 정점마다 찍으면 소음이다 — 세션에 한 번이면 족하다.
        if (s_rewardBuffer.Count > this.rewardSlots.Length && !s_overflowWarned)
        {
            s_overflowWarned = true;
            Debug.LogWarning($"[TournamentNodeView] 정점 보상 {s_rewardBuffer.Count}건이 슬롯 {this.rewardSlots.Length}칸을 초과 — 앞칸만 표시한다.", this);
        }
    }

    // 도전 요청은 맵으로 올린다(정점은 씬 전환을 모른다). 잠김 판정은 맵이 한 번 더 본다.
    void OnTapped()
    {
        if (this.m_index < 0) return;
        this.m_onTap?.Invoke(this.m_index);
    }
}
