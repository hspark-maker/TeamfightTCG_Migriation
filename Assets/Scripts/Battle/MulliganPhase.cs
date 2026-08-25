using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 후공 어드밴티지 멀리건: 후공 플레이어가 전투 시작 시 자기 필드 슬롯 카드 1장을 골라
/// 덱(대기열)의 무작위 카드와 교환한다. 전투 시작 1회, 첫 턴 시작 전(TurnRunner.PlayIntroAndStart).
///
/// 멀티는 후공 owner 클라이언트가 슬롯 선택(+스킵)을 RPC로 전파하고,
/// 양쪽이 같은 선택을 확정한 뒤 MatchRandom을 동일하게 소비한다.
/// </summary>
public static class MulliganPhase
{
    /// <summary>멀리건 단계 실행. _firstOwner=선공 ownerIndex(0=플레이어팀, 1=적팀).
    /// _ct=씬 파괴/이탈 시 사람 선택 대기를 깨는 취소 토큰(TurnRunner 수명).</summary>
    public static async UniTask Run(TurnContext _ctx, int _firstOwner, CancellationToken _ct)
    {
        // 튜토리얼/멀티는 스킵(스코프 밖). 스킵도 양측 대칭이라 RNG 스트림 교란 없음(draw 자체를 안 함).
        if (TutorialConfig.IsActive) return;
        if (DeckConfig.IsMultiplayer && DeckConfig.AiTakeover) return;

        int t_secondOwner = 1 - _firstOwner;
        bool t_secondIsLocal = DeckConfig.IsMultiplayer
            ? TurnState.IsLocalTurn(t_secondOwner)
            : t_secondOwner == 0;

        BattleField t_field = _ctx.playerField.OwnerIndex == t_secondOwner
            ? _ctx.playerField : _ctx.enemyField;
        BattleFieldView t_view = _ctx.playerField.OwnerIndex == t_secondOwner
            ? _ctx.playerFieldView : _ctx.enemyFieldView;

        if (t_field == null || t_view == null) return;
        if (t_field.WaitingCount == 0) return;   // 교환할 덱 카드 없음 — no-op(양측 대칭).

        // 열린 덱 패널이 멀리건 선택 입력을 가리지 않게 닫는다.
        DeckPileUI.CloseAny();

        // 슬롯 선택: 후공이 플레이어면 사람 입력(스킵 가능), AI면 결정론 휴리스틱.
        int t_slot;
        if (DeckConfig.IsMultiplayer && t_secondIsLocal)
        {
            t_slot = await WaitPlayerSelect(t_field, _ctx, _ct, NetTimeouts.MulliganPickSec);
            if (_ct.IsCancellationRequested || DeckConfig.AiTakeover) return;
            NetworkGameController t_network = NetworkGameController.Instance;
            if (t_network == null)
            {
                TurnRunner.Instance?.AbortMatch(EMatchEndReason.Timeout);
                return;
            }
            t_network.SendMulliganChoice(t_slot);
        }
        else if (DeckConfig.IsMultiplayer)
        {
            var (t_received, t_remoteSlot) = await WaitOpponentChoice(_ctx, _ct);
            if (!t_received)
            {
                if (_ct.IsCancellationRequested || DeckConfig.AiTakeover) return;
                TurnRunner.Instance?.AbortMatch(EMatchEndReason.Timeout);
                return;
            }
            t_slot = t_remoteSlot;
        }
        else if (t_secondIsLocal)
        {
            t_slot = await WaitPlayerSelect(t_field, _ctx, _ct);
        }
        else
        {
            t_slot = PickAiSlot(t_field);
            // 상대(AI)가 멀리건을 쓴다는 사실을 잠깐 보여준다 — 안 그러면 카드가 이유 없이 바뀐 것처럼 보인다.
            if (t_slot >= 0) await ShowAiNotice(t_field, t_slot, _ctx, _ct);
        }
        if (t_slot < 0) return;   // 스킵/취소/무효 — 교환 없음(draw 미소비).

        // 나가는 카드의 뷰는 **스왑 전에** 잡아 둔다 — 스왑이 끝나면 그 슬롯의 카드가 바뀌어
        // CardView.GetView(t_out)로는 더 이상 찾을 수 없다.
        CardInstance t_out     = t_field.GetSlot(t_slot);
        CardView     t_outView = t_out != null ? CardView.GetView(t_out) : null;

        // 무작위 덱 카드 인덱스(결정론). 슬롯 선택 확정 뒤 1회 소비.
        int t_deckIndex = MatchRandom.Range(t_field.WaitingCount);
        CardInstance t_in = t_field.MulliganSwap(t_slot, t_deckIndex);
        if (t_in == null) return;

        // 교체된 카드가 덱으로 물러난다(교활 교대와 같은 그림, 안개만 뺀다).
        // **Refresh 전에** 불러야 한다 — 스왑은 끝났지만 슬롯 뷰는 아직 나가는 카드를 그리고 있고,
        // 이 창을 놓치면 새로 들어온 카드가 대신 돌아 나가는 그림이 된다(교활 호출 규약과 동일).
        // isRevealed는 MulliganSwap이 이미 false로 만들어 뒀다 — 연출이 상태를 만들지 않는다.
        if (t_outView != null) await CunningVfx.PlayExit(t_outView, _withFog: false);

        // 연출: 교체 표시(Refresh) 후 새 카드만 딜 애니(FillAndAnimate와 동형).
        t_view.Refresh();
        await t_view.PlayFillAnim(new List<CardInstance> { t_in });
    }

    /// <summary>후공 플레이어가 자기 슬롯 카드 1장을 탭(또는 스킵)할 때까지 대기. 선택 슬롯 인덱스, 스킵이면 -1.
    /// 정상 턴 입력(TurnState.InputAllowed / 드래그-공격)과 무관하게 직접 raycast로 받는다 —
    /// 이 시점엔 아직 어떤 턴도 시작 전이라 CardView 입력 경로가 닫혀 있음.</summary>
    static async UniTask<int> WaitPlayerSelect(BattleField _field, TurnContext _ctx, CancellationToken _ct,
                                                float _timeoutSec = 0f)
    {
        // 대상 강조: 나머지 암전 + 후공 슬롯 카드만 밝게+하이라이트.
        var t_targets = new List<CardView>();
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardInstance t_c = _field.GetSlot(i);
            if (t_c == null) continue;
            CardView t_cv = CardView.GetView(t_c);
            if (t_cv != null) t_targets.Add(t_cv);
        }
        if (t_targets.Count == 0) return -1;

        CardView.FadeAll(0.3f);
        CardView.FadeCards(1f, t_targets.ToArray());
        foreach (CardView t_cv in t_targets) t_cv.SetHighlight(true);

        MulliganOverlayUI t_ui = _ctx?.mulliganOverlay;
        t_ui?.Show("교환할 카드를 선택하세요");

        // 고를 필드만 남기고 나머지를 덮는다(튜토리얼 필드 포커스와 같은 그림).
        // 구멍은 슬롯 격자 기준이라 카드가 비어 있어도 자리가 흔들리지 않는다.
        BattleFieldView t_focusView = _ctx != null && _ctx.playerField == _field
            ? _ctx.playerFieldView : _ctx?.enemyFieldView;
        if (t_focusView != null) t_ui?.SetFocusHole(t_focusView.ScreenBounds());

        int t_chosen = -1;
        float t_deadline = _timeoutSec > 0f
            ? Time.realtimeSinceStartup + _timeoutSec
            : float.PositiveInfinity;
        try
        {
            while (true)
            {
                // 토큰 취소(씬 파괴/이탈) 시 throw 없이 루프 종료 → finally에서 페이드/UI 정리.
                bool t_cancelled = await UniTask.Yield(PlayerLoopTiming.Update, _ct).SuppressCancellationThrow();
                if (t_cancelled) { t_chosen = -1; break; }
                if (DeckConfig.IsMultiplayer && DeckConfig.AiTakeover) break;
                if (Time.realtimeSinceStartup >= t_deadline)
                {
                    Debug.Log($"[Net] 멀리건 선택이 {_timeoutSec}초를 넘겨 자동 스킵한다.");
                    break;
                }

                if (t_ui != null && t_ui.SkipPressed) { t_chosen = -1; break; }      // 스킵 = 교환 없음.
                if (!Input.GetMouseButtonDown(0)) continue;
                if (Camera.main == null) continue;
                // UI(스킵 버튼) 위 클릭은 카드 선택으로 처리하지 않음.
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) continue;

                Vector3 t_wp = Camera.main.ScreenToWorldPoint(new Vector3(
                    Input.mousePosition.x, Input.mousePosition.y, -Camera.main.transform.position.z));
                Collider2D t_hit = Physics2D.OverlapPoint(t_wp);
                if (t_hit == null) continue;

                CardView t_cv = t_hit.GetComponentInParent<CardView>();
                if (t_cv == null || t_cv.BoundCard == null) continue;

                int t_slot = t_cv.BoundCard.slotIndex;
                if (t_slot < 0 || t_slot >= BattleField.SLOT_COUNT) continue;
                if (_field.GetSlot(t_slot) != t_cv.BoundCard) continue;   // 후공 필드의 슬롯 카드만 유효

                t_chosen = t_slot;
                break;
            }
        }
        finally
        {
            t_ui?.Hide();
            foreach (CardView t_cv in t_targets)
                if (t_cv != null) t_cv.SetHighlight(false);
            CardView.RestoreAllFades();
        }
        return t_chosen;
    }

    /// <summary>멀티 선공 측이 상대의 멀리건 선택 패킷을 기다리는 동안 기존 오버레이를 표시한다.</summary>
    static async UniTask<(bool received, int slot)> WaitOpponentChoice(TurnContext _ctx, CancellationToken _ct)
    {
        MulliganOverlayUI t_ui = _ctx?.mulliganOverlay;
        t_ui?.Show("상대가 카드를 교환하고 있습니다.", _showSkip: false);
        try
        {
            if (_ct.IsCancellationRequested || NetworkGameController.Instance == null)
                return (false, -1);
            return await NetworkGameController.Instance.WaitForOpponentMulliganChoice();
        }
        finally
        {
            t_ui?.Hide();
        }
    }

    static async UniTask ShowAiNotice(BattleField _field, int _slot, TurnContext _ctx, CancellationToken _ct)
    {
        CardView t_target = CardView.GetView(_field.GetSlot(_slot));

        CardView.FadeAll(0.3f);
        if (t_target != null)
        {
            CardView.FadeCards(1f, t_target);
            t_target.SetHighlight(true);
            t_target.PlayAttentionPulse();
        }

        MulliganOverlayUI t_ui = _ctx?.mulliganOverlay;
        t_ui?.Show("상대가 카드를 교환합니다", _showSkip: false);   // 구멍 없음 — 고를 게 없으니 전체를 덮지 않는다
        try
        {
            await UniTask.Delay((int)(GameTiming.Battle.MulliganNoticeHold * 1000), cancellationToken: _ct)
                         .SuppressCancellationThrow();
        }
        finally
        {
            t_ui?.Hide();
            if (t_target != null) t_target.SetHighlight(false);
            CardView.RestoreAllFades();
        }
    }

    /// <summary>AI 후공 슬롯 선택(결정론, RNG 미소비). 가장 약한 카드(현재 hp 최소, 동률이면 낮은 슬롯) 교체.</summary>
    static int PickAiSlot(BattleField _field)
    {
        int t_best = -1;
        int t_bestHp = int.MaxValue;
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardInstance t_c = _field.GetSlot(i);
            if (t_c == null) continue;
            if (t_c.hp < t_bestHp) { t_bestHp = t_c.hp; t_best = i; }
        }
        return t_best;
    }
}
