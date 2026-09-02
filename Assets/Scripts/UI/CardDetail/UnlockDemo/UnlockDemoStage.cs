using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 방금 열린 능력이 발동하는 모습을 전용 카메라 + RenderTexture로 재생하는 작은 무대(대본은 키워드 갈래와 시너지 갈래 둘).
// 규칙은 돌리지 않는다 — 빈 BattleEvent 목록으로 모션·파티클만 재생하고, 숫자는 CardView의 표시 전용 API로만 낸다.
public class UnlockDemoStage : SingletonOverlayBase, IUnlockDemoStage
{
    // 로비 카메라의 시야 밖(UiRectCapture와 같은 자리).
    static readonly Vector3 StageOrigin = new Vector3(0f, 20000f, 0f);

    static UnlockDemoStage s_instance;

    static int s_stageSerial;

    [Header("무대 배선")]
    [Tooltip("이 무대만 담는 카메라. tag는 반드시 Untagged — MainCamera를 달면 로비 배경 영상이 이쪽으로 튄다.")]
    [SerializeField] Camera demoCamera;

    [Tooltip("앞자리. 방금 강화한 그 카드가 선다 — 대개 공격자이고, 도발·비늘·수호자에서만 **맞는 쪽**이 된다.")]
    [SerializeField] CardView slotAttacker;

    [Tooltip("맞은편. 대개 맞는 쪽이고, 도발에서만 치러 오는 쪽이 된다.")]
    [SerializeField] CardView slotDefender;

    [Tooltip("윗줄 곁자리. **적 자리다** — 무쌍의 광역 대상이 여기 선다.")]
    [SerializeField] CardView slotNeighbor;

    [Tooltip("아랫줄 곁자리. **아군 자리다** — 도발이 지키는 아군·힐러가 살리는 아군·시너지 동료가 여기 선다.\n" +
             "윗줄과 돌려쓸 수 없다: 자리는 CardAnimator가 첫 활성 프레임에 못 박고, 진영을 가르는 단서는 줄(y)뿐이다.")]
    [SerializeField] CardView slotAlly;

    [SerializeField] KeywordDemoConfig config;

    [Header("텍스처")]
    [Tooltip("비율은 띠 모양을 따라간다 — 그 자리가 거의 정사각이라 가로세로를 같게 둔다(RawImage의 " +
             "AspectRatioFitter가 이 비율을 그대로 읽는다).\n" +
             "해상도는 띠가 화면 폭의 약 78%(1080 기준 840px)를 먹는 데서 온다 — 그보다 작으면 숫자가 뭉갠다.")]
    [SerializeField] int textureWidth  = 1024;
    [SerializeField] int textureHeight = 1024;

    [Header("박자")]
    [Tooltip("한 판이 끝나고 다시 돌기까지의 뜸.")]
    [SerializeField] float loopGap = 1.1f;

    [Tooltip("무대가 서고 첫 판이 시작하기까지 — 모달 페이드인 동안을 비워 둔다.")]
    [SerializeField] float startDelay = 0.35f;

    RenderTexture m_texture;

    CancellationTokenSource m_loop;

    // 무대가 잠시 빌려 쓰는 전역 — 전투가 읽는 값이라 반드시 되돌린다.
    int  m_ownerIndex0;
    bool m_inputAllowed0;

    // 되돌리기는 한 번뿐이어야 한다 — 두 번 덮으면 그 사이 시작한 전투의 상태를 걷어찬다.
    bool m_borrowed;

    // AttackSequence는 취소 토큰을 받지 않아 도는 판을 중간에 끊을 수 없다.
    bool m_playing;

    // 판이 도는 중에 들어온 걷기 지시 — 부수는 일은 그 판이 풀린 뒤 RunLoop이 한다.
    bool m_disposePending;

    // 이번 무대가 안내하는 카드. 배역과 달리 대본이 고르는 값이 아니다.
    int m_cardId;

    // 이번 무대의 배역. 자리가 곧 진영이다 — 윗줄은 적, 아랫줄은 아군.
    UnlockDemoCast m_cast;

    /// <summary>무대를 세운다. 위치는 Instantiate 인자로 준다 — 세운 뒤 옮기면 카드가 공격하고 원점으로 돌아간다.</summary>
    public static bool TryGet(out UnlockDemoStage _stage)
    {
        if (s_instance == null)
        {
            var t_prefab = RuntimeOverlayPrefabs.Get<UnlockDemoStage>();
            if (t_prefab != null)
            {
                // 아직 못 부순 무대와 같은 자리에 세우면 새 카메라가 죽어가는 카드까지 함께 비춘다.
                Vector3 t_origin = StageOrigin + new Vector3(1000f * (s_stageSerial++ & 7), 0f, 0f);

                GameObject t_go = Instantiate(t_prefab, t_origin, Quaternion.identity);
                s_instance = t_go.GetComponent<UnlockDemoStage>();

                if (s_instance == null)
                {
                    Debug.LogWarning($"[UnlockDemoStage] {t_prefab.name} 루트에 UnlockDemoStage가 없습니다(프리팹 배선 확인).");
                    Destroy(t_go);
                }
            }
        }

        _stage = s_instance;
        return _stage != null;
    }

    /// <summary>_card가 _keyword를 쓰는 모습을 반복 재생하고 그 텍스처를 돌려준다. 세울 수 없으면 null.</summary>
    public Texture Begin(int _card, CardKeyword _keyword) => BeginCore(_card, UnlockDemoScriptTable.For(_keyword));

    /// <summary>_card가 속한 _synergy가 일하는 모습을 반복 재생하고 그 텍스처를 돌려준다. 연출 에셋이 없으면 null.</summary>
    public Texture Begin(int _card, SynergyData _synergy) => BeginCore(_card, UnlockDemoScriptTable.For(_synergy));

    // 순서가 계약이다 — TurnState.LocalOwnerIndex를 배역보다 먼저 세워야 Render와 VFX 오프셋·회전이 같은 방향을 본다.
    Texture BeginCore(int _card, IUnlockDemoScript _script)
    {
        if (_card <= 0 || _script == null || this.demoCamera == null || this.slotAttacker == null) return null;

        Stop();

        // 빌리기는 한 번만 기록한다 — 두 번째면 내가 바꿔 놓은 값을 원본으로 적게 된다.
        if (!this.m_borrowed)
        {
            this.m_ownerIndex0   = TurnState.LocalOwnerIndex;
            this.m_inputAllowed0 = TurnState.InputAllowed;
            this.m_borrowed      = true;
        }

        // 공격자가 아군 기준이어야 VFX 오프셋·회전이 전투와 같은 방향으로 선다.
        TurnState.LocalOwnerIndex = 0;

        // 데모 카드에도 콜라이더가 살아 있다 — 끄지 않으면 화면 밖 무대에 대고 공격 입력이 먹는다.
        TurnState.InputAllowed = false;

        if (!_script.TryBuildCast(_card, this.config, out UnlockDemoCast t_cast)) { Restore(); return null; }

        this.m_cardId = _card;
        this.m_cast   = t_cast;
        ApplyRoles();

        EnsureTexture();
        this.demoCamera.targetTexture = this.m_texture;
        this.demoCamera.enabled       = true;

        this.m_loop = new CancellationTokenSource();
        RunLoop(_script, this.m_loop.Token).Forget();

        return this.m_texture;
    }

    /// <summary>무대를 걷는다. 텍스처가 해제되므로 부른 쪽은 RawImage에서 먼저 떼야 한다.</summary>
    public void End()
    {
        Stop();
        Restore();
        ReleaseTexture();

        // 왕관은 카드의 자식이 아니라 월드 오브젝트라, 안 걷으면 무대 자리에 남는다.
        LegacyCrownVfx.Clear();

        // 텍스처만 떼면 이 카메라가 화면에 직접 그린다.
        if (this.demoCamera != null) this.demoCamera.enabled = false;

        // 안 비우면 다음 Begin이 부서지기를 기다리는 이 무대를 다시 잡는다.
        if (s_instance == this) s_instance = null;

        if (this.m_playing) { this.m_disposePending = true; return; }

        // 남겨두면 BattleBoardView 레지스트리에 남아, 전투에서 CardView.FadeAll이 이 넷까지 흐리게 만든다.
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;

        Stop();
        Restore();
        ReleaseTexture();
        LegacyCrownVfx.Clear();
    }

    // ── 대본이 보는 무대 ────────────────────────────────────────────────

    public CardView Attacker => this.slotAttacker;
    public CardView Defender => this.slotDefender;

    /// <summary>윗줄 곁자리(적). 이번 배역이 안 세웠으면 null.</summary>
    public CardView Neighbor => Staged(this.slotNeighbor);

    /// <summary>아랫줄 곁자리(아군). 이번 배역이 안 세웠으면 null.</summary>
    public CardView Ally => Staged(this.slotAlly);

    // 곁자리 둘만 배역에 따라 켜고 끈다 — 앞자리와 맞은편은 ApplyRoles가 SetActive하지 않는다.
    static CardView Staged(CardView _view)
        => _view != null && _view.gameObject.activeSelf ? _view : null;

    // 공격 한 번. 빈 이벤트 목록과 _forceSpecial: false가 이 무대의 규약이다 — 체력을 안 바꾸고 시네마를 끈다.
    public UniTask Swing(CardView _atk, CardView _def, CardView _splash, CancellationToken _token,
                         Func<UniTask> _afterHit = null)
    {
        if (_token.IsCancellationRequested) return UniTask.CompletedTask;

        // Neighbor 프로퍼티가 이미 거르지만 여기도 남긴다 — 인터페이스가 공개된 이상 이쪽이 마지막 방어선이다.
        CardView t_splashView = _splash != null && _splash.gameObject.activeSelf ? _splash : null;

        return AttackSequence.PlaySplash(_atk, _def,
                                         _events: Array.Empty<TeamfightTCG.BattleCore.BattleEvent>(), _splashView: t_splashView,
                                         _afterHit: _afterHit,
                                         _forceSpecial: false);
    }

    public async UniTask Hold(float _seconds, CancellationToken _token)
    {
        await UniTask.Delay(Ms(_seconds), cancellationToken: _token).SuppressCancellationThrow();
    }

    // ── 배역 ────────────────────────────────────────────────────────────

    // 배역을 무대에 세운다. 매 바퀴 다시 불러 앞 바퀴의 표기 조작·보호막 표시를 씻어낸다(진영은 슬롯이 정한다).
    void ApplyRoles()
    {
        Render(this.slotAttacker, this.m_cardId,          0, 0, this.m_cast.ShowKeyword, this.m_cast.ShowSynergy);
        Render(this.slotDefender, this.m_cast.OpponentId, 1, 1);

        if (this.slotNeighbor != null)
        {
            this.slotNeighbor.gameObject.SetActive(this.m_cast.UsesNeighbor);
            if (this.m_cast.UsesNeighbor) Render(this.slotNeighbor, this.m_cast.NeighborId, 1, 2);
        }

        if (this.slotAlly != null)
        {
            this.slotAlly.gameObject.SetActive(this.m_cast.UsesAlly);

            // 시너지 축은 동료에게도 간다 — 키워드 축은 넘기지 않는다(지켜지는 쪽은 그 능력의 주인이 아니다).
            if (this.m_cast.UsesAlly) Render(this.slotAlly, this.m_cast.CompanionId, 0, 2, CardKeyword.None, this.m_cast.ShowSynergy);
        }

        // 보호막은 뷰에 얹은 표시라 Render가 걷어가지 않는다.
        if (this.slotAttacker != null) this.slotAttacker.SetShieldVisible(false);
        if (this.m_cast.UsesAlly && this.slotAlly != null) this.slotAlly.SetShieldVisible(false);
    }

    // 카드 한 장을 세운다. _keyword·_synergy는 이 카드가 가진 능력이 아니라 **표시할** 능력이다.
    static void Render(CardView _view, int _data, int _owner, int _slot,
                       CardKeyword _keyword = CardKeyword.None, SynergyState _synergy = null)
    {
        if (_view == null || _data <= 0) return;

        var t_card = new CardInstance(_data, _owner) { isRevealed = true, slotIndex = _slot };

        // 안내창이 말하는 그 하나만 남긴다 — 공격 모션까지 이 축을 따른다(Swing·AttackSequence가 BoundCard의 키워드를 읽는다).
        // 단 아이콘 줄에서 빠지는 키워드로 좁히면 KeywordIconConfig 기본 아이콘이 폴백으로 떠 되레 어긋난다.
        const CardKeyword ICONLESS = CardVisualRules.IconRowExcluded | CardVisualRules.AlwaysStatus;
        if ((_keyword & ~ICONLESS) != CardKeyword.None) t_card.unlockedKeywords = _keyword;

        _view.InitializeAnimator();   // 초기화(GameInitializer)이 없는 씬이라 직접 깨운다
        _view.Render(t_card, _synergy);
    }

    // ── 재생 ────────────────────────────────────────────────────────────

    async UniTaskVoid RunLoop(IUnlockDemoScript _script, CancellationToken _token)
    {
        // finally로 감싸는 이유는 대본이 규약을 어기고 예외를 던지는 경우다 — 그때도 미뤄 둔 파괴가 일어나야 한다.
        try
        {
            await UniTask.Delay(Ms(this.startDelay), cancellationToken: _token).SuppressCancellationThrow();

            while (!_token.IsCancellationRequested)
            {
                // 한 판은 끊을 수 없다 — End가 오면 그쪽이 부수기를 미루고, 판이 풀린 뒤 이 루프가 마무리한다.
                this.m_playing = true;
                await PlayOnce(_script, _token);
                this.m_playing = false;

                if (_token.IsCancellationRequested) break;

                await UniTask.Delay(Ms(this.loopGap), cancellationToken: _token).SuppressCancellationThrow();
                if (_token.IsCancellationRequested) break;

                ApplyRoles();
            }
        }
        finally
        {
            this.m_playing = false;
            if (this.m_disposePending) Destroy(gameObject);
        }
    }

    // 배역이 안 섰으면 대본을 부르지 않는다 — 대본마다 이 검사를 다시 쓰게 두면 열여섯 벌로 복제된다.
    UniTask PlayOnce(IUnlockDemoScript _script, CancellationToken _token)
    {
        if (this.slotAttacker == null || this.slotDefender == null || this.slotAttacker.BoundCard == null)
            return UniTask.CompletedTask;

        return _script.PlayAsync(this, _token);
    }

    // ── 수명 ────────────────────────────────────────────────────────────

    void EnsureTexture()
    {
        int t_w = Mathf.Max(64, this.textureWidth);
        int t_h = Mathf.Max(64, this.textureHeight);

        if (this.m_texture != null && this.m_texture.width == t_w && this.m_texture.height == t_h) return;

        ReleaseTexture();

        // 깊이 24는 선택이 아니다 — URP Render Graph가 depth stencil 없는 타깃을 거부한다.
        // MSAA는 카메라가 아니라 타깃 텍스처가 정한다 — 끄면 확대된 그림에서 테두리와 숫자가 계단진다.
        this.m_texture = new RenderTexture(t_w, t_h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        this.m_texture.Create();
    }

    void ReleaseTexture()
    {
        if (this.demoCamera != null) this.demoCamera.targetTexture = null;
        if (this.m_texture == null) return;

        this.m_texture.Release();
        Destroy(this.m_texture);
        this.m_texture = null;
    }

    void Stop()
    {
        CancellationTokenSource t_loop = this.m_loop;
        this.m_loop = null;

        if (t_loop == null) return;

        t_loop.Cancel();
        t_loop.Dispose();
    }

    // 빌린 전역을 돌려준다 — 안 되돌리면 다음 전투가 "입력 잠김" 상태로 시작한다.
    void Restore()
    {
        if (!this.m_borrowed) return;   // 이미 돌려줬다 — 두 번째 되돌리기는 남의 상태를 덮는다
        this.m_borrowed = false;

        TurnState.LocalOwnerIndex = this.m_ownerIndex0;
        TurnState.InputAllowed    = this.m_inputAllowed0;
    }

    static int Ms(float _seconds) => Mathf.Max(0, (int)(_seconds * 1000f));
}
