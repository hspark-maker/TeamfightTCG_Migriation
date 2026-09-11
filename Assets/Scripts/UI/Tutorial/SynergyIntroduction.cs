using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>저장된 덱의 최초 활성 시너지를 설명한다. 가이드 수령과 온보딩 커서를 변경하지 않는다.</summary>
public sealed class SynergyIntroduction : MonoBehaviour
{
    static SynergyIntroduction s_instance;
    bool m_active;
    bool m_deferred;
    bool m_presentingDeck;
    SimpleYNPopup m_proposal;
    DeckEditController m_editor;
    SynergyProgress m_target;
    int m_slot = -1;
    RectTransform m_badge;
    Vector3 m_badgeScale;

    static SynergyIntroductionSaveData State => DataSaveManager.Data?.Tutorial?.SynergyIntroduction;
    public static bool IsActive => s_instance != null && s_instance.m_active;
    public static bool HasPending => State != null && !State.Completed && State.DeckSlot >= 0
        && (s_instance == null || !s_instance.m_deferred);

    /// <summary>새 서버 세이브 채택 후 세션 UI와 미루기 상태를 초기화한다. 저장 진행도는 보존한다.</summary>
    public static void ResetSession()
    {
        if (s_instance == null) return;
        s_instance.ClearPresentation();
        s_instance.m_deferred = false;
        s_instance.m_target = null;
        s_instance.m_slot = -1;
    }

    static SynergyIntroduction Ensure()
    {
        if (s_instance != null) return s_instance;
        var t_go = new GameObject(nameof(SynergyIntroduction));
        DontDestroyOnLoad(t_go);
        s_instance = t_go.AddComponent<SynergyIntroduction>();
        return s_instance;
    }

    void Awake()
    {
        s_instance = this;
        DeckSaveManager.OnDeckChanged += OnStateChanged;
        DeckSaveManager.OnSelectedSlotChanged += OnStateChanged;
        CardGrowthManager.OnGrowthChanged += OnStateChanged;
        OwnershipManager.OnOwnershipChanged += OnStateChanged;
    }

    void OnDestroy()
    {
        DeckSaveManager.OnDeckChanged -= OnStateChanged;
        DeckSaveManager.OnSelectedSlotChanged -= OnStateChanged;
        CardGrowthManager.OnGrowthChanged -= OnStateChanged;
        OwnershipManager.OnOwnershipChanged -= OnStateChanged;
        ClearPresentation();
        if (s_instance == this) s_instance = null;
    }

    void OnStateChanged()
    {
        m_deferred = false;
        Reevaluate();
    }

    void LateUpdate()
    {
        if (m_active && m_presentingDeck && (m_editor == null || !m_editor.IsOpen || !m_editor.gameObject.activeInHierarchy
            || m_editor.CurrentSlot != m_slot)) ClearPresentation();
    }

    public static void Reevaluate()
    {
        if (!CardCatalog.IsReady || !CardGrowthManager.IsReady || State == null || State.Completed) return;
        Ensure();
        if (IsActive) return;
        int t_slot = -1;
        SynergyProgress t_target = null;
        // 저장한 후보가 여전히 유효하면 유지한다. 없거나 무효일 때만 우선순위로 새로 고른다.
        if (TryResolve(State.DeckSlot, State.SynergyId, out t_target)) t_slot = State.DeckSlot;
        else
        {
            int t_selected = DeckSaveManager.SelectedSlot;
            if (TryResolve(t_selected, null, out t_target)) t_slot = t_selected;
            else
                for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
                    if (t_i != t_selected && TryResolve(t_i, null, out t_target)) { t_slot = t_i; break; }
        }
        string t_id = t_target?.Synergy.SynergyId ?? "";
        if (State.DeckSlot == t_slot && State.SynergyId == t_id) return;
        State.DeckSlot = t_slot;
        State.SynergyId = t_id;
        DataSaveManager.SaveCoalesced();
        Debug.Log($"[SynergyIntroduction] Candidate slot={t_slot}, synergy={t_id}");
    }

    static bool TryResolve(int _slot, string _id, out SynergyProgress _target)
    {
        _target = null;
        if (_slot < 0 || _slot >= DeckSaveManager.SLOT_COUNT || !DeckSaveManager.IsSlotValid(_slot)) return false;
        List<int> t_cards = DeckSaveManager.GetSlot(_slot);
        if (new HashSet<int>(t_cards).Count != DeckSaveManager.DECK_SIZE) return false;
        foreach (int t_card in t_cards) if (!OwnershipManager.IsOwned(t_card)) return false;
        List<SynergyProgress> t_progress = DeckSynergyEligibility.Resolve(t_cards);
        foreach (SynergyProgress t_item in t_progress)
        {
            if (!t_item.IsActive || (!string.IsNullOrEmpty(_id) && t_item.Synergy.SynergyId != _id)) continue;
            if (_target == null || string.CompareOrdinal(t_item.Synergy.SynergyId, _target.Synergy.SynergyId) < 0)
                _target = t_item;
        }
        return _target != null;
    }

    /// <summary>표시 조정기가 안전한 로비 시점에 호출한다.</summary>
    public static bool TryBegin()
    {
        Reevaluate();
        if (IsActive || !HasPending || !OutgameTutorialProgress.IsCompleted || UIPoolManager.instance == null) return false;
        SynergyIntroduction t_self = Ensure();
        if (!TryResolve(State.DeckSlot, State.SynergyId, out t_self.m_target)) return false;
        t_self.m_slot = State.DeckSlot;
        t_self.m_active = true;
        bool t_accept = false;
        t_self.m_proposal = UIPoolManager.instance.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = $"{SynergyText.Name(t_self.m_target.Synergy)} 시너지가 활성화됐어요!\n함께 강해진 카드를 확인해 보세요.",
            yesText = "조합 보기",
            noText = "나중에",
            yesAction = () => t_accept = true,
            noAction = () =>
            {
                t_self.m_deferred = true;
                Debug.Log("[SynergyIntroduction] Deferred " + t_self.m_target.Synergy.SynergyId);
            },
            onHide = () =>
            {
                t_self.m_proposal = null;
                if (!t_self.m_active) return;
                if (t_accept) t_self.StartCoroutine(t_self.OpenDeck());
                else t_self.m_active = false;
            },
        });
        if (t_self.m_proposal != null)
        {
            Debug.Log("[SynergyIntroduction] Present " + t_self.m_target.Synergy.SynergyId);
            return true;
        }
        t_self.m_active = false;
        return false;
    }

    IEnumerator OpenDeck()
    {
        // SimpleYNPopup은 액션 실행 뒤 Hide한다. 다음 프레임에 다음 UI를 세운다.
        yield return null;
        if (!m_active || !TryResolve(m_slot, State.SynergyId, out m_target)) { ClearPresentation(); yield break; }
        LobbyTabController t_shell = FindFirstObjectByType<LobbyTabController>();
        DeckTabController t_tab = t_shell != null ? t_shell.GetComponentInChildren<DeckTabController>(true) : null;
        if (t_shell == null || t_tab == null) { ClearPresentation(); yield break; }
        t_shell.Select(t_tab, false);
        t_tab.OpenEditor(m_slot);
        // 탭 슬라이드와 편집기 레이아웃이 자리를 잡은 다음 강조한다.
        yield return new WaitForSecondsRealtime(0.5f);
        if (!m_active || !TryResolve(m_slot, State?.SynergyId, out m_target))
        { ClearPresentation(); yield break; }
        foreach (DeckEditController t_editor in FindObjectsByType<DeckEditController>(FindObjectsSortMode.None))
            if (t_editor.IsOpen && t_editor.gameObject.activeInHierarchy && t_editor.CurrentSlot == m_slot)
            { m_editor = t_editor; break; }
        if (m_editor == null || !TryResolve(m_slot, State.SynergyId, out m_target)) { ClearPresentation(); yield break; }
        RectTransform t_anchor = m_editor.FindSynergyAnchor(m_target.Synergy);
        if (t_anchor == null) { ClearPresentation(); yield break; }
        m_badge = t_anchor;
        m_presentingDeck = true;
        m_badgeScale = t_anchor.localScale;
        t_anchor.localScale = m_badgeScale * 1.15f;
        m_editor.ApplySynergyFocus(m_target.Synergy);
        OutgameTutorialGateUI.Ensure().ShowMessageGate(this, t_anchor,
            $"시너지가 해금된 {SynergyText.Name(m_target.Synergy)} 카드 {m_target.Count}장을 모아\n시너지 효과가 활성화됐어요.",
            ShowEffect, _atBottom: true, _dim: false);
    }

    void ShowEffect()
    {
        if (!m_active || m_editor == null || !TryResolve(m_slot, State.SynergyId, out m_target))
        { ClearPresentation(); return; }
        ExplainPopupData t_data = ExplainPopupData.ForSynergy(m_target.Synergy, m_target.Count);
        if (t_data == null) { ClearPresentation(); return; }
        OutgameTutorialGateUI.Ensure().ShowMessageGate(this, m_editor.FindSynergyAnchor(m_target.Synergy),
            t_data.displayName + "\n" + t_data.explain, Complete, _atBottom: true, _dim: false);
    }

    void Complete()
    {
        State.Completed = true;
        DataSaveManager.SaveCoalesced();
        Debug.Log("[SynergyIntroduction] Completed " + State.SynergyId);
        ClearPresentation();
    }

    public static void CancelPresentation()
    {
        if (s_instance != null) s_instance.ClearPresentation();
    }

    void ClearPresentation()
    {
        m_active = false;
        m_presentingDeck = false;
        StopAllCoroutines();
        if (m_proposal != null && m_proposal.isShow) m_proposal.Hide();
        m_proposal = null;
        OutgameTutorialGateUI.Instance?.Clear(this);
        if (m_badge != null) m_badge.localScale = m_badgeScale;
        m_badge = null;
        if (m_editor != null) m_editor.ApplySynergyFocus(null);
        m_editor = null;
    }
}
