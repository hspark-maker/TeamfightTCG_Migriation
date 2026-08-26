using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 키워드 하나가 발동하는 모습을 실제 전투 연출로 재생하는 작은 무대.
// 전용 카메라가 이 무대를 RenderTexture에 그리고, 해금 인트로의 RawImage가 그 텍스처를 받는다 —
// 파티클은 월드 스페이스라 Screen Space Overlay 캔버스에 직접 얹히지 않기 때문이다.
//
// ⚠ 규칙을 돌리지 않는다. AttackSequence는 _onEffect 하나로 연출과 규칙이 갈려 있어,
//   거기에 null을 넘기면 체력이 안 깎이고 모션·파티클만 돈다(피해 = 콜백 전후 HpTotal 차).
//   RNG·세이브·전투 상태 어디에도 닿지 않는다.
//
// 무대는 y = 20000에 선다. 로비 Main Camera(직교 size 5, 원점)가 물리적으로 못 보는 자리라
// 씬의 cullingMask를 건드릴 필요도, 전용 레이어를 저작할 필요도 없다(UiRectCapture와 같은 자리).
// BattleVfx가 스폰하는 파티클은 레이어를 안 바꾸므로(ApplySorting은 정렬만 손댄다) 이 격리가 유일하게 듣는 방법이다.
//
// BattleFieldView는 두지 않는다 — 그것이 필요한 곳은 시네마 이동(MoveToCenter)뿐이고,
// 이 무대는 _forceSpecial: false로 시네마를 아예 끈다.
public class KeywordDemoStage : SingletonOverlayBase
{
    // 로비 카메라의 시야 밖. UiRectCapture가 같은 이유로 같은 자리를 쓴다.
    static readonly Vector3 StageOrigin = new Vector3(0f, 20000f, 0f);

    static KeywordDemoStage s_instance;

    // 세운 무대 수. 자리를 옆으로 밀어 겹침을 피하는 데만 쓴다(TryGet 주석 참고).
    static int s_stageSerial;

    [Header("무대 배선")]
    [Tooltip("이 무대만 담는 카메라. tag는 반드시 Untagged — MainCamera를 달면 로비 배경 영상이 이쪽으로 튄다.")]
    [SerializeField] Camera demoCamera;

    [Tooltip("앞자리. 방금 강화한 그 카드가 선다 — 대개 공격자이고, 도발에서만 **맞는 쪽**이 된다.")]
    [SerializeField] CardView slotAttacker;

    [Tooltip("맞은편. 대개 맞는 쪽이고, 도발에서만 치러 오는 쪽이 된다.")]
    [SerializeField] CardView slotDefender;

    [Tooltip("곁에 서는 쪽(무쌍 광역 대상·도발이 지켜주는 아군·힐러가 살리는 아군). 쓰지 않는 대본에서는 꺼둔다.")]
    [SerializeField] CardView slotNeighbor;

    [SerializeField] KeywordDemoConfig config;

    [Header("텍스처")]
    [Tooltip("비율은 띠 모양을 따라간다 — 그 자리가 거의 정사각이라 가로세로를 같게 둔다(RawImage 쪽 " +
             "AspectRatioFitter가 이 비율을 그대로 읽으므로 여기만 고치면 된다). 16:9로 두면 띠가 눌려 " +
             "자리의 절반이 비고, 같은 카메라가 가로로 두 배를 비추느라 카드가 반으로 작아진다.\n" +
             "해상도는 띠가 화면 폭의 약 78%(1080 기준 840px)를 먹는 데서 온다 — 그보다 작으면 업스케일로 " +
             "체력·공격력 숫자가 뭉갠다. 모달이 걷힐 때 해제되는 한 장이라 상주 비용은 없다.")]
    [SerializeField] int textureWidth  = 1024;
    [SerializeField] int textureHeight = 1024;

    [Header("박자")]
    [Tooltip("한 판이 끝나고 다시 돌기까지의 뜸. 반복하지 않으면 한눈판 사이에 지나가 버린다.")]
    [SerializeField] float loopGap = 1.1f;

    [Tooltip("무대가 서고 첫 판이 시작하기까지. 모달이 페이드인하는 동안을 비워 둔다.")]
    [SerializeField] float startDelay = 0.35f;

    RenderTexture m_texture;

    // 반복 재생 루프. End()나 파괴로 끊는다 — 남겨두면 꺼진 무대 위에서 트윈이 계속 돈다.
    CancellationTokenSource m_loop;

    // 무대가 잠시 빌려 쓰는 전역. 데모는 로비에서 도는데 이 값들은 전투가 읽으므로 반드시 되돌린다.
    int  m_ownerIndex0;
    bool m_inputAllowed0;

    // 빌린 상태인가. 되돌리기는 한 번뿐이어야 한다 — 이미 돌려준 값을 나중에 또 덮어쓰면
    // 그 사이에 시작한 전투의 상태를 걷어차게 된다.
    bool m_borrowed;

    // 대본 한 판이 도는 중인가. AttackSequence는 취소 토큰을 받지 않아 **중간에 끊을 수 없다** —
    // 도는 동안 무대를 부수면 시퀀스가 이미 사라진 CardView를 만져 MissingReferenceException이 난다.
    bool m_playing;

    // 걷으라는 지시가 판이 도는 중에 들어왔다. 부수는 일은 그 판이 스스로 풀린 뒤 RunLoop이 한다.
    bool m_disposePending;

    /// <summary>무대를 세운다. 위치를 Instantiate 인자로 주는 것이 중요하다 —
    /// <c>CardAnimator.Awake</c>가 그 프레임의 <c>transform.position</c>을 슬롯 자리로 못 박기 때문에,
    /// 세운 뒤에 옮기면 카드가 공격하고 **원점으로 돌아간다**.</summary>
    public static bool TryGet(out KeywordDemoStage _stage)
    {
        if (s_instance == null)
        {
            // 오버레이와 같은 경로로 해석한다(UIPrefab 라벨 → DataLibrary 타입 색인, 주소는 클래스명).
            // 못 찾으면 그쪽이 이미 로그를 남긴다. SingletonOverlay.TryGetOrCreate는 위치 인자를 안 받아 쓰지 않는다.
            var t_prefab = RuntimeOverlayPrefabs.Get<KeywordDemoStage>();
            if (t_prefab != null)
            {
                // 무대마다 자리를 옆으로 옮긴다. 걷혔지만 아직 못 부순 무대(끊을 수 없는 판이 도는 중)와
                // 같은 자리에 세우면, 새 무대의 카메라가 죽어가는 카드까지 함께 비춘다.
                Vector3 t_origin = StageOrigin + new Vector3(1000f * (s_stageSerial++ & 7), 0f, 0f);

                GameObject t_go = Instantiate(t_prefab, t_origin, Quaternion.identity);
                s_instance = t_go.GetComponent<KeywordDemoStage>();

                if (s_instance == null)
                {
                    Debug.LogWarning($"[KeywordDemoStage] {t_prefab.name} 루트에 KeywordDemoStage가 없습니다(프리팹 배선 확인).");
                    Destroy(t_go);
                }
            }
        }

        _stage = s_instance;
        return _stage != null;
    }

    /// <summary>_card가 _keyword를 쓰는 모습을 반복 재생하고, 그 그림이 담긴 텍스처를 돌려준다.
    /// 세울 수 없으면 null — 부른 쪽은 띠를 끄고 글자만 보여주면 된다.</summary>
    public Texture Begin(int _card, CardKeyword _keyword)
    {
        if (_card <= 0 || this.demoCamera == null || this.slotAttacker == null) return null;

        Stop();

        // 빌리기는 한 번만 기록한다 — 이미 빌린 채로 다시 들어오면 **내가 바꿔 놓은 값**을 원본으로 적게 된다.
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

        if (!BindRoles(_card, _keyword)) { Restore(); return null; }

        EnsureTexture();
        this.demoCamera.targetTexture = this.m_texture;
        this.demoCamera.enabled       = true;

        this.m_loop = new CancellationTokenSource();
        RunLoop(_keyword, this.m_loop.Token).Forget();

        return this.m_texture;
    }

    /// <summary>무대를 걷는다. 텍스처는 여기서 해제되므로 부른 쪽은 RawImage에서 먼저 떼야 한다.
    ///
    /// ⚠ 도는 판이 있으면 **곧바로 부수지 않는다**. AttackSequence는 취소를 받지 않아 걷으라는 말을
    /// 알아듣지 못하고, 그 와중에 카드가 사라지면 시퀀스가 죽은 CardView를 만진다.
    /// 화면에서는 즉시 사라지고(카메라를 끈다) 부수는 일만 판이 풀린 뒤로 미룬다.</summary>
    public void End()
    {
        Stop();
        Restore();
        ReleaseTexture();

        // 카메라를 끄는 일이 곧 "화면에서 걷혔다"다 — 텍스처만 떼면 이 카메라가 화면에 직접 그린다.
        if (this.demoCamera != null) this.demoCamera.enabled = false;

        // 자리를 먼저 비운다. 안 그러면 다음 Begin이 부서지기를 기다리는 이 무대를 다시 잡는다.
        if (s_instance == this) s_instance = null;

        if (this.m_playing) { this.m_disposePending = true; return; }

        // 남겨두면 BattleBoardView 정적 레지스트리에 데모 카드가 계속 등록돼 있어,
        // 전투에 들어갔을 때 CardView.FadeAll이 이 셋까지 함께 흐리게 만든다.
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;

        Stop();
        Restore();
        ReleaseTexture();
    }

    // ── 배역 ────────────────────────────────────────────────────────────

    // 카드를 세운다. 앞자리는 언제나 그 카드, 나머지는 저작(KeywordDemoConfig)이 정한다.
    // 진영은 대본마다 갈린다 — 회복은 적에게 쏘지 않고, 도발은 아군을 대신 맞아주는 것이라
    // 곁자리가 적이면 "누구를 지켰나"가 성립하지 않는다.
    bool BindRoles(int _card, CardKeyword _keyword)
    {
        int t_opponent = 0;
        int t_neighbor = 0;
        this.config?.Roles(_keyword, out t_opponent, out t_neighbor);

        if (t_opponent <= 0)
        {
            Debug.LogWarning($"[KeywordDemoStage] {_keyword} 데모의 상대 카드가 저작되지 않았습니다(KeywordDemoConfig 확인).");
            return false;
        }

        // 맞은편 진영. 힐러만 아군이다(회복 대상). 도발의 맞은편은 **치러 오는 적**이라 그대로 적 진영.
        int t_defenderOwner = _keyword == CardKeyword.Healer ? 0 : 1;

        // 곁자리 진영. 무쌍만 적이다(같이 휩쓸리는 대상) — 도발·힐러는 지키고 살리는 아군이다.
        int t_neighborOwner = _keyword == CardKeyword.Peerless ? 1 : 0;

        Render(this.slotAttacker, _card,       0, 0);
        Render(this.slotDefender, t_opponent,  t_defenderOwner, 1);

        // 곁자리는 이 셋만 쓴다. 나머지 대본에서 세워두면 화면만 복잡해지고 시선이 갈린다.
        bool t_useNeighbor = _keyword == CardKeyword.Peerless
                          || _keyword == CardKeyword.Taunt
                          || _keyword == CardKeyword.Healer;

        if (this.slotNeighbor != null)
        {
            this.slotNeighbor.gameObject.SetActive(t_useNeighbor && t_neighbor > 0);
            if (t_useNeighbor && t_neighbor > 0) Render(this.slotNeighbor, t_neighbor, t_neighborOwner, 2);
        }

        return true;
    }

    static void Render(CardView _view, int _data, int _owner, int _slot)
    {
        if (_view == null) return;

        _view.InitializeAnimator();   // 부트스트랩(GameInitializer)이 없는 씬이라 직접 깨운다
        _view.Render(new CardInstance(_data, _owner) { isRevealed = true, slotIndex = _slot });
    }

    // ── 대본 ────────────────────────────────────────────────────────────

    async UniTaskVoid RunLoop(CardKeyword _keyword, CancellationToken _token)
    {
        await UniTask.Delay(Ms(this.startDelay), cancellationToken: _token).SuppressCancellationThrow();

        while (!_token.IsCancellationRequested)
        {
            // 한 판은 끊을 수 없다(AttackSequence가 취소를 안 받는다) — 도는 동안 End가 오면
            // 그쪽이 부수기를 미루고, 판이 풀린 아래에서 이 루프가 마무리한다.
            this.m_playing = true;
            await PlayOnce(_keyword, _token);
            this.m_playing = false;

            if (_token.IsCancellationRequested) break;

            await UniTask.Delay(Ms(this.loopGap), cancellationToken: _token).SuppressCancellationThrow();
        }

        this.m_playing = false;
        if (this.m_disposePending) Destroy(gameObject);
    }

    async UniTask PlayOnce(CardKeyword _keyword, CancellationToken _token)
    {
        CardView t_atk = this.slotAttacker;
        CardView t_def = this.slotDefender;
        if (t_atk == null || t_def == null || t_atk.BoundCard == null) return;

        switch (_keyword)
        {
            // 도발만 **공격 방향이 반대**다(적이 이 카드를 치러 온다) → 배역을 뒤집어 넘긴다.
            case CardKeyword.Taunt:  await PlayTaunt(_taunter: t_atk, _enemy: t_def, _token: _token);  return;
            case CardKeyword.Healer: await PlayHealer(t_atk, _token);        return;
        }

        await Swing(t_atk, t_def, _keyword == CardKeyword.Peerless ? this.slotNeighbor : null, _token);
        if (_token.IsCancellationRequested) return;

        switch (_keyword)
        {
            // 처형은 "한 번 더"가 본체다. 마법진이 돌고 같은 공격이 이어져야 그 뜻이 나온다.
            case CardKeyword.Execution:
                ExecutionVfx.Play(t_atk);
                await Swing(t_atk, t_def, null, _token);
                return;

            // 교활은 때린 뒤 사라지는 것이 본체다. 뒷면으로 물러났다가 같은 카드가 다시 들어온다
            // (덱에서 다른 아군이 나오는 그림은 카드 한 장을 더 세워야 해서 이 무대에선 접었다).
            case CardKeyword.Cunning:
                await CunningVfx.PlayExit(t_atk);
                if (_token.IsCancellationRequested) return;
                await CunningVfx.PlayEnter(t_atk);
                return;

            // 원거리·표식은 **반격이 안 오는 것**이 본체라, 반격 역재생을 붙이지 않는 것 자체가 대본이다.
            // 그 대신 맞은 쪽이 되받으려다 마는 시늉을 한 박 넣어 "안 왔다"를 눈에 띄게 한다.
            case CardKeyword.Ranged:
            case CardKeyword.Mark:
                t_def.PlayRejectShake();
                return;
        }
    }

    // 공격 한 번. _onEffect를 넘기지 않는 것이 이 무대의 규약이다 — 넘기는 순간 체력이 깎인다.
    // _forceSpecial: false로 시네마를 끈다(3단계 진화 카드가 첫 공격에 클로즈업으로 튀는 것을 막는다).
    UniTask Swing(CardView _atk, CardView _def, CardView _splash, CancellationToken _token)
    {
        if (_token.IsCancellationRequested) return UniTask.CompletedTask;

        var (t_preKw, t_atKw) = AttackFlow.Keywords(_atk.BoundCard);

        CardView t_splashView = _splash != null && _splash.gameObject.activeSelf ? _splash : null;

        return AttackSequence.PlaySplash(_atk, _def,
                                         _onEffect: null, _splashView: t_splashView,
                                         _preEffectKw: t_preKw, _atEffectKw: t_atKw,
                                         _forceSpecial: false);
    }

    // 도발: **적이** 곁의 아군을 노리다 이 카드에 끌려와, 결국 이 카드를 친다.
    // 다른 대본과 달리 앞자리가 맞는 쪽이다 — 도발은 내가 하는 일이 아니라 남이 나를 치게 만드는 일이라,
    // 앞자리를 공격자로 두면 정작 배운 키워드가 남의 카드에서 빛난다.
    //
    // 연출 짝은 인게임(CardInputController)과 같다: 막힌 공격자 위에 TauntBlocked, 도발 보유자에게 TauntGuard.
    // 한쪽만 있으면 "왜 막혔는지"나 "누가 막는지" 중 하나가 빠진다.
    async UniTask PlayTaunt(CardView _taunter, CardView _enemy, CancellationToken _token)
    {
        // 지켜줄 아군. 없으면 노리는 박자를 통째로 건너뛴다 — 대신 맞아줄 상대가 없으면 도발이 성립하지 않는다.
        CardView t_wanted = this.slotNeighbor != null && this.slotNeighbor.gameObject.activeSelf
                                ? this.slotNeighbor : null;

        if (t_wanted != null)
        {
            // 1) 적이 아군을 노린다.
            t_wanted.SetHighlight(true);
            await UniTask.Delay(Ms(TAUNT_AIM_HOLD), cancellationToken: _token).SuppressCancellationThrow();
            if (_token.IsCancellationRequested) { t_wanted.SetHighlight(false); return; }

            // 2) 못 친다 — 노리던 쪽이 튕기고, 치려던 적 위에 차단 표식이 선다.
            t_wanted.PlayRejectShake();
            BattleVfx.PlayAttached(BattleVfxId.TauntBlocked, _enemy.transform,
                                   _flip: false, _enemy.VfxSortingLayerId);
        }

        // 3) "이쪽을 쳐라" — 도발 카드가 대답한다.
        // 키워드 글로우를 따로 부르지 않는다: PlayAttentionPulse가 도발 카드면 스스로 띄운다(인게임 거절 경로와 같다).
        // 여기서 또 부르면 같은 프레임에 글로우가 두 장 스폰돼 혼자 두 배로 밝아진다.
        BattleVfx.PlayAttached(BattleVfxId.TauntGuard, _taunter.transform,
                               _flip: false, _taunter.VfxSortingLayerId);
        _taunter.PlayAttentionPulse();

        await UniTask.Delay(Ms(TAUNT_REDIRECT_HOLD), cancellationToken: _token).SuppressCancellationThrow();
        if (t_wanted != null) t_wanted.SetHighlight(false);
        if (_token.IsCancellationRequested) return;

        // 4) 그래서 이 카드가 맞는다. 끌려온 것으로 끝내면 "대신 맞는다"의 뒷말이 빠진다.
        await Swing(_enemy, _taunter, null, _token);
    }

    // 힐러: 때리는 것이 아니라 아군을 살린다. 대상은 곁자리(있으면)와 맞는 쪽 둘 다.
    async UniTask PlayHealer(CardView _healer, CancellationToken _token)
    {
        var t_targets = new List<(CardView view, CardInstance card, int amount)>();

        Add(this.slotDefender);
        if (this.slotNeighbor != null && this.slotNeighbor.gameObject.activeSelf) Add(this.slotNeighbor);

        void Add(CardView _v)
        {
            if (_v != null && _v.BoundCard != null) t_targets.Add((_v, _v.BoundCard, 1));
        }

        if (t_targets.Count == 0) return;

        _healer.PlayKeywordGlow(CardKeyword.Healer).Forget();
        HealVfx.PlayHealBurst(_healer, t_targets);

        // 이 연출은 스스로 끝을 알리지 않는다 — 길이는 HealVfx가 아는 값을 그대로 받아 쓴다.
        await UniTask.Delay(Ms(HealVfx.BurstDuration(t_targets.Count)), cancellationToken: _token)
                     .SuppressCancellationThrow();
    }

    // ── 수명 ────────────────────────────────────────────────────────────

    void EnsureTexture()
    {
        int t_w = Mathf.Max(64, this.textureWidth);
        int t_h = Mathf.Max(64, this.textureHeight);

        if (this.m_texture != null && this.m_texture.width == t_w && this.m_texture.height == t_h) return;

        ReleaseTexture();

        // 깊이 24는 선택이 아니다 — URP의 Render Graph는 depth stencil이 없는 타깃을 거부한다
        // ("Fake or uninitialized surface is not supported for attachment 0").
        // MSAA는 카메라가 아니라 **타깃 텍스처**가 정한다(URP는 targetTexture의 샘플 수를 그대로 쓴다).
        // 끄면 카드 테두리와 숫자가 계단지는데, 이 그림은 화면 폭의 78%로 확대돼 그 계단이 그대로 보인다.
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

    // 빌린 전역을 돌려준다. 데모가 로비에서 도는 동안만 바꿔 쓰는 값이라, 안 되돌리면
    // 다음 전투가 "입력 잠김" 상태로 시작한다.
    void Restore()
    {
        if (!this.m_borrowed) return;   // 이미 돌려줬다 — 두 번째 되돌리기는 남의 상태를 덮는다
        this.m_borrowed = false;

        TurnState.LocalOwnerIndex = this.m_ownerIndex0;
        TurnState.InputAllowed    = this.m_inputAllowed0;
    }

    // 도발 대본의 두 박자. 저작 축으로 뺄 값이 아니다 — 이 무대에만 있는 한 대본의 내부 호흡이라,
    // 인스펙터에 내면 다른 키워드에도 있는 값처럼 읽힌다.
    const float TAUNT_AIM_HOLD      = 0.35f;   // 적이 아군을 노리는 동안
    const float TAUNT_REDIRECT_HOLD = 0.6f;    // 끌려오고 나서 치기까지

    static int Ms(float _seconds) => Mathf.Max(0, (int)(_seconds * 1000f));
}
