using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// 로비 PlayBtn → 출전 덱 확정 → AI 대전 진입.
/// 전투가 소비하는 DeckConfig.PlayerDeck을 채우는 지점은 이 진입점이 여는 덱 화면(MatchDeckShell) 하나뿐이다.
/// 배틀 씬은 확정된 값을 읽기만 한다 — 확정 지점이 씬을 넘어 둘로 갈리지 않게.
public class LobbyMatchLauncher : MonoBehaviour
{
    [SerializeField] MatchDeckShell shell;          // 미배선이면 게이트 없이 첫 유효 덱으로 진입(구 동작)
    [SerializeField] AIDeckConfig   aiDeckConfig;   // BattleScene GameInitializer가 참조하는 것과 동일 에셋

    [Header("매칭 연출")]
    [SerializeField] MatchmakingShell    matchShellPrefab;   // 미배선이면 매칭 없이 구 동작
    [SerializeField] OpponentProfilePool profilePool;

    [Header("유효 덱 없음 안내")]
    [SerializeField] LobbyTabController lobbyTabController;
    [SerializeField] int deckTabIndex = 3;   // LobbyTabController.tabs: 0 Shop · 1 Pack · 2 Match · 3 Deck · 4 Collection

    [Header("기능 잠금")]
    [Tooltip("전투 진입 버튼(PlayBtn). 이 컴포넌트가 LobbyPlay 해금 여부로 interactable을 소유한다.\n" +
             "미배선이면 잠김이 화면에 안 드러난다 — 진입 차단 자체는 StartAiBattle이 따로 막는다.")]
    [SerializeField] Button playButton;

    const string BATTLE_SCENE = "BattleScene";

    // 게이트가 열려 있는 동안 PlayBtn 재클릭을 막는다 — 두 번째 진입이 셸의 선택 상태를 덮고,
    // Confirm 한 번에 두 await가 동시에 깨어 LoadScene이 두 번 돈다.
    bool m_running;

    IMatchmaker      m_matchmaker;
    MatchmakingShell m_matchShell;

    // 페이크 → 실제 Photon 매칭 교체는 이 한 줄이 전부다.
    IMatchmaker Matchmaker => m_matchmaker ??= new FakeMatchmaker(aiDeckConfig, profilePool);

    // 로비 캔버스에 미리 얹지 않고 첫 매칭 때 띄운다 — 로비 프리팹을 저장할 때마다 SafeArea가
    // 런타임 계산값으로 굳어(anchorMax) 관계없는 좌표가 함께 커밋된다. 부모는 덱 화면과 같은 SafeArea다.
    MatchmakingShell MatchShell
    {
        get
        {
            if (m_matchShell == null && matchShellPrefab != null)
                m_matchShell = Instantiate(matchShellPrefab, transform.parent);

            return m_matchShell;
        }
    }

    // 튜토리얼 전투는 상대가 시나리오 고정이라 매칭을 태우지 않는다 —
    // 마지막 튜토 전투가 끝나며 TutorialConfig가 꺼지고, 그 다음 판부터 이 문이 열린다.
    bool UseMatchmaking => !TutorialConfig.IsActive && matchShellPrefab != null;

    void OnEnable()
    {
        OutgameFeatureLock.OnChanged += ApplyPlayLock;
        ApplyPlayLock();
    }

    void OnDisable()
    {
        OutgameFeatureLock.OnChanged -= ApplyPlayLock;
    }

    public void StartAiBattle()
    {
        if (m_running) return;

        // 버튼을 죽여 두는 것만으로는 부족하다 — 잠김 표시는 표현 레이어 몫이고, 진입을 실제로 막는 주체는 여기다.
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.LobbyPlay)) return;

        DeckConfig.SetMultiplayer(false);

        // 덱 화면을 거치지 않는 튜토리얼 챕터. 저장된 덱이 아직 없으므로 유효 덱 검사보다 반드시 앞이다.
        if (TutorialConfig.IsActive && !TutorialConfig.ShowDeckGate)
        {
            EnterBattle();
            return;
        }

        // 셸이 세이브 슬롯 좌표로 동작하므로 판정도 세이브 기준이다(DeckConfig는 아직 비어 있어도 된다).
        if (!DeckSaveManager.HasAnyValidSlot())
        {
            ShowNoDeckPopup();
            return;
        }

        RunEntryAsync().Forget();
    }

    // 로비에서 전투로 넘어가는 유일한 문. 세 진입 경로가 여기로 모인다 — 전환 연출을 갈아끼울 때 손댈 자리가 하나여야 한다.
    //
    // m_running을 되돌리지 않는 이유: 커튼이 도는 동안 로비는 그대로 살아 있다. 하드컷 시절엔 그 창이 한 프레임이라
    // 무시할 만했지만, 이제는 그 사이 PlayBtn 재클릭이 덱 화면을 커튼 밑에서 다시 연다(RunEntryAsync의 finally가
    // 이 지점보다 먼저 m_running을 내린다). 로비는 곧 파괴되므로 다시 세운 채 두면 된다.
    void EnterBattle()
    {
        m_running = true;
        CurtainView.LoadScene(BATTLE_SCENE);
    }

    // 진입 체인이 "전투 시작"으로 닫히면 그때 씬을 로드한다. 포기면 각 화면이 스스로 닫고 로비가 그대로 남는다.
    async UniTaskVoid RunEntryAsync()
    {
        var t_ct = this.GetCancellationTokenOnDestroy();

        bool t_confirmed;
        m_running = true;
        try
        {
            t_confirmed = await RunEntryChainAsync(t_ct);
        }
        finally
        {
            m_running = false;
        }

        // 씬이 내려가며 취소된 경우 — 파괴 중인 오브젝트를 건드리지 않는다.
        if (t_ct.IsCancellationRequested) return;

        if (t_confirmed) EnterBattle();
    }

    // 매칭 연출 → 상대 확정 → 출전 덱 확정. 어느 단계든 포기하면 false로 빠져 로비가 그대로 남는다.
    async UniTask<bool> RunEntryChainAsync(CancellationToken _ct)
    {
        MatchOpponent? t_opponent = null;
        if (UseMatchmaking)
        {
            t_opponent = await MatchShell.RunMatchAsync(Matchmaker, _ct);
            if (t_opponent == null) return false;   // 취소 = 로비로 되돌아간다
        }

        ConfirmOpponent(t_opponent);

        if (shell == null)
        {
            Debug.LogWarning("[LobbyMatchLauncher] 덱 화면 미배선 — 첫 유효 덱으로 전투에 진입한다.");

            // 넘어갈 화면이 없으니 매칭 화면은 여기서 스스로 내려간다(전환이 내려 줄 기회가 없다).
            m_matchShell?.Close();

            return TryApplyFirstValidDeck();
        }

        // 매칭을 거치지 않은 경로(튜토리얼)는 옮겨 앉힐 이전 화면이 없다 — 덱 화면이 곧장 뜬다.
        if (t_opponent == null) return await shell.RunSelectionAsync(_ct);

        return await RunSelectionWithHandoffAsync(_ct);
    }

    // 매칭 화면 → 덱 화면. 덱 화면을 매칭 화면 "밑에" 먼저 세운 뒤, 매칭의 세 부품(내 카드·상대 카드·VS)이
    // 새 화면의 제자리로 옮겨 앉는다. 커튼으로 덮지 않는 이유는 두 화면의 축이 이미 같기 때문이다 —
    // 가리면 같은 무대라는 사실이 오히려 지워진다(자세한 규약은 MatchHandoffFx 참고).
    async UniTask<bool> RunSelectionWithHandoffAsync(CancellationToken _ct)
    {
        MatchHandoffTargets t_targets = shell.PrepareForHandoff();

        // 선택 게이트는 전환이 도는 동안 시작해 첫 대기에서 멈춘다 — 전환이 끝난 프레임엔 이미 서 있어야 한다.
        UniTask<bool> t_selection = shell.RunSelectionAsync(_ct);

        await m_matchShell.PlayHandoffAsync(t_targets, _ct);

        return await t_selection;
    }

    // 상대를 전투 전에 확정한다 — 덱 화면의 EnemySection과 실제 전투가 같은 값을 보게 하는 유일한 지점.
    // 튜토리얼은 전투가 TutorialConfig.EnemyDeck으로 초기화되므로(GameInitializer) 여기서 랜덤을 뽑으면
    // 화면에 그린 6장이 실제 상대와 달라진다 — "상대 덱을 미리 확인한다"는 안내가 거짓이 된다.
    void ConfirmOpponent(MatchOpponent? _matched)
    {
        if (TutorialConfig.IsActive && TutorialConfig.EnemyDeck != null)
        {
            MatchOpponentHandoff.Clear();
            DeckConfig.SetEnemyDeck(TutorialConfig.EnemyDeck);
            return;
        }

        if (_matched.HasValue) MatchOpponentHandoff.Set(_matched.Value);
        else                   MatchOpponentHandoff.Clear();

        // 덱 없이 프로필만 온 상대(실제 매칭)는 덱만 폴백을 탄다 — 표시는 매칭한 상대를 그대로 유지한다.
        if (_matched.HasValue && _matched.Value.IsValid)
        {
            DeckConfig.SetEnemyDeck(_matched.Value.Deck);
            return;
        }

        DeckConfig.SetEnemyDeck(aiDeckConfig != null
            ? aiDeckConfig.GetDeckForTier(RankManager.TierIndex)
            : new List<CardData>());
    }

    void ApplyPlayLock()
    {
        if (playButton != null) playButton.interactable = OutgameFeatureLock.IsUnlocked(EOutgameFeature.LobbyPlay);
    }

    void ShowNoDeckPopup()
    {
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "유효한 덱이 없습니다.\n덱을 먼저 구성해 주세요.",
            yesText   = "덱 편성",
            noText    = "닫기",
            yesAction = GoToDeckTab,
        });
    }

    void GoToDeckTab()
    {
        lobbyTabController?.Select(deckTabIndex);
    }

    // 셸 미배선 폴백 전용. 저장된 슬롯 중 첫 유효 덱을 DeckConfig에 적용하고, 없으면 false.
    static bool TryApplyFirstValidDeck()
    {
        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
        {
            if (!DeckSaveManager.IsSlotValid(t_i)) continue;

            DeckConfig.Set(DeckSaveManager.Load(t_i));
            return true;
        }
        return false;
    }
}
