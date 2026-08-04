using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 후공 어드밴티지 멀리건: 후공 플레이어가 전투 시작 시 자기 필드 슬롯 카드 1장을 골라
/// 덱(대기열)의 무작위 카드와 교환한다. 전투 시작 1회, 첫 턴 시작 전(TurnRunner.PlayIntroAndStart).
///
/// 현재 스코프: 싱글 전용(멀티는 DecideFirstPlayer가 선/후공 고정이라 멀리건이 발동하지 않음).
/// 멀티 확장 시 슬롯 선택(+스킵)을 RPC로 전파하고 apply(MulliganSwap)만 공유하면 됨 —
/// 무작위 추출은 이미 MatchRandom(공유시드) 기반이라 결정론 안전.
/// </summary>
public static class MulliganPhase
{
    /// <summary>멀리건 단계 실행. _firstOwner=선공 ownerIndex(0=플레이어팀, 1=적팀).
    /// _ct=씬 파괴/이탈 시 사람 선택 대기를 깨는 취소 토큰(TurnRunner 수명).</summary>
    public static async UniTask Run(TurnContext _ctx, int _firstOwner, CancellationToken _ct)
    {
        // 튜토리얼/멀티는 스킵(스코프 밖). 스킵도 양측 대칭이라 RNG 스트림 교란 없음(draw 자체를 안 함).
        if (TutorialConfig.IsActive || DeckConfig.IsMultiplayer) return;

        int t_secondOwner = 1 - _firstOwner;
        bool t_secondIsPlayer = t_secondOwner == 0;

        BattleField t_field = _ctx.playerField.OwnerIndex == t_secondOwner
            ? _ctx.playerField : _ctx.enemyField;
        BattleFieldView t_view = _ctx.playerField.OwnerIndex == t_secondOwner
            ? _ctx.playerFieldView : _ctx.enemyFieldView;

        if (t_field == null || t_view == null) return;
        if (t_field.WaitingCount == 0) return;   // 교환할 덱 카드 없음 — no-op(양측 대칭).

        // 슬롯 선택: 후공이 플레이어면 사람 입력(스킵 가능), AI면 결정론 휴리스틱.
        int t_slot;
        if (t_secondIsPlayer)
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
    static async UniTask<int> WaitPlayerSelect(BattleField _field, TurnContext _ctx, CancellationToken _ct)
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

        var t_ui = new MulliganOverlay(_ctx?.turnLabel != null ? _ctx.turnLabel.font : null);

        // 고를 필드만 남기고 나머지를 덮는다(튜토리얼 필드 포커스와 같은 그림).
        // 구멍은 슬롯 격자 기준이라 카드가 비어 있어도 자리가 흔들리지 않는다.
        BattleFieldView t_focusView = _ctx != null && _ctx.playerField == _field
            ? _ctx.playerFieldView : _ctx?.enemyFieldView;
        if (t_focusView != null) t_ui.SetFocusHole(t_focusView.ScreenBounds());

        int t_chosen = -1;
        try
        {
            while (true)
            {
                // 토큰 취소(씬 파괴/이탈) 시 throw 없이 루프 종료 → finally에서 페이드/UI 정리.
                bool t_cancelled = await UniTask.Yield(PlayerLoopTiming.Update, _ct).SuppressCancellationThrow();
                if (t_cancelled) { t_chosen = -1; break; }

                if (t_ui.SkipPressed) { t_chosen = -1; break; }      // 스킵 = 교환 없음.
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
            t_ui.Destroy();
            foreach (CardView t_cv in t_targets)
                if (t_cv != null) t_cv.SetHighlight(false);
            CardView.RestoreAllFades();
        }
        return t_chosen;
    }

    /// <summary>상대(AI) 멀리건 예고: 교체될 카드만 밝게+하이라이트하고 안내 문구를 잠깐 띄운다.
    /// 순수 연출 — RNG 미소비, 상태 변경 없음(스왑은 호출부가 이 대기 후 수행). 취소 시 즉시 정리하고 빠진다.</summary>
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

        var t_ui = new MulliganOverlay(_ctx?.turnLabel != null ? _ctx.turnLabel.font : null,
            "상대가 카드를 교환합니다", _showSkip: false);
        try
        {
            await UniTask.Delay((int)(GameTiming.Battle.MulliganNoticeHold * 1000), cancellationToken: _ct)
                         .SuppressCancellationThrow();
        }
        finally
        {
            t_ui.Destroy();
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

    // ── 임시 오버레이 UI(코드 생성, 프리팹 없음). 멀리건 기능 삭제 시 이 클래스도 함께 삭제. ──
    // 안내 텍스트 + 스킵 버튼. 스크린 스페이스 오버레이 캔버스 1개.
    class MulliganOverlay
    {
        readonly GameObject root;
        public bool SkipPressed { get; private set; }

        // _showSkip=false면 안내 문구만(상대 멀리건 예고처럼 입력이 없는 표시용).
        public MulliganOverlay(TMP_FontAsset _font, string _message = "교환할 카드를 선택하세요", bool _showSkip = true)
        {
            EnsureEventSystem();

            this.root = new GameObject("MulliganOverlay");
            var t_canvas = this.root.AddComponent<Canvas>();
            t_canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            t_canvas.sortingOrder = 999;
            var t_scaler = this.root.AddComponent<CanvasScaler>();
            t_scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            t_scaler.referenceResolution = new Vector2(1080, 1920);
            t_scaler.matchWidthOrHeight  = 0.5f;
            this.root.AddComponent<GraphicRaycaster>();

            TMP_FontAsset t_f = _font != null ? _font : TMP_Settings.defaultFontAsset;

            // 안내 텍스트(상단).
            CreateText("Instruction", _message, t_f, 48,
                new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(900f, 120f));

            // 스킵 버튼(하단).
            if (_showSkip) CreateSkipButton(t_f);
        }

        /// <summary>고를 필드만 남기고 화면 나머지를 덮는다(튜토리얼 필드 포커스와 같은 그림).
        ///
        /// 구멍은 딤 네 장(위·아래·좌·우)으로 만든다 — 뚫린 영역에는 아무것도 없으므로 그쪽 클릭은
        /// 그대로 카드까지 내려간다. 카드는 월드 오브젝트고 이 캔버스가 그 위(sortingOrder 999)라
        /// 카드 스프라이트를 따로 어둡게 하지 않아도 바깥은 전부 덮인다.</summary>
        public void SetFocusHole(Rect _screenRect)
        {
            if (_screenRect.width <= 0f || _screenRect.height <= 0f) return;   // 잡히지 않은 영역이면 덮지 않는다

            const float k_pad = 24f;   // 카드 프레임 장식이 딤에 물리지 않을 여유(px)

            // **정규화(0~1) 앵커로 배치한다.** 이 캔버스엔 CanvasScaler가 걸려 있어 anchoredPosition/sizeDelta는
            // 스크린 픽셀이 아니라 레퍼런스 해상도 단위다 — 픽셀을 그대로 넣으면 배율만큼 어긋난다.
            float t_left   = Mathf.Clamp01((_screenRect.xMin - k_pad) / Screen.width);
            float t_right  = Mathf.Clamp01((_screenRect.xMax + k_pad) / Screen.width);
            float t_bottom = Mathf.Clamp01((_screenRect.yMin - k_pad) / Screen.height);
            float t_top    = Mathf.Clamp01((_screenRect.yMax + k_pad) / Screen.height);

            CreateDim("Dim_Top",    new Vector2(0f, t_top),      new Vector2(1f, 1f));
            CreateDim("Dim_Bottom", new Vector2(0f, 0f),         new Vector2(1f, t_bottom));
            CreateDim("Dim_Left",   new Vector2(0f, t_bottom),   new Vector2(t_left, t_top));
            CreateDim("Dim_Right",  new Vector2(t_right, t_bottom), new Vector2(1f, t_top));
        }

        /// <summary>_min/_max는 화면 비율(0~1). 오프셋을 0으로 두면 해상도·스케일러와 무관하게 그 영역을 덮는다.</summary>
        void CreateDim(string _name, Vector2 _min, Vector2 _max)
        {
            if (_max.x - _min.x <= 0.0001f || _max.y - _min.y <= 0.0001f) return;   // 두께 0이면 만들 이유가 없다

            var t_go = new GameObject(_name);
            t_go.transform.SetParent(this.root.transform, false);
            t_go.transform.SetAsFirstSibling();   // 안내 문구·스킵 버튼보다 뒤에

            var t_img = t_go.AddComponent<Image>();
            t_img.color = new Color(0f, 0f, 0f, 0.62f);
            t_img.raycastTarget = true;           // 딤 위 클릭이 카드로 새지 않게

            var t_rt = t_img.rectTransform;
            t_rt.anchorMin = _min;
            t_rt.anchorMax = _max;
            t_rt.offsetMin = Vector2.zero;
            t_rt.offsetMax = Vector2.zero;
        }

        void CreateText(string _name, string _msg, TMP_FontAsset _font, float _size,
            Vector2 _anchor, Vector2 _pos, Vector2 _sizeDelta)
        {
            var t_go = new GameObject(_name);
            t_go.transform.SetParent(this.root.transform, false);
            var t_txt = t_go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t_txt.font = _font;
            t_txt.text      = _msg;
            t_txt.fontSize  = _size;
            t_txt.color     = Color.white;
            t_txt.alignment = TextAlignmentOptions.Center;
            t_txt.enableWordWrapping = false;
            var t_rt = t_txt.rectTransform;
            t_rt.anchorMin = _anchor; t_rt.anchorMax = _anchor; t_rt.pivot = _anchor;
            t_rt.anchoredPosition = _pos;
            t_rt.sizeDelta        = _sizeDelta;
        }

        void CreateSkipButton(TMP_FontAsset _font)
        {
            var t_go = new GameObject("SkipButton");
            t_go.transform.SetParent(this.root.transform, false);
            var t_img = t_go.AddComponent<Image>();
            t_img.color = new Color(0f, 0f, 0f, 0.6f);
            var t_rt = t_img.rectTransform;
            t_rt.anchorMin = new Vector2(0.5f, 0f); t_rt.anchorMax = new Vector2(0.5f, 0f); t_rt.pivot = new Vector2(0.5f, 0f);
            t_rt.anchoredPosition = new Vector2(0f, 220f);
            t_rt.sizeDelta        = new Vector2(300f, 110f);

            var t_btn = t_go.AddComponent<Button>();
            t_btn.onClick.AddListener(() => this.SkipPressed = true);

            var t_lblGo = new GameObject("Label");
            t_lblGo.transform.SetParent(t_go.transform, false);
            var t_lbl = t_lblGo.AddComponent<TextMeshProUGUI>();
            if (_font != null) t_lbl.font = _font;
            t_lbl.text      = "스킵";
            t_lbl.fontSize  = 44;
            t_lbl.color     = Color.white;
            t_lbl.alignment = TextAlignmentOptions.Center;
            var t_lrt = t_lbl.rectTransform;
            t_lrt.anchorMin = Vector2.zero; t_lrt.anchorMax = Vector2.one;
            t_lrt.offsetMin = Vector2.zero; t_lrt.offsetMax = Vector2.zero;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var t_go = new GameObject("EventSystem");
            t_go.AddComponent<EventSystem>();
            t_go.AddComponent<StandaloneInputModule>();
        }

        public void Destroy()
        {
            if (this.root != null) Object.Destroy(this.root);
        }
    }
}
