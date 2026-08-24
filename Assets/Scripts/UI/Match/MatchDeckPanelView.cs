using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 매치 진입 직전 화면(MatchDeckPanel.prefab 루트에 부착).
// 책임은 둘뿐이다 — 양쪽 6칸을 각자의 덱으로 그리는 것, 하단 3버튼을 셸에 잇는 것.
// 어떤 저장 슬롯이 선택됐는지는 이 뷰가 아니라 셸(MatchDeckShell)이 안다 — 여기는 상태를 들지 않는 순수 렌더러다.
// 덱 데이터의 진실원은 내 쪽이 DeckSaveManager, 상대 쪽이 DeckConfig.EnemyDeck이고
// 이 뷰는 매 Render마다 거기서 다시 읽는다(사본을 캐시하지 않는다).
// 상대 덱을 "무엇으로 확정할지"는 여기서 정하지 않는다 — 게이트를 열기 전에
// LobbyMatchLauncher.ConfirmOpponent가 확정해 캐리어에 실어둔다(전투가 소비하는 값과 동일).
public class MatchDeckPanelView : MonoBehaviour
{
    [SerializeField] MatchDeckShell   shell;
    [SerializeField] CardVisualView[] mySlots;      // 6칸. MySlot_N 자신이 아니라 자식 MySlot_N/CardUIView를 물린다
    [SerializeField] CardVisualView[] enemySlots;   // 6칸. 같은 규약 — EnemySlot_N/CardUIView를 물린다
    [SerializeField] TMP_Text         myPowerText;      // MyInfoBar/PowerBadge/PowerText
    [SerializeField] TMP_Text         enemyPowerText;   // EnemyInfoBar/PowerBadge/PowerText
    [SerializeField] Button           editButton;
    [SerializeField] Button           backButton;
    [SerializeField] Button           battleButton;

    [Header("연출")]
    [Tooltip("매칭에서 넘어올 때만 도는 등장 안무. 직접 열 때(디버그·튜토리얼)는 지금처럼 그냥 뜬다.")]
    [SerializeField] MatchDeckIntroFx introFx = new MatchDeckIntroFx();

    // 마지막으로 그린 덱 파워. 등장 안무가 0에서 여기까지 세어 올린다 —
    // 환산식을 안무가 다시 계산하면 화면과 배지가 갈라질 수 있다.
    int m_myPowerValue;
    int m_enemyPowerValue;

    // 안무가 도는 동안 손을 막는다. 저작에 없어도 되게 런타임에 붙인다.
    CanvasGroup m_group;

    void Awake()
    {
        // 미배선 필드는 조용히 건너뛴다 — 이 프로젝트의 UI는 부분 배선으로 축소 화면을 만드는 게 관례다.
        if (editButton != null)
        {
            editButton.onClick.RemoveAllListeners();   // 프리팹에 남은 배선·중복 등록 방지
            editButton.onClick.AddListener(OnEditClicked);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackClicked);
        }

        if (battleButton != null)
        {
            battleButton.onClick.RemoveAllListeners();
            battleButton.onClick.AddListener(OnBattleClicked);
        }
    }

    // 양쪽 섹션을 한 번에 그린다.
    // OnEnable에서 자동 호출하지 않는 이유: 어느 슬롯이 선택됐는지는 셸만 안다.
    // 패널이 켜질 때마다 뷰가 스스로 그리면 슬롯을 모른 채 0번이나 직전 값으로 그리게 된다 → 셸이 명시적으로 부른다.
    public void Render(int _slotIndex)
    {
        // 한쪽이 미배선이어도 다른 쪽은 그려야 한다 → 여기서 조기 반환하지 않고 각 렌더러가 알아서 건너뛴다.
        RenderMySlots(_slotIndex);
        RenderEnemySlots();
    }

    // 지정 저장 슬롯의 덱을 MySection 6칸에 그린다.
    void RenderMySlots(int _slotIndex)
    {
        // 슬롯 -1(미선택)·불완전 덱은 모두 여기서 걸린다 — IsSlotValid가 범위와 6장 완성을 함께 판정한다.
        bool t_valid = _slotIndex >= 0 && _slotIndex < DeckSaveManager.SLOT_COUNT && DeckSaveManager.IsSlotValid(_slotIndex);

        // 유효한 덱이 없으면 전투를 시작할 수 없다. 표시용 차단이고, 실제 방어는 Confirm 안의 재검사다.
        // 칸이 미배선이어도 이 판정만은 해야 한다 → 버튼 갱신을 칸 렌더보다 앞에 둔다.
        if (battleButton != null) battleButton.interactable = t_valid;

        List<CardData> t_deck = t_valid ? DeckSaveManager.GetSlot(_slotIndex) : null;

        // _owned는 항상 true다. 매치 화면에 올라오는 건 이미 편성된 소유 카드뿐이라 잠금 표시가 뜨면 안 된다.
        BindSlots(mySlots, t_deck, _mine: true);   // 내 덱이라 강화 반영 체력으로 그린다
        m_myPowerValue = SetPower(myPowerText, t_deck, _mine: true);
    }

    // 상대 덱을 EnemySection 6칸에 그린다. 상대는 저장 슬롯이 아니라 씬 캐리어에서 온다.
    // 캐리어가 비어 있으면(=호스트가 확정하지 못한 경우) 전 칸이 빈 칸으로 접힌다.
    // 멀티는 상대 덱이 이 화면보다 늦게(배틀 씬의 SyncInitialDecks) 도착하므로 애초에 이 화면을 열지 않는다.
    void RenderEnemySlots()
    {
        // 표시용 공개이므로 _owned는 내 쪽과 같은 true — 상대 카드를 잠금 실루엣으로 가리지 않는다.
        // 내 강화는 안 붙인다 — _mine:false는 상대(AI) 레벨 기준으로 그린다(DeckPower.OpponentLevelOf).
        BindSlots(enemySlots, DeckConfig.EnemyDeck, _mine: false);
        // 파워 합도 같은 기준이어야 한다 — 칸은 상대 레벨, 배지만 내 강화 합이면 6칸 합계와 배지가 어긋난다.
        m_enemyPowerValue = SetPower(enemyPowerText, DeckConfig.EnemyDeck, _mine: false);
    }

    /// <summary>
    /// 매칭 화면이 이 화면으로 넘어올 때 쓸 자리들과, 이 화면이 스스로 서는 안무를 함께 건넨다.
    /// 부품을 아는 것은 이 뷰뿐이라 좌표도 안무도 여기서 나간다 — 매칭 셸은 덱 화면의 타입을 몰라도 된다.
    /// </summary>
    public MatchHandoffTargets BuildHandoffTargets()
    {
        // 섹션 좌표는 레이아웃 그룹이 정한다 — 열자마자 읽으면 아직 계산되지 않은 자리를 집는다.
        Canvas.ForceUpdateCanvases();

        SetInteractable(false);

        var t_intro = introFx.BuildIntro(enemySlots, mySlots,
                                         enemyPowerText, m_enemyPowerValue,
                                         myPowerText,    m_myPowerValue,
                                         battleButton);

        // 전환 시퀀스에 얹히기 전에 세워 둔다 — 만들자마자 스스로 돌기 시작하면 얹히는 순간 진행분이 잘린다.
        t_intro.Pause();

        // OnComplete가 아니라 OnKill이다. 씬이 내려가며 안무가 잘리면 완료 콜백은 오지 않아 화면이 손을 못 받는 채로 남는다.
        t_intro.OnKill(() => SetInteractable(true));

        // 루트를 통째로 넘긴다 — 전환이 이 화면을 당겨 들이는(확대→1) 축의 대상이 이것이다.
        return new MatchHandoffTargets(introFx.VersusSeat, (RectTransform)transform, t_intro);
    }

    /// <summary>
    /// 전투 시작에 대한 응답 한 박. 버튼이 튀고 화면이 한 발 앞으로 나간다 —
    /// 게이트(_onGate)는 안무가 끝나기 전에 열려 커튼이 그 위로 닫히며 이 화면을 접어 간다.
    /// 순차로 붙이면(안무가 다 끝난 뒤 게이트) 응답과 커튼이 두 사건이 되어 굼떠진다.
    /// </summary>
    public void PlayLaunch(Action _onGate)
    {
        SetInteractable(false);

        // 게이트는 반드시 한 번은 열려야 하고, 두 번 열려도 안 된다. 안무가 잘리면(씬 파괴·DOTween.KillAll)
        // 시각 콜백이 오지 않아 전투가 영영 시작되지 않는다 → OnKill로 받되 여기서 한 번으로 좁힌다.
        bool t_opened = false;

        void OpenGate()
        {
            if (t_opened) return;

            t_opened = true;
            _onGate?.Invoke();
        }

        Sequence t_seq = introFx.BuildLaunch((RectTransform)transform, battleButton);

        t_seq.SetLink(gameObject);
        t_seq.InsertCallback(introFx.LaunchGateAt, OpenGate);
        t_seq.OnKill(OpenGate);
        t_seq.Play();
    }

    /// <summary>안무가 세운 중간값을 저작 상태로 되돌린다. 전환을 타지 않고 열리는 경로가 반쯤 없는 화면을 물려받지 않게.</summary>
    public void ResetIntro()
    {
        introFx.Reset(enemySlots, mySlots);

        // 전환이 이 루트를 당겨 들이다 잘렸을 수 있다 — 배율을 되돌리지 않으면 다음 진입이 확대된 채로 열린다.
        transform.DOKill();
        transform.localScale = Vector3.one;

        // 파워는 안무가 0부터 세어 올린다 — 도중에 잘리면 0이 찍힌 채 굳는다.
        if (enemyPowerText != null) enemyPowerText.text = m_enemyPowerValue.ToString();
        if (myPowerText    != null) myPowerText.text    = m_myPowerValue.ToString();

        SetInteractable(true);
    }

    void SetInteractable(bool _on)
    {
        if (m_group == null) m_group = GetComponent<CanvasGroup>();
        if (m_group == null) m_group = gameObject.AddComponent<CanvasGroup>();

        m_group.blocksRaycasts = _on;
    }

    // 덱 파워 표기. 환산식은 DeckPower가 단일 진실원이다(편성 화면의 자동 편성 정렬과 같은 식).
    // 덱이 null이면 0이 찍힌다 — 미선택·불완전 덱을 빈칸이 아니라 0으로 보이게 하는 게 의도다.
    static int SetPower(TMP_Text _text, List<CardData> _deck, bool _mine)
    {
        int t_power = DeckPower.Of(_deck, _mine);

        if (_text != null) _text.text = t_power.ToString();   // 미배선 필드는 조용히 건너뛴다

        return t_power;
    }

    // 칸 배열 하나를 덱으로 채운다. 덱이 짧거나 null이면 남는 칸은 빈 칸이 된다.
    static void BindSlots(CardVisualView[] _slots, List<CardData> _deck, bool _mine)
    {
        // 미배선 배열은 조용히 건너뛴다 — 부분 배선으로 축소 화면을 만드는 게 이 프로젝트 UI의 관례다.
        if (_slots == null) return;

        int t_count = _deck != null ? Mathf.Min(_slots.Length, _deck.Count) : 0;

        for (int t_i = 0; t_i < _slots.Length; t_i++)
        {
            if (_slots[t_i] == null) continue;

            // Bind(null, ...)이면 CardVisualView가 스스로 gameObject.SetActive(false)로 빈 칸을 숨긴다
            // (CardVisualView.Bind 진입부) → 여기서 빈 칸 숨김/복구를 따로 처리하지 않는다.
            // 카드를 다시 넘기면 같은 자리에서 SetActive(true)로 되살아난다.
            _slots[t_i].Bind(t_i < t_count ? _deck[t_i] : null, true, _mine);
        }
    }

    void OnEditClicked()
    {
        if (shell != null) shell.OpenEditor();
    }

    // 화면 닫기. 이 오버레이는 로비 위에 떠 있어 닫으면 로비로 돌아갈 뿐이라 확인을 받지 않는다
    // (덱 편집과 달리 잃을 편집분도 없다). 실제로 어디에 남을지는 셸을 await 하는 호스트가 정한다.
    void OnBackClicked()
    {
        if (shell != null) shell.Cancel();
    }

    void OnBattleClicked()
    {
        if (shell != null) shell.Confirm();
    }
}
