using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 방금 열린 능력이 발동하는 모습을 실제 전투 연출로 재생하는 작은 무대.
// 전용 카메라가 이 무대를 RenderTexture에 그리고, 해금 인트로의 RawImage가 그 텍스처를 받는다 —
// 파티클은 월드 스페이스라 Screen Space Overlay 캔버스에 직접 얹히지 않기 때문이다.
//
// 대본은 두 갈래다: 키워드 하나가 발동하는 모습과, 시너지 하나가 일하는 모습.
// 배역 배선(BindKeywordRoles / BindSynergyRoles)부터 갈라 둔다 — 진영 규칙이 서로 반대라
// 한 함수에 합치면 그 함수의 주석이 한쪽에는 반드시 거짓말이 된다.
//
// ⚠ 규칙을 돌리지 않는다. 빈 BattleEvent 목록은 피해 0으로 읽혀 모션과 파티클만 재생한다.
//   시너지 대본은 여기에 한 줄을 더한다: SynergyEffect 파생 클래스와 SynergyTriggers 디스패처를
//   부르지 않고, CardInstance의 **상태 변경 메서드**(Heal · GrantShield · ClearShield)도 부르지 않는다 —
//   그것들은 BattleEventStream.Emit을 타므로, 로비에서 부르면 전투 이벤트 스트림에 로비발 사건이 흐른다.
//   보여줄 숫자가 필요하면 CardView의 표시 전용 API(OverrideHpDisplay · SetShieldVisible)로만 낸다.
//
// 무대는 y = 20000에 선다. 로비 Main Camera(직교 size 5, 원점)가 물리적으로 못 보는 자리라
// 씬의 cullingMask를 건드릴 필요도, 전용 레이어를 저작할 필요도 없다(UiRectCapture와 같은 자리).
// BattleVfx가 스폰하는 파티클은 레이어를 안 바꾸므로(ApplySorting은 정렬만 손댄다) 이 격리가 유일하게 듣는 방법이다.
//
// BattleFieldView는 두지 않는다 — 그것이 필요한 곳은 시네마 이동(MoveToCenter)뿐이고,
// 이 무대는 _forceSpecial: false로 시네마를 아예 끈다.
public class UnlockDemoStage : SingletonOverlayBase
{
    // 로비 카메라의 시야 밖. UiRectCapture가 같은 이유로 같은 자리를 쓴다.
    static readonly Vector3 StageOrigin = new Vector3(0f, 20000f, 0f);

    static UnlockDemoStage s_instance;

    // 세운 무대 수. 자리를 옆으로 밀어 겹침을 피하는 데만 쓴다(TryGet 주석 참고).
    static int s_stageSerial;

    [Header("무대 배선")]
    [Tooltip("이 무대만 담는 카메라. tag는 반드시 Untagged — MainCamera를 달면 로비 배경 영상이 이쪽으로 튄다.")]
    [SerializeField] Camera demoCamera;

    [Tooltip("앞자리. 방금 강화한 그 카드가 선다 — 대개 공격자이고, 도발·비늘·수호자에서만 **맞는 쪽**이 된다.")]
    [SerializeField] CardView slotAttacker;

    [Tooltip("맞은편. 대개 맞는 쪽이고, 도발에서만 치러 오는 쪽이 된다.")]
    [SerializeField] CardView slotDefender;

    [Tooltip("윗줄 곁자리(무쌍 광역 대상·도발이 지켜주는 아군·힐러가 살리는 아군). 키워드 대본 전용이다.")]
    [SerializeField] CardView slotNeighbor;

    [Tooltip("아랫줄 곁자리. 같은 시너지를 가진 동료가 선다 — 시너지 대본 전용이다.\n" +
             "윗줄(slotNeighbor)을 돌려쓸 수 없다: CardAnimator가 첫 활성 프레임의 좌표를 슬롯 자리로 못 박아 " +
             "런타임에 못 옮기고, 이 화면에서 아군과 적을 가르는 단서는 줄(y)뿐이라 윗줄에 세우면 적으로 읽힌다.")]
    [SerializeField] CardView slotAlly;

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

    // 이번 무대의 배역. 매 바퀴 같은 배역을 다시 세우는 데 쓴다(ApplyRoles).
    int  m_cardId;
    int  m_opponentId;
    int  m_neighborId;
    int  m_companionId;
    int  m_defenderOwner;
    int  m_neighborOwner;
    bool m_useNeighbor;
    bool m_useAlly;

    /// <summary>무대를 세운다. 위치를 Instantiate 인자로 주는 것이 중요하다 —
    /// <c>CardAnimator.Awake</c>가 그 프레임의 <c>transform.position</c>을 슬롯 자리로 못 박기 때문에,
    /// 세운 뒤에 옮기면 카드가 공격하고 **원점으로 돌아간다**.</summary>
    public static bool TryGet(out UnlockDemoStage _stage)
    {
        if (s_instance == null)
        {
            // 오버레이와 같은 경로로 해석한다(UIPrefab 라벨 → DataLibrary 타입 색인, 주소는 클래스명).
            // 못 찾으면 그쪽이 이미 로그를 남긴다. SingletonOverlay.TryGetOrCreate는 위치 인자를 안 받아 쓰지 않는다.
            var t_prefab = RuntimeOverlayPrefabs.Get<UnlockDemoStage>();
            if (t_prefab != null)
            {
                // 무대마다 자리를 옆으로 옮긴다. 걷혔지만 아직 못 부순 무대(끊을 수 없는 판이 도는 중)와
                // 같은 자리에 세우면, 새 무대의 카메라가 죽어가는 카드까지 함께 비춘다.
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

    /// <summary>_card가 _keyword를 쓰는 모습을 반복 재생하고, 그 그림이 담긴 텍스처를 돌려준다.
    /// 세울 수 없으면 null — 부른 쪽은 띠를 끄고 글자만 보여주면 된다.</summary>
    public Texture Begin(int _card, CardKeyword _keyword) => BeginCore(_card, DemoCue.OfKeyword(_keyword));

    /// <summary>_card가 속한 _synergy가 일하는 모습을 반복 재생하고, 그 그림이 담긴 텍스처를 돌려준다.
    /// 연출 에셋(<c>SynergyData.vfx</c>)이 비었으면 null — 그 시너지는 규칙만 있고 보여줄 것이 없다.</summary>
    public Texture Begin(int _card, SynergyData _synergy) => BeginCore(_card, DemoCue.OfSynergy(_synergy));

    // 대본 두 갈래를 공개 자리에서 오버로드로 가르는 이유: 호출부는 언제나 둘 중 하나만 안다.
    // 구조체 하나를 공개하면 "둘 다 비었거나 둘 다 찬" 상태를 호출부가 만들 수 있고 그 검증이 무대로 들어온다.
    // 안쪽은 DemoCue 하나로 합쳐 루프가 두 벌로 복제되지 않게 한다.
    Texture BeginCore(int _card, DemoCue _cue)
    {
        if (_card <= 0 || this.demoCamera == null || this.slotAttacker == null) return null;

        if (_cue.IsSynergy && _cue.Synergy.vfx == null)
        {
            Debug.LogWarning($"[UnlockDemoStage] {_cue.Synergy.SynergyId}: 연출 에셋(vfx) 미배선 — 무대 없이 글자만 남깁니다.");
            return null;
        }

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

        bool t_bound = _cue.IsSynergy ? BindSynergyRoles(_card, _cue.Synergy)
                                      : BindKeywordRoles(_card, _cue.Keyword);
        if (!t_bound) { Restore(); return null; }

        EnsureTexture();
        this.demoCamera.targetTexture = this.m_texture;
        this.demoCamera.enabled       = true;

        this.m_loop = new CancellationTokenSource();
        RunLoop(_cue, this.m_loop.Token).Forget();

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

        // 왕관은 카드의 자식이 아니라 월드 오브젝트이고 정적 리스트가 붙든다 —
        // 안 걷으면 y = 20000 자리에 남아 다음 유산 대본에서 두 배로 뜬다.
        LegacyCrownVfx.Clear();

        // 카메라를 끄는 일이 곧 "화면에서 걷혔다"다 — 텍스처만 떼면 이 카메라가 화면에 직접 그린다.
        if (this.demoCamera != null) this.demoCamera.enabled = false;

        // 자리를 먼저 비운다. 안 그러면 다음 Begin이 부서지기를 기다리는 이 무대를 다시 잡는다.
        if (s_instance == this) s_instance = null;

        if (this.m_playing) { this.m_disposePending = true; return; }

        // 남겨두면 BattleBoardView 정적 레지스트리에 데모 카드가 계속 등록돼 있어,
        // 전투에 들어갔을 때 CardView.FadeAll이 이 넷까지 함께 흐리게 만든다.
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

    // ── 배역 ────────────────────────────────────────────────────────────

    // 키워드 대본의 배역. 앞자리는 언제나 그 카드, 나머지는 저작(KeywordDemoConfig)이 정한다.
    // 진영은 대본마다 갈린다 — 회복은 적에게 쏘지 않고, 도발은 아군을 대신 맞아주는 것이라
    // 곁자리가 적이면 "누구를 지켰나"가 성립하지 않는다.
    bool BindKeywordRoles(int _card, CardKeyword _keyword)
    {
        int t_opponent = 0;
        int t_neighbor = 0;
        this.config?.Roles(_keyword, out t_opponent, out t_neighbor);

        if (t_opponent <= 0)
        {
            Debug.LogWarning($"[UnlockDemoStage] {_keyword} 데모의 상대 카드가 저작되지 않았습니다(KeywordDemoConfig 확인).");
            return false;
        }

        this.m_cardId     = _card;
        this.m_opponentId = t_opponent;
        this.m_neighborId = t_neighbor;

        // 맞은편 진영. 힐러만 아군이다(회복 대상). 도발의 맞은편은 **치러 오는 적**이라 그대로 적 진영.
        this.m_defenderOwner = _keyword == CardKeyword.Healer ? 0 : 1;

        // 곁자리 진영. 무쌍만 적이다(같이 휩쓸리는 대상) — 도발·힐러는 지키고 살리는 아군이다.
        this.m_neighborOwner = _keyword == CardKeyword.Peerless ? 1 : 0;

        // 곁자리는 이 셋만 쓴다. 나머지 대본에서 세워두면 화면만 복잡해지고 시선이 갈린다.
        this.m_useNeighbor = t_neighbor > 0
                          && (_keyword == CardKeyword.Peerless
                           || _keyword == CardKeyword.Taunt
                           || _keyword == CardKeyword.Healer);

        this.m_companionId = 0;
        this.m_useAlly     = false;

        ApplyRoles();
        return true;
    }

    // 시너지 대본의 배역. 아군 둘이 아랫줄에 서고 적 하나가 맞은편에 선다 —
    // 시너지는 같은 편이 모여야 성립하므로 곁자리 진영에 분기가 없다.
    bool BindSynergyRoles(int _card, SynergyData _synergy)
    {
        int t_opponent = 0;
        int t_unused   = 0;

        // 시너지에는 배역 저작 축이 없다 — 맞은편은 키워드 표의 기본값을 그대로 쓰고 동료는 코드가 고른다.
        this.config?.Roles(CardKeyword.None, out t_opponent, out t_unused);

        if (t_opponent <= 0)
        {
            Debug.LogWarning("[UnlockDemoStage] 시너지 데모의 상대 카드가 저작되지 않았습니다(KeywordDemoConfig의 기본 배역 확인).");
            return false;
        }

        this.m_cardId        = _card;
        this.m_opponentId    = t_opponent;
        this.m_neighborId    = 0;
        this.m_companionId   = FindSynergyCompanion(_card, _synergy, t_opponent);
        this.m_defenderOwner = 1;
        this.m_neighborOwner = 0;
        this.m_useNeighbor   = false;
        this.m_useAlly       = this.m_companionId > 0;

        ApplyRoles();
        return true;
    }

    // 배역을 무대에 세운다. 매 바퀴 다시 부른다 — 표기 조작·보호막 표시가 앞 바퀴에서 넘어오면
    // 대본이 두 번째 판부터 거짓말을 한다(새 CardInstance로 Render하면 표기 상태가 카드째로 갈린다).
    void ApplyRoles()
    {
        Render(this.slotAttacker, this.m_cardId,     0,                    0);
        Render(this.slotDefender, this.m_opponentId, this.m_defenderOwner, 1);

        if (this.slotNeighbor != null)
        {
            this.slotNeighbor.gameObject.SetActive(this.m_useNeighbor);
            if (this.m_useNeighbor) Render(this.slotNeighbor, this.m_neighborId, this.m_neighborOwner, 2);
        }

        if (this.slotAlly != null)
        {
            this.slotAlly.gameObject.SetActive(this.m_useAlly);
            if (this.m_useAlly) Render(this.slotAlly, this.m_companionId, 0, 2);
        }

        // 보호막은 카드가 아니라 뷰에 얹은 표시라 Render가 걷어가지 않는다(수호자 대본이 켠다).
        if (this.slotAttacker != null) this.slotAttacker.SetShieldVisible(false);
        if (this.m_useAlly && this.slotAlly != null) this.slotAlly.SetShieldVisible(false);
    }

    static void Render(CardView _view, int _data, int _owner, int _slot)
    {
        if (_view == null || _data <= 0) return;

        _view.InitializeAnimator();   // 초기화(GameInitializer)이 없는 씬이라 직접 깨운다
        _view.Render(new CardInstance(_data, _owner) { isRevealed = true, slotIndex = _slot });
    }

    /// <summary>같은 시너지를 가진 다른 카드 중 가장 작은 ID. 없으면 0(곁자리를 비운다).
    /// 열거 순서가 아니라 최소값으로 고르는 이유는 <c>CardCatalog.AllIds</c>가 Dictionary 열거 결과라
    /// 런타임이 순서를 보장하지 않기 때문이다 — 같은 시너지에는 언제나 같은 동료가 서야 한다.</summary>
    static int FindSynergyCompanion(int _card, SynergyData _synergy, int _opponent)
    {
        // 미준비 상태에서 RequireSynergies를 부르면 throw한다 — 안내창이 예외로 죽는 것보다 곁자리를 비우는 편이 낫다.
        if (!CardCatalog.IsReady || _synergy == null) return 0;

        int t_best = 0;

        foreach (int t_id in CardCatalog.AllIds)
        {
            if (t_id == _card || t_id == _opponent) continue;
            if (t_best > 0 && t_id > t_best) continue;

            IReadOnlyList<SynergyData> t_list = CardCatalog.RequireSynergies(t_id);
            if (t_list == null) continue;

            foreach (SynergyData t_s in t_list)
                if (t_s == _synergy) { t_best = t_id; break; }
        }

        return t_best;
    }

    // ── 대본 ────────────────────────────────────────────────────────────

    async UniTaskVoid RunLoop(DemoCue _cue, CancellationToken _token)
    {
        await UniTask.Delay(Ms(this.startDelay), cancellationToken: _token).SuppressCancellationThrow();

        while (!_token.IsCancellationRequested)
        {
            // 한 판은 끊을 수 없다(AttackSequence가 취소를 안 받는다) — 도는 동안 End가 오면
            // 그쪽이 부수기를 미루고, 판이 풀린 아래에서 이 루프가 마무리한다.
            this.m_playing = true;
            await PlayOnce(_cue, _token);
            this.m_playing = false;

            if (_token.IsCancellationRequested) break;

            await UniTask.Delay(Ms(this.loopGap), cancellationToken: _token).SuppressCancellationThrow();
            if (_token.IsCancellationRequested) break;

            // 다음 판은 저작 상태에서 시작한다. 표기 조작과 보호막 표시가 남으면 두 번째 판이 거짓말을 한다.
            ApplyRoles();
        }

        this.m_playing = false;
        if (this.m_disposePending) Destroy(gameObject);
    }

    UniTask PlayOnce(DemoCue _cue, CancellationToken _token)
        => _cue.IsSynergy ? PlaySynergy(_cue.Synergy, _token) : PlayKeyword(_cue.Keyword, _token);

    async UniTask PlayKeyword(CardKeyword _keyword, CancellationToken _token)
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

    // 시너지 대본의 갈림길. 키는 SynergyId 하나뿐이다 — 효과 클래스나 연출 에셋 타입으로 가르면
    // 덩치와 비늘이 붙어 버린다(둘 다 StatSynergyEffect + 엠블럼만 있는 설정이지만 보여줄 순간이 반대다).
    async UniTask PlaySynergy(SynergyData _synergy, CancellationToken _token)
    {
        CardView t_atk = this.slotAttacker;
        CardView t_def = this.slotDefender;
        if (t_atk == null || t_def == null || t_atk.BoundCard == null || _synergy == null) return;

        // 동료를 못 찾았으면 곁자리 없이 1인 대본으로 줄인다 — 각 박자가 스스로 null을 견딘다.
        CardView t_ally = this.slotAlly != null && this.slotAlly.gameObject.activeSelf ? this.slotAlly : null;

        switch (_synergy.SynergyId)
        {
            case "Bulk":      await PlayBulk(t_atk, t_ally, _synergy, _token);            return;
            case "Scale":     await PlayScale(t_atk, t_def, t_ally, _synergy, _token);    return;
            case "Guardian":  await PlayGuardian(t_atk, t_def, t_ally, _synergy, _token); return;
            case "Caretaker": await PlayCaretaker(t_atk, t_ally, _synergy, _token);       return;
            case "Flow":      await PlayFlow(t_atk, t_def, t_ally, _synergy, _token);     return;
            case "Brand":     await PlayBrand(t_atk, t_def, t_ally, _synergy, _token);    return;
            case "Predator":  await PlayPredator(t_atk, t_def, _synergy, _token);         return;
            case "Trace":     await PlayTrace(t_atk, t_def, t_ally, _synergy, _token);    return;
            case "Legacy":    await PlayLegacy(t_atk, t_def, t_ally, _synergy, _token);   return;
            default:          await PlayAnySynergy(t_atk, t_def, _synergy, _token);       return;
        }
    }

    // 덩치: 같은 편이 모이니 몸이 커진다. 볼거리가 배치 그 자체라 공격을 붙이지 않는다.
    async UniTask PlayBulk(CardView _atk, CardView _ally, SynergyData _syn, CancellationToken _token)
    {
        if (_ally != null)
        {
            SynergyEmblemVfx.Play(_ally, _syn, SynergyEmblemTiming.Placed);
            await Hold(SYNERGY_STEP, _token);
            if (_token.IsCancellationRequested) return;
        }

        SynergyEmblemVfx.Play(_atk, _syn, SynergyEmblemTiming.Placed);
        await Hold(SynergyEmblemVfx.DurationOf(_syn, SynergyEmblemTiming.Placed), _token);
        if (_token.IsCancellationRequested) return;

        SynergyEmblemVfx.Play(_atk, _syn, SynergyEmblemTiming.Triggered);
        _atk.OverrideHpDisplay(_atk.BoundCard.hp, _atk.BoundCard.bonusHp + BULK_SHOW_BONUS);

        await Hold(SYNERGY_HOLD, _token);
    }

    // 비늘: 맞아도 덜 아프다. **빈 이벤트 규약 자체가 대본이다** —
    // 적이 치는데 피해 숫자가 안 뜨는 그림이 곧 "깎였다"이고, 접촉 순간의 엠블럼이 그 원인을 밝힌다.
    async UniTask PlayScale(CardView _atk, CardView _def, CardView _ally, SynergyData _syn, CancellationToken _token)
    {
        if (_ally != null) SynergyEmblemVfx.Play(_ally, _syn, SynergyEmblemTiming.Placed);
        SynergyEmblemVfx.Play(_atk, _syn, SynergyEmblemTiming.Placed);

        await Hold(SynergyEmblemVfx.DurationOf(_syn, SynergyEmblemTiming.Placed), _token);
        if (_token.IsCancellationRequested) return;

        await Swing(_def, _atk, null, _token, _afterHit: () =>
        {
            SynergyEmblemVfx.Play(_atk, _syn, SynergyEmblemTiming.Triggered);
            return UniTask.CompletedTask;
        });
        if (_token.IsCancellationRequested) return;

        await Hold(SYNERGY_HOLD, _token);
    }

    // 수호자: 배치되며 막이 서고, 그 막이 한 대를 삼킨다.
    // CardInstance.GrantShield를 부르지 않는다 — 모델을 안 건드려야 PlayShieldBreakEffect가
    // 끝나면서 표시를 정확히 꺼준다(그 분기가 boundCard.hasShield를 읽는다).
    async UniTask PlayGuardian(CardView _atk, CardView _def, CardView _ally, SynergyData _syn, CancellationToken _token)
    {
        _atk.SetShieldVisible(true);
        SynergyEmblemVfx.Play(_atk, _syn, SynergyEmblemTiming.Placed);

        if (_ally != null)
        {
            _ally.SetShieldVisible(true);
            SynergyEmblemVfx.Play(_ally, _syn, SynergyEmblemTiming.Placed);
        }

        await Hold(SynergyEmblemVfx.DurationOf(_syn, SynergyEmblemTiming.Placed), _token);
        if (_token.IsCancellationRequested) return;

        SynergyEmblemVfx.Play(_atk, _syn, SynergyEmblemTiming.Triggered);
        if (_ally != null) SynergyEmblemVfx.Play(_ally, _syn, SynergyEmblemTiming.Triggered);

        await Hold(SynergyEmblemVfx.DurationOf(_syn, SynergyEmblemTiming.Triggered), _token);
        if (_token.IsCancellationRequested) return;

        await Swing(_def, _atk, null, _token, _afterHit: () =>
        {
            _atk.PlayShieldBreakEffect();
            return UniTask.CompletedTask;
        });
        if (_token.IsCancellationRequested) return;

        await Hold(SYNERGY_HOLD, _token);
    }

    // 돌보미: 동료가 나오면 서로를 돌본다. 게임 경로와 같은 그림 — 엠블럼이 돌보미 전원 위에 뜨고
    // 회복 표기가 **같은 순간** 각자 자리에서 터진다(힐러 투사체는 쓰지 않는다).
    async UniTask PlayCaretaker(CardView _atk, CardView _ally, SynergyData _syn, CancellationToken _token)
    {
        SynergyEmblemVfx.Play(_atk, _syn, SynergyEmblemTiming.Triggered);
        if (_ally != null) SynergyEmblemVfx.Play(_ally, _syn, SynergyEmblemTiming.Triggered);

        // 데모엔 유예된 표기가 없으므로 _consumeDeferred는 기본값(false) — 그래야 "+N"이 실제로 뜬다.
        if (_atk.BoundCard != null) _atk.PlayHealEffect(CARETAKER_SHOW_HEAL);
        if (_ally != null && _ally.BoundCard != null) _ally.PlayHealEffect(CARETAKER_SHOW_HEAL);

        await Hold(SynergyEmblemVfx.DurationOf(_syn, SynergyEmblemTiming.Triggered), _token);
    }

    // 흐름: 동료가 늘수록 바람이 커진다. 스택이 1에서 2로 오르는 것을 크기로 읽게 한다.
    async UniTask PlayFlow(CardView _atk, CardView _def, CardView _ally, SynergyData _syn, CancellationToken _token)
    {
        if (!(_syn.vfx is FlowSynergyVfxConfig t_cfg))
        {
            WarnVfxType(_syn, "FlowSynergyVfxConfig");
            await PlayAnySynergy(_atk, _def, _syn, _token);
            return;
        }

        int t_stack = 1;

        if (_ally != null)
        {
            SynergyVfx.PlayFlowWind(_ally, t_cfg, t_stack);
            await Hold(FLOW_STEP, _token);
            if (_token.IsCancellationRequested) return;
            t_stack = 2;
        }

        SynergyVfx.PlayFlowWind(_atk, t_cfg, t_stack);
        await Hold(FLOW_STEP, _token);
        if (_token.IsCancellationRequested) return;

        // 공격 개시와 함께 한 번 더 — 인게임의 공격 직전 발동과 같은 그림이다.
        SynergyVfx.PlayFlowWind(_atk, t_cfg, t_stack);
        await Swing(_atk, _def, null, _token);
    }

    // 낙인: 낙인 전원이 먼저 쏘고, 그 다음에 친다.
    async UniTask PlayBrand(CardView _atk, CardView _def, CardView _ally, SynergyData _syn, CancellationToken _token)
    {
        if (!(_syn.vfx is BrandSynergyVfxConfig t_cfg))
        {
            WarnVfxType(_syn, "BrandSynergyVfxConfig");
            await PlayAnySynergy(_atk, _def, _syn, _token);
            return;
        }

        SynergyEmblemVfx.Play(_atk, _syn, SynergyEmblemTiming.Triggered);
        if (_ally != null) SynergyEmblemVfx.Play(_ally, _syn, SynergyEmblemTiming.Triggered);

        await Hold(SynergyEmblemVfx.DurationOf(_syn, SynergyEmblemTiming.Triggered), _token);
        if (_token.IsCancellationRequested) return;

        var t_sources = new List<CardView> { _atk };
        if (_ally != null) t_sources.Add(_ally);

        var t_damages = new int[t_sources.Count];
        for (int t_i = 0; t_i < t_damages.Length; t_i++) t_damages[t_i] = BRAND_SHOW_DAMAGE;

        // 표기 전용 볼리다(착탄이 PlayHitAnim + OverrideHpDisplay만 부른다) — 실제 체력은 그대로다.
        await BrandVolleyVfx.PlayVolley(t_sources, _def, t_damages,
                                        _def.BoundCard.hp, _def.BoundCard.bonusHp, t_cfg);
        if (_token.IsCancellationRequested) return;

        await Swing(_atk, _def, null, _token);
    }

    // 포식자: 때린 만큼 되마신다. 만피에서 시작하면 회복이 안 읽히므로 표기를 먼저 낮춰 둔다.
    // 곁자리는 세우지 않는다 — 흡혈은 개인 효과라 동료를 세워도 그 자리가 하는 일이 없다.
    async UniTask PlayPredator(CardView _atk, CardView _def, SynergyData _syn, CancellationToken _token)
    {
        if (!(_syn.vfx is PredatorSynergyVfxConfig t_cfg))
        {
            WarnVfxType(_syn, "PredatorSynergyVfxConfig");
            await PlayAnySynergy(_atk, _def, _syn, _token);
            return;
        }

        int t_hp    = _atk.BoundCard.hp;
        int t_bonus = _atk.BoundCard.bonusHp;
        int t_drain = Mathf.Clamp(PREDATOR_SHOW_DRAIN, 1, Mathf.Max(1, t_hp - 1));

        _atk.OverrideHpDisplay(t_hp - t_drain, t_bonus);
        await Hold(SYNERGY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        await Swing(_atk, _def, null, _token);
        if (_token.IsCancellationRequested) return;

        // 공격 연출이 표기를 모델 값으로 되돌렸을 수 있다 — 흡수 직전에 낮춘 값을 다시 세운다.
        _atk.OverrideHpDisplay(t_hp - t_drain, t_bonus);

        await PredatorVfx.PlayDrain(_def, _atk, t_cfg);
        if (_token.IsCancellationRequested) return;

        _atk.OverrideHpDisplay(t_hp, t_bonus);
        await Hold(SYNERGY_HOLD, _token);
    }

    // 표식: 때린 자리에 표식이 남고, 동료가 그 적을 문다.
    // 곁자리를 가장 잘 쓰는 대본이다 — 동료의 두 번째 공격이 없으면 표식이 "그래서 뭐가 좋은가"를 못 말한다.
    async UniTask PlayTrace(CardView _atk, CardView _def, CardView _ally, SynergyData _syn, CancellationToken _token)
    {
        if (!(_syn.vfx is TraceSynergyVfxConfig t_cfg) || t_cfg.mark.prefab == null)
        {
            WarnVfxType(_syn, "TraceSynergyVfxConfig");
            await PlayAnySynergy(_atk, _def, _syn, _token);
            return;
        }

        await Swing(_atk, _def, null, _token, _afterHit: () =>
        {
            BattleVfx.Play(t_cfg.mark, _def.SlotPosition, _def.VfxSortingLayerId);
            return UniTask.CompletedTask;
        });
        if (_token.IsCancellationRequested) return;

        await Hold(TRACE_MARK_HOLD, _token);
        if (_token.IsCancellationRequested) return;

        if (_ally != null)
        {
            await Swing(_ally, _def, null, _token);
            if (_token.IsCancellationRequested) return;
        }

        // 표기 조작은 반드시 공격 뒤다 — PlayHitAnim이 표기를 모델 값으로 되돌리므로 앞에 두면 조용히 지워진다.
        _atk.OverrideHpDisplay(_atk.BoundCard.hp, _atk.BoundCard.bonusHp + TRACE_SHOW_BONUS);
        await Hold(SYNERGY_HOLD, _token);
    }

    // 유산: 턴마다 왕관이 하나씩 쌓인다. Show의 개수 인자 오버로드는 게임 상태를 안 바꾸는 미리보기 진입점이다.
    // 파괴 국면은 대본에 없다 — 왕관 비행 연출이 제거돼 보여줄 그림이 없다(_ally는 그래서 지금 안 쓴다).
    async UniTask PlayLegacy(CardView _atk, CardView _def, CardView _ally, SynergyData _syn, CancellationToken _token)
    {
        if (!(_syn.vfx is LegacySynergyVfxConfig))
        {
            WarnVfxType(_syn, "LegacySynergyVfxConfig");
            await PlayAnySynergy(_atk, _def, _syn, _token);
            return;
        }

        LegacyCrownVfx.Show(_atk.BoundCard, _syn, 1);
        await Hold(LEGACY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        LegacyCrownVfx.Show(_atk.BoundCard, _syn, 2);
        await Hold(LEGACY_STEP, _token);
        if (_token.IsCancellationRequested) return;

        // 파괴 국면(왕관 비행)은 연출 자체가 제거됐다 — 남은 것은 회복 숫자뿐이라 데모로 보여줄 그림이 없다.
        // 그래서 대본은 "쌓이는 것"까지만 보여주고 끝낸다.
        await Hold(SYNERGY_HOLD, _token);
    }

    // 대본이 아직 없는 시너지(새로 늘어난 것)와 연출 에셋 타입이 어긋난 경우의 폴백.
    async UniTask PlayAnySynergy(CardView _atk, CardView _def, SynergyData _syn, CancellationToken _token)
    {
        SynergyEmblemTiming t_timing = _syn.PlaysEmblemAt(SynergyEmblemTiming.Triggered)
                                     ? SynergyEmblemTiming.Triggered
                                     : SynergyEmblemTiming.Placed;

        if (_syn.PlaysEmblemAt(t_timing))
        {
            SynergyEmblemVfx.Play(_atk, _syn, t_timing);
            await Hold(SynergyEmblemVfx.DurationOf(_syn, t_timing), _token);
            if (_token.IsCancellationRequested) return;
        }

        await Swing(_atk, _def, null, _token);
    }

    // 연출 에셋 타입이 어긋나면 그 대본은 통째로 무음이 된다 — "안 뜬다"와 "깨졌다"를 로그로 가른다.
    static void WarnVfxType(SynergyData _syn, string _expected)
        => Debug.LogWarning($"[UnlockDemoStage] {_syn.SynergyId}: vfx가 {_expected}가 아니라 기본 대본으로 떨어집니다.");

    // 공격 한 번. 빈 이벤트 목록을 넘기는 것이 이 무대의 규약이다 — 체력은 바꾸지 않는다.
    // _forceSpecial: false로 시네마를 끈다(3단계 진화 카드가 첫 공격에 클로즈업으로 튀는 것을 막는다).
    UniTask Swing(CardView _atk, CardView _def, CardView _splash, CancellationToken _token,
                  Func<UniTask> _afterHit = null)
    {
        if (_token.IsCancellationRequested) return UniTask.CompletedTask;

        CardView t_splashView = _splash != null && _splash.gameObject.activeSelf ? _splash : null;

        return AttackSequence.PlaySplash(_atk, _def,
                                         _events: Array.Empty<TeamfightTCG.BattleCore.BattleEvent>(), _splashView: t_splashView,
                                         _afterHit: _afterHit,
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

    async UniTask Hold(float _seconds, CancellationToken _token)
    {
        await UniTask.Delay(Ms(_seconds), cancellationToken: _token).SuppressCancellationThrow();
    }

    /// <summary>어느 대본을 돌릴지. 만드는 문을 둘로 나눠 둔 것은 "둘 다 찼거나 둘 다 빈" 상태를
    /// 애초에 만들 수 없게 하기 위해서다.</summary>
    readonly struct DemoCue
    {
        public readonly CardKeyword Keyword;
        public readonly SynergyData Synergy;

        public bool IsSynergy => this.Synergy != null;

        DemoCue(CardKeyword _keyword, SynergyData _synergy)
        {
            this.Keyword = _keyword;
            this.Synergy = _synergy;
        }

        public static DemoCue OfKeyword(CardKeyword _keyword) => new DemoCue(_keyword, null);
        public static DemoCue OfSynergy(SynergyData _synergy) => new DemoCue(CardKeyword.None, _synergy);
    }

    // 도발 대본의 두 박자. 저작 축으로 뺄 값이 아니다 — 이 무대에만 있는 한 대본의 내부 호흡이라,
    // 인스펙터에 내면 다른 키워드에도 있는 값처럼 읽힌다.
    const float TAUNT_AIM_HOLD      = 0.35f;   // 적이 아군을 노리는 동안
    const float TAUNT_REDIRECT_HOLD = 0.6f;    // 끌려오고 나서 치기까지

    // 시너지 대본의 호흡. 위와 같은 이유로 저작 축이 아니다.
    const float SYNERGY_STEP    = 0.3f;    // 배역이 하나씩 서는 간격
    const float SYNERGY_HOLD    = 0.7f;    // 결과를 읽는 시간
    const float FLOW_STEP       = 0.35f;   // 바람이 한 단 커지는 간격
    const float LEGACY_STEP     = 0.55f;   // 왕관이 한 개 늘어나는 간격
    const float TRACE_MARK_HOLD = 0.35f;   // 표식이 붙고 동료가 달려들기까지

    // 대본이 보여주는 숫자. 규칙에서 오는 값이 아니라 **읽히기 위한 값**이다 —
    // 시트의 실제 수치를 끌어오면 티어에 따라 0이 되는 날 대본이 통째로 무음이 된다.
    const int BULK_SHOW_BONUS     = 3;
    const int CARETAKER_SHOW_HEAL = 2;
    const int BRAND_SHOW_DAMAGE   = 1;
    const int PREDATOR_SHOW_DRAIN = 3;
    const int TRACE_SHOW_BONUS    = 2;

    static int Ms(float _seconds) => Mathf.Max(0, (int)(_seconds * 1000f));
}
