using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>연출 확인 전용 테스트 씬 컨트롤러.
///
/// <para><b>이 스크립트는 연출을 "재생"만 한다.</b> 값 튜닝(BattleTimingConfig 저장/불러오기)과
/// 전투 규칙 재현은 일부러 들어 있지 않다 — 타이밍 값은 SO 인스펙터에서 직접 고치고,
/// 여기서는 고친 결과가 화면에서 어떻게 보이는지만 본다.
/// 예전엔 테스터가 SO를 덮어쓸 수 있어서, 확인하러 들어왔다가 전역 연출 타이밍이 바뀐 채로
/// 커밋되는 사고가 가능했다.</para>
///
/// <para>조작은 <b>인스펙터 버튼</b>이다(AttackAnimTesterEditor). 단축키는 두지 않는다 —
/// 이 씬은 연출 담당자와 같이 보는 자리라 키를 외워야 하는 도구는 쓰이지 않는다.
/// 카드 탭·드래그 공격은 그대로 살아 있다(인게임과 같은 입력 경로).</para>
///
/// <para>재생은 전부 <b>게임이 실제로 쓰는 진입점</b>을 그대로 부른다. 테스터 전용 사본을 만들면
/// 그쪽만 고쳐지고 본 경로가 뒤처진다 — 연출을 고칠 때 이 파일도 같이 봐야 하는 이유다.</para></summary>
/// <summary>테스터가 재생할 수 있는 시너지 연출 종류. <b>연출이 늘면 여기에 값을 하나 더하고
/// <see cref="AttackAnimTester.PlaySelectedSynergy"/>의 분기만 늘린다</b> — 인스펙터 드롭다운과
/// 버튼은 이 enum을 그대로 읽으므로 에디터 코드는 손대지 않는다.</summary>
public enum SynergyPreviewKind
{
    Emblem,          // 고른 시너지의 엠블럼(타이밍은 아래 emblemTiming)
    SwarmVolley,     // 무리: 아군 전원 → 적 슬롯0 선피해 볼리
    FlowWind,        // 흐름: 아군 필드를 지나는 바람(중첩만큼 커진다)
    CaretakerHeal,   // 돌보미: 아군 전원 회복(힐러와 같은 연출)
}

/// <summary>키워드에서 따로 확인할 수 있는 연출 종류.</summary>
public enum KeywordPreviewKind
{
    Glow,
    Attack,
    Vfx,
}

/// <summary>이어 붙여 재생할 수 있는 한 마디. 인게임 <c>AttackFlow</c>가 실제로 도는 순서
/// (선피해 → 공격 시퀀스 → 공격 후 효과 → 교대 → 결과 연출)를 그대로 재현하려고 나눠 둔 단위다.</summary>
public enum AttackStep
{
    PlacedEmblem,     // 배치 상징(고른 시너지의 Placed 엠블럼)
    FlowWind,         // 등장 바람(흐름)
    SwarmVolley,      // 공격 전 선피해(무리)
    Attack,           // 공격 시퀀스 — 무장·접근·접촉·피격·처형까지
    AfterAttackHeal,  // 공격 후 회복(청소부·돌보미가 쓰는 힐 연출)
    TriggeredEmblem,  // 발동 상징(고른 시너지의 Triggered 엠블럼)
    CunningSwap,      // 교활 퇴장 + 재등장
    Wait,             // 한 박자 쉬기
}

public class AttackAnimTester : MonoBehaviour
{
    [Header("필드")]
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;

    [Header("세울 카드 (슬롯 순서대로, 비우면 빈 슬롯)")]
    [SerializeField] CardData[] playerCards = new CardData[3];
    [SerializeField] CardData[] enemyCards  = new CardData[3];

    [Header("공격 연출 옵션")]
    [Tooltip("체크=특별 연출(시네마). 해제=일반 박치기")]
    [SerializeField] bool useSpecialCinema = false;
    [Tooltip("체크=처치에 성공하면 처형 연출(무기 양 끝 스파크 + 마법진) 재생")]
    [SerializeField] bool useExecution = true;
    [Tooltip("체크=총 체력이 낮은 쪽이 접촉 순간 즉사. 사망/생존 연출 분기를 반복해서 보려는 스위치다\n" +
             "(체력을 조금씩 깎으면 몇 대를 때려야 죽어 확인이 번거롭다)")]
    [SerializeField] bool killWeakerSide = true;
    [Tooltip("체크=반격 피격 VFX도 재생")]
    [SerializeField] bool hitVfxOnCounter = true;

    [Header("무장 VFX (공격자 기준, 무장~접촉까지 부착 유지)")]
    // 스폰/해제 시점은 CardView가 쥔다(무장=FocusWeapon(true), 해제=AttackSequence 접촉 시점).
    // 여기선 "어떤 프리팹을 쓸지"만 넘긴다 → localOffset/lifetime 같은 VfxSlot 자체 배치값은 안 쓰인다.
    [SerializeField] VfxSlot attackVfx = new VfxSlot { localOffset = new Vector3(0f, 0f, -0.5f), lifetime = 2f };
    [Header("피격 VFX (방어자 기준, 접촉 시점)")]
    [SerializeField] VfxSlot hitVfx    = new VfxSlot { localOffset = new Vector3(0f, 0f, -0.5f), lifetime = 1.5f };

    [Header("타이밍 SO (주입 전용 — 값은 SO 인스펙터에서 고친다)")]
    // 부트스트랩이 없는 씬이라 아무도 GameTiming에 SO를 안 넣어준다 → 그대로 두면 GameTiming.Battle이
    // 코드 기본값짜리 임시 인스턴스를 만들어 쓰고, 그걸 읽는 연출은 SO를 고쳐도 반응이 없다.
    [SerializeField] BattleTimingConfig timingConfig;

    [Header("시너지 연출")]
    [Tooltip("어느 시너지를 볼지(프로젝트의 SynergyData 목록 인덱스)")]
    [SerializeField] int synergyIndex = 0;
    [Tooltip("무엇을 재생할지. 종류가 늘면 이 enum에 값을 더하고 PlaySelectedSynergy의 분기만 늘린다")]
    [SerializeField] SynergyPreviewKind synergyPreview = SynergyPreviewKind.Emblem;

    [Header("키워드 연출")]
    [Tooltip("연출을 확인할 키워드(실제 연출이 있는 키워드만 표시)")]
    [SerializeField] int keywordIndex = 0;
    [Tooltip("고른 키워드에서 확인할 연출")]
    [SerializeField] KeywordPreviewKind keywordPreview = KeywordPreviewKind.Glow;

    [Header("연출별 값")]
    [Tooltip("엠블럼을 띄울 아군 슬롯")]
    [Range(0, 2)] [SerializeField] int emblemSlot = 0;
    [SerializeField] SynergyEmblemTiming emblemTiming = SynergyEmblemTiming.Placed;
    [Tooltip("체크=끝나는 대로 다시 재생 + SO를 고치는 순간 즉시 반영. 겹침·간격처럼 눈으로 맞추는 값에 쓴다")]
    [SerializeField] bool emblemAutoReplay = false;
    [Range(0f, 2f)] [SerializeField] float emblemReplayGap = 0.35f;
    [Tooltip("흐름 바람을 어느 중첩으로 볼지. 중첩이 클수록 크게 재생된다")]
    [Min(1)] [SerializeField] int flowStack = 3;
    [Tooltip("무리 볼리 한 발당 표기 피해(연출용 숫자, 실제 피해 없음)")]
    [SerializeField] int swarmDamagePerShot = 1;
    [Tooltip("돌보미 회복 표기량(연출용 숫자, 실제 회복 없음)")]
    [SerializeField] int caretakerHeal = 1;

    [Header("연결 재생 — 한 마디씩 순서대로")]
    [Tooltip("위에서 아래로 이어 재생한다. 순서를 바꾸거나 마디를 빼면 그대로 반영된다")]
    [SerializeField] AttackStep[] sequence =
    {
        AttackStep.PlacedEmblem,
        AttackStep.SwarmVolley,
        AttackStep.Attack,
        AttackStep.AfterAttackHeal,
        AttackStep.TriggeredEmblem,
        AttackStep.CunningSwap,
    };
    [Tooltip("마디와 마디 사이 간격(초). 인게임은 규칙 처리가 끼어들어 이만큼 벌어진다")]
    [Range(0f, 1.5f)] [SerializeField] float stepGap = 0.25f;
    [Tooltip("대기가 필요 없는 마디(바람·회복처럼 기다릴 API가 없는 것)를 이 시간만큼 본다")]
    [Range(0.1f, 3f)] [SerializeField] float untimedStepHold = 0.9f;

    [Header("자동 배선 (비워 두면 프로젝트에서 찾아 채운다)")]
    [SerializeField] SynergyData[] emblemSynergies;
    [SerializeField] SwarmSynergyVfxConfig swarmVfx;
    [SerializeField] FlowSynergyVfxConfig  flowVfx;

    bool busy;
    bool armedPreview;

    /// <summary>지금 연출이 도는 중인가. 인스펙터 버튼을 잠그는 기준.</summary>
    public bool Busy => this.busy;

    /// <summary>인스펙터 표시용 현재 상태 한 줄.</summary>
    public string StatusLine
    {
        get
        {
            SynergyData t_syn = CurrentEmblemSynergy;
            return $"{(t_syn != null ? t_syn.name : "시너지 없음")} · {this.synergyPreview}"
                 + $" · vfx {(t_syn?.vfx != null ? t_syn.vfx.name : "미배선")}\n"
                 + $"무장VFX {this.attackVfx.Index + 1}/{this.attackVfx.Count} {this.attackVfx.CurrentName}"
                 + $"   피격VFX {this.hitVfx.Index + 1}/{this.hitVfx.Count} {this.hitVfx.CurrentName}";
        }
    }

    void Start()
    {
        TurnState.LocalOwnerIndex = 0;   // 플레이어(owner 0)가 로컬 → 탭 무장 대상.
        TurnState.InputAllowed    = true;

        ResolveConfig();
        ResolveSynergyAssets();

        // 부트스트랩(GameInitializer) off라 애니메이터 캐시 초기화가 안 됨 → 첫 연출 누락 방지로 직접 호출.
        this.playerFieldView.InitializeAnimators();
        this.enemyFieldView.InitializeAnimators();

        RefreshField();
        PushArmedVfx();

        CardView.OnAttack += HandleAttack;
    }

    void OnDestroy() => CardView.OnAttack -= HandleAttack;

    void Update() => PollEmblemAutoReplay();

    // ── 카드 배치 ────────────────────────────────────────────────────────

    /// <summary>인스펙터의 카드 배열대로 양쪽 필드를 다시 그린다. 카드를 바꾼 뒤 누르면 된다.</summary>
    public void RefreshField()
    {
        RenderField(this.playerFieldView, this.playerCards, 0);
        RenderField(this.enemyFieldView,  this.enemyCards,  1);
    }

    void RenderField(BattleFieldView _fv, CardData[] _cards, int _owner)
    {
        if (_fv == null) return;
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardView t_cv = _fv.GetSlotView(i);
            if (t_cv == null) continue;

            CardData t_data = (_cards != null && i < _cards.Length) ? _cards[i] : null;
            if (t_data == null) { t_cv.Render(null); continue; }

            var t_inst = new CardInstance(t_data, _owner) { isRevealed = true, slotIndex = i };
            t_cv.Render(t_inst);
        }
    }

    // ── 공격 연출 ────────────────────────────────────────────────────────

    public void PlayPlayerAttack() => TryAttack(this.playerFieldView, 0, this.enemyFieldView, 0);
    public void PlayEnemyAttack()  => TryAttack(this.enemyFieldView,  0, this.playerFieldView, 0);

    void TryAttack(BattleFieldView _atkFv, int _atkSlot, BattleFieldView _defFv, int _defSlot)
    {
        if (this.busy) return;
        CardView t_atk = _atkFv?.GetSlotView(_atkSlot);
        CardView t_def = _defFv?.GetSlotView(_defSlot);
        if (t_atk == null || t_def == null || t_atk.BoundCard == null || t_def.BoundCard == null) return;
        RunAttack(t_atk, t_def).Forget();
    }

    void HandleAttack(CardView _attacker, CardView _target)
    {
        if (this.busy) return;
        RunAttack(_attacker, _target).Forget();
    }

    /// <summary>단발 공격 재생. busy 잠금은 여기서만 건다 — 시퀀스는 <see cref="AttackCore"/>를 직접 부른다
    /// (시퀀스가 이미 잠가 둔 상태에서 또 잠그면 중간에 풀려 다음 단계가 겹친다).</summary>
    async UniTask RunAttack(CardView _attacker, CardView _defender)
    {
        this.busy = true;
        TurnState.InputAllowed = false;

        await AttackCore(_attacker, _defender);

        TurnState.InputAllowed = true;
        this.busy = false;
    }

    async UniTask AttackCore(CardView _attacker, CardView _defender)
    {
        AttackEffect t_effect = _attacker.BoundCard?.data?.attackEffect;

        // 적(비로컬) 공격이면 오프셋/회전 반전 — AttackSequence의 t_flip 규칙과 동일.
        bool t_flip = _attacker.BoundCard?.ownerIndex != TurnState.LocalOwnerIndex;

        // 테스터는 규칙 계층을 안 돌리므로 무쌍 광역 대상만 같은 규칙으로 직접 골라 넘긴다.
        CardView t_splash = _attacker.BoundCard.HasKeyword(CardKeyword.Peerless)
            ? FindSplashTarget(_defender) : null;
        var (t_preKw, t_atKw) = AttackFlow.Keywords(_attacker.BoundCard);

        // 무장 VFX는 무장 시점에 켜진다. 버튼 공격은 무장 단계를 안 거치므로 여기서 대신 켜준다
        // (탭/드래그 공격은 FocusWeapon에서 이미 켜져 있고, SetArmedVfx는 중복 호출에 안전).
        _attacker.SetArmedVfx(true);

        // _onEffect = 접촉 시점. 체력이 낮은 쪽만 사망시키고(체력은 안 깎는다) 피격 VFX를 띄운다.
        void OnEffect()
        {
            if (this.killWeakerSide)
            {
                KillIfWeaker(_defender, _attacker);
                KillIfWeaker(_attacker, _defender);   // 반격
                if (t_splash != null) KillIfWeaker(t_splash, _attacker);
            }
            this.hitVfx.Spawn(_defender.transform, t_flip);
            if (t_splash != null) this.hitVfx.Spawn(t_splash.transform, t_flip);
            if (this.hitVfxOnCounter)
                this.hitVfx.Spawn(_attacker.transform, !t_flip);
        }

        // 필드 모델이 없는 테스터에서도 매치포인트 접근 줌·국소 감속을 함께 확인한다.
        BattleFinisher.ArmApproachPreview();

        // 광역 대상이 있으면 splash 경로 — AttackSequence가 거기서 무쌍 연출로 갈린다.
        await AttackSequence.PlaySplash(_attacker, _defender, t_effect,
            _onEffect: OnEffect, _splashView: t_splash,
            _preEffectKw: t_preKw, _atEffectKw: t_atKw,
            _forceSpecial: this.useSpecialCinema);

        // 처형 연출. 인게임 조건과 같게 **처치 + 공격자 생존**일 때만 — 공격자가 반격에 같이 죽었으면 뜨면 안 된다.
        // 리스폰 전에 불러야 죽은 상태 기준으로 판정된다.
        if (this.useExecution
            && _defender.BoundCard != null && _defender.BoundCard.hp <= 0
            && _attacker.BoundCard != null && _attacker.BoundCard.hp > 0)
            ExecutionVfx.Play(_attacker);

        // 사망하면 다시 채워 반복 가능하게.
        if (_defender.BoundCard != null && _defender.BoundCard.hp <= 0) Respawn(_defender);
        if (_attacker.BoundCard != null && _attacker.BoundCard.hp <= 0) Respawn(_attacker);
        if (t_splash?.BoundCard != null && t_splash.BoundCard.hp <= 0) Respawn(t_splash);
    }

    // ── 연결 재생 ────────────────────────────────────────────────────────

    /// <summary>인스펙터에 적힌 마디를 순서대로 이어 재생한다. 인게임 한 번의 공격이
    /// 실제로 어떻게 이어지는지(선피해 → 공격 → 공격 후 효과 → 교대) 한 호흡으로 보는 용도.
    ///
    /// 각 마디는 단발 버튼과 <b>같은 진입점</b>을 부른다 — 시퀀스 전용 사본을 만들면
    /// 단발로 볼 때와 이어 볼 때가 달라져 어느 쪽이 진짜인지 알 수 없게 된다.</summary>
    public void PlaySequence() => RunSequence().Forget();

    async UniTaskVoid RunSequence()
    {
        if (this.busy) return;
        if (this.sequence == null || this.sequence.Length == 0) return;

        this.busy = true;
        TurnState.InputAllowed = false;

        for (int i = 0; i < this.sequence.Length; i++)
        {
            await RunStep(this.sequence[i]);
            if (i < this.sequence.Length - 1) await Hold(this.stepGap);
        }

        TurnState.InputAllowed = true;
        this.busy = false;
    }

    async UniTask RunStep(AttackStep _step)
    {
        switch (_step)
        {
            case AttackStep.PlacedEmblem:
                await PlayEmblemAndWait(SynergyEmblemTiming.Placed);
                break;

            case AttackStep.TriggeredEmblem:
                await PlayEmblemAndWait(SynergyEmblemTiming.Triggered);
                break;

            case AttackStep.FlowWind:
                PlayFlowWind(this.flowStack);
                await Hold(this.untimedStepHold);
                break;

            case AttackStep.SwarmVolley:
                await PreviewSwarmVolley();   // 볼리는 착탄까지 기다릴 수 있다
                break;

            case AttackStep.Attack:
            {
                CardView t_atk = this.playerFieldView?.GetSlotView(0);
                CardView t_def = this.enemyFieldView?.GetSlotView(0);
                if (t_atk?.BoundCard != null && t_def?.BoundCard != null)
                    await AttackCore(t_atk, t_def);
                break;
            }

            case AttackStep.AfterAttackHeal:
                PlayCaretakerHeal();
                await Hold(this.untimedStepHold);
                break;

            case AttackStep.CunningSwap:
                await CunningCore();
                break;

            case AttackStep.Wait:
                await Hold(this.untimedStepHold);
                break;
        }
    }

    /// <summary>엠블럼을 그 타이밍으로 재생하고 <b>연출 길이만큼</b> 기다린다 — 다음 마디와 겹치지 않게.
    /// 배선이 없으면 기다리지 않고 넘어간다(경고는 PlayEmblem이 남긴다).</summary>
    async UniTask PlayEmblemAndWait(SynergyEmblemTiming _timing)
    {
        SynergyEmblemTiming t_prev = this.emblemTiming;
        this.emblemTiming = _timing;

        SynergyData t_syn = CurrentEmblemSynergy;
        bool t_wired = t_syn?.vfx != null && t_syn.vfx.PlaysEmblemAt(_timing);

        PlayEmblem();
        if (t_wired) await Hold(SynergyEmblemVfx.DurationOf(t_syn, _timing));

        this.emblemTiming = t_prev;   // 단발 재생 쪽 설정을 시퀀스가 바꿔 놓지 않게 되돌린다
    }

    static UniTask Hold(float _seconds)
        => UniTask.Delay((int)(Mathf.Max(0f, _seconds) * 1000f), DelayType.DeltaTime);

    /// <summary>처치 없이 처형 연출만(슬롯0). 무기가 꺼져 있어도 좌표는 유효하므로 그대로 뜬다.</summary>
    public void PlayExecutionOnly() => ExecutionVfx.Play(this.playerFieldView?.GetSlotView(0));

    /// <summary>교활 퇴장 연출만 따로 보기(슬롯0). 인게임에선 대기 큐와 스왑이 있어야 발동하는데
    /// 테스터엔 대기 큐가 없다 — 연출이 요구하는 입력(뒷면 상태)만 흉내내고 끝나면 되돌린다.
    /// 되돌리지 않으면 카드가 "???"로 남아 다음 실험이 뒷면으로 시작한다.</summary>
    public void PlayCunningExit() => PreviewCunningExit().Forget();

    async UniTaskVoid PreviewCunningExit()
    {
        if (this.busy) return;
        this.busy = true;
        await CunningCore();
        this.busy = false;
    }

    async UniTask CunningCore()
    {
        CardView t_cv = this.playerFieldView?.GetSlotView(0);
        CardInstance t_card = t_cv?.BoundCard;
        if (t_card == null) return;

        t_card.isRevealed = false;   // 연출이 반 바퀴 지점에서 재렌더할 때 뒷면이 나오게

        await CunningVfx.PlayExit(t_cv);

        // 교대 카드 등장분. 테스터엔 대기 큐가 없어 같은 카드가 앞면으로 되돌아오며 들어온다.
        t_card.isRevealed = true;
        if (t_cv != null) t_cv.Render(t_card);
        await CunningVfx.PlayEnter(t_cv);
    }

    // ── 키워드 연출 ──────────────────────────────────────────────────────

    static readonly CardKeyword[] k_previewableKeywords =
    {
        CardKeyword.Ranged,
        CardKeyword.Peerless,
        CardKeyword.Execution,
        CardKeyword.Taunt,
        CardKeyword.Cunning,
        CardKeyword.Healer,
    };

    static readonly KeywordPreviewKind[] k_glowOnly = { KeywordPreviewKind.Glow };
    static readonly KeywordPreviewKind[] k_glowAttack =
        { KeywordPreviewKind.Glow, KeywordPreviewKind.Attack };
    static readonly KeywordPreviewKind[] k_glowVfx =
        { KeywordPreviewKind.Glow, KeywordPreviewKind.Vfx };

    public CardKeyword[] PreviewableKeywords() => k_previewableKeywords;

    public CardKeyword SelectedKeyword
        => k_previewableKeywords[Mathf.Clamp(this.keywordIndex, 0, k_previewableKeywords.Length - 1)];

    public KeywordPreviewKind SelectedKeywordPreview => this.keywordPreview;

    public KeywordPreviewKind[] AvailableKeywordPreviews(CardKeyword _keyword)
    {
        switch (_keyword)
        {
            case CardKeyword.Ranged:
            case CardKeyword.Peerless:
                return k_glowAttack;
            case CardKeyword.Execution:
            case CardKeyword.Cunning:
            case CardKeyword.Healer:
                return k_glowVfx;
            default:
                return k_glowOnly;
        }
    }

    public void ClampKeywordPreviewToAvailable()
    {
        this.keywordIndex = Mathf.Clamp(this.keywordIndex, 0, k_previewableKeywords.Length - 1);
        KeywordPreviewKind[] t_available = AvailableKeywordPreviews(SelectedKeyword);
        foreach (KeywordPreviewKind t_kind in t_available)
            if (t_kind == this.keywordPreview) return;
        this.keywordPreview = t_available[0];
    }

    public void PlaySelectedKeyword() => PreviewSelectedKeyword().Forget();

    async UniTaskVoid PreviewSelectedKeyword()
    {
        if (this.busy) return;

        CardView t_view = this.playerFieldView?.GetSlotView(0);
        CardInstance t_card = t_view?.BoundCard;
        if (t_card == null) return;

        CardKeyword t_keyword = SelectedKeyword;
        CardKeyword t_originalKeywords = t_card.unlockedKeywords;

        this.busy = true;
        TurnState.InputAllowed = false;
        try
        {
            // 아이콘과 글로우가 같은 키워드를 보도록 해금 키워드로 임시 부여하고 다시 그린다.
            t_card.unlockedKeywords = (t_card.unlockedKeywords
                                     & ~(CardKeyword.Ranged | CardKeyword.Peerless | CardKeyword.Execution
                                       | CardKeyword.Taunt | CardKeyword.Cunning | CardKeyword.Healer))
                                     | t_keyword;
            t_view.Render(t_card);

            switch (this.keywordPreview)
            {
                case KeywordPreviewKind.Glow:
                    await t_view.PlayKeywordGlow(t_keyword);
                    break;
                case KeywordPreviewKind.Attack:
                {
                    CardView t_defender = this.enemyFieldView?.GetSlotView(0);
                    if (t_defender?.BoundCard != null) await AttackCore(t_view, t_defender);
                    break;
                }
                case KeywordPreviewKind.Vfx:
                    if (t_keyword == CardKeyword.Execution) ExecutionVfx.Play(t_view);
                    else if (t_keyword == CardKeyword.Cunning) await CunningCore();
                    else if (t_keyword == CardKeyword.Healer) PlayCaretakerHeal();
                    break;
            }
        }
        finally
        {
            // 공격 중 리스폰했으면 새 카드 인스턴스를 죽은 원본으로 덮어쓰지 않는다.
            if (ReferenceEquals(t_view.BoundCard, t_card))
            {
                t_card.unlockedKeywords = t_originalKeywords;
                t_view.Render(t_card);
            }
            TurnState.InputAllowed = true;
            this.busy = false;
        }
    }

    // ── 시너지 연출 ──────────────────────────────────────────────────────
    // 전부 실제 게임이 쓰는 진입점 그대로 부른다. 다른 건 상태 변경이 없다는 것뿐(순수 연출이라 그래도 된다).

    /// <summary>인스펙터에서 고른 연출 하나를 재생한다. 버튼이 연출 종류를 아는 유일한 지점이 여기다 —
    /// 새 연출은 <see cref="SynergyPreviewKind"/>에 값 하나 + 여기 분기 하나로 끝난다.</summary>
    public void PlaySelectedSynergy()
    {
        switch (this.synergyPreview)
        {
            case SynergyPreviewKind.Emblem:        PlayEmblem();                  break;
            case SynergyPreviewKind.SwarmVolley:   PlaySwarmVolley();             break;
            case SynergyPreviewKind.FlowWind:      PlayFlowWind(this.flowStack);  break;
            case SynergyPreviewKind.CaretakerHeal: PlayCaretakerHeal();           break;
        }
    }

    /// <summary>지금 고른 시너지가 <b>실제로 가진</b> 연출만. 인스펙터 연출 드롭다운이 이 목록으로 채워진다 —
    /// 없는 연출까지 고를 수 있으면 "눌러도 아무 일이 없다"가 배선 누락인지 버그인지 구분되지 않는다.
    ///
    /// 판정 근거는 두 가지다. 엠블럼은 연출 에셋에 그 타이밍 줄이 있는지(SynergyVfxConfig),
    /// 고유 연출은 그 시너지가 그 효과를 실제로 쓰는지(SynergyEffect 타입)로 본다 —
    /// 효과 쪽을 보는 이유는 연출 에셋이 아직 없어도 "이 시너지 것"이라는 사실은 효과가 이미 알고 있어서다.</summary>
    public SynergyPreviewKind[] AvailablePreviews()
    {
        SynergyData t_syn = CurrentEmblemSynergy;
        if (t_syn == null) return new SynergyPreviewKind[0];

        var t_list = new List<SynergyPreviewKind>();

        bool t_hasEmblem = t_syn.vfx != null
                        && (t_syn.vfx.PlaysEmblemAt(SynergyEmblemTiming.Placed)
                         || t_syn.vfx.PlaysEmblemAt(SynergyEmblemTiming.Triggered));
        if (t_hasEmblem) t_list.Add(SynergyPreviewKind.Emblem);

        if (HasEffect<SwarmSynergyEffect>(t_syn))     t_list.Add(SynergyPreviewKind.SwarmVolley);
        if (HasEffect<FlowSynergyEffect>(t_syn))      t_list.Add(SynergyPreviewKind.FlowWind);
        if (HasEffect<CaretakerSynergyEffect>(t_syn)) t_list.Add(SynergyPreviewKind.CaretakerHeal);

        return t_list.ToArray();
    }

    static bool HasEffect<T>(SynergyData _syn) where T : SynergyEffect
    {
        if (_syn?.tiers == null) return false;
        foreach (SynergyTier t_tier in _syn.tiers)
        {
            if (t_tier?.effects == null) continue;
            foreach (SynergyEffect t_eff in t_tier.effects)
                if (t_eff is T) return true;
        }
        return false;
    }

    /// <summary>고른 시너지가 그 연출을 갖고 있지 않으면 가진 것 중 첫 번째로 바꾼다(없으면 그대로).
    /// 시너지를 넘길 때마다 인스펙터가 부른다 — 안 그러면 이전 시너지의 연출이 선택으로 남는다.</summary>
    public void ClampPreviewToAvailable()
    {
        SynergyPreviewKind[] t_available = AvailablePreviews();
        if (t_available.Length == 0) return;

        foreach (SynergyPreviewKind t_kind in t_available)
            if (t_kind == this.synergyPreview) return;

        this.synergyPreview = t_available[0];
    }

    public SynergyPreviewKind SelectedPreview => this.synergyPreview;

    /// <summary>지금 고른 연출이 쓰는 값 필드 이름들. 인스펙터가 <b>고른 연출에 해당하는 칸만</b> 그리는 근거다
    /// (안 쓰는 값이 같이 떠 있으면 어느 값이 지금 화면에 영향을 주는지 알 수 없다).</summary>
    public static string[] FieldsFor(SynergyPreviewKind _kind)
    {
        switch (_kind)
        {
            case SynergyPreviewKind.Emblem:
                return new[] { "emblemSlot", "emblemTiming", "emblemAutoReplay", "emblemReplayGap" };
            case SynergyPreviewKind.SwarmVolley:   return new[] { "swarmDamagePerShot" };
            case SynergyPreviewKind.FlowWind:      return new[] { "flowStack" };
            case SynergyPreviewKind.CaretakerHeal: return new[] { "caretakerHeal" };
            default:                           return new string[0];
        }
    }

    /// <summary>무리 선피해 볼리: 아군 라이브 슬롯 전원이 적 슬롯0에게 한 발씩.</summary>
    public void PlaySwarmVolley() => PreviewSwarmVolley().Forget();

    async UniTask PreviewSwarmVolley()
    {
        CardView t_target = this.enemyFieldView?.GetSlotView(0);
        if (t_target == null || t_target.BoundCard == null) return;

        var t_sources = new List<CardView>();
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardView t_v = this.playerFieldView?.GetSlotView(i);
            if (t_v != null && t_v.BoundCard != null) t_sources.Add(t_v);
        }
        if (t_sources.Count == 0) return;

        var t_damages = new int[t_sources.Count];
        for (int i = 0; i < t_damages.Length; i++) t_damages[i] = Mathf.Max(0, this.swarmDamagePerShot);

        await SwarmVfx.PlayVolley(t_sources, t_target, t_damages,
                                  t_target.BoundCard.hp, t_target.BoundCard.bonusHp, this.swarmVfx);
    }

    /// <summary>흐름 바람(아군 필드). 중첩이 커질수록 커지는 연출이라 스택 값을 받는다.</summary>
    public void PlayFlowWind(int _stack = 1)
        => SynergyVfx.PlayFlowWind(this.playerFieldView, this.flowVfx, _stack);

    /// <summary>돌보미: 힐러와 같은 연출(HealVfx)을 아군 전원에게. 발사 주체는 슬롯0.</summary>
    public void PlayCaretakerHeal()
    {
        CardView t_src = this.playerFieldView?.GetSlotView(0);
        if (t_src == null) return;

        var t_targets = new List<(CardView view, CardInstance card, int amount)>();
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardView t_v = this.playerFieldView?.GetSlotView(i);
            if (t_v != null && t_v.BoundCard != null)
                t_targets.Add((t_v, t_v.BoundCard, Mathf.Max(0, this.caretakerHeal)));
        }
        if (t_targets.Count > 0) HealVfx.PlayHealBurst(t_src, t_targets);
    }

    /// <summary>지금 고른 시너지의 그 타이밍 엠블럼 1회. 게임과 같은 진입점(SynergyEmblemVfx.Play)을 탄다.
    /// 그 타이밍 줄이 없으면 조용히 아무것도 안 뜨므로, 왜 안 뜨는지 로그로 알려준다
    /// (아무 반응이 없으면 "연출이 깨졌다"와 "배선이 없다"를 구분할 수 없다).</summary>
    public void PlayEmblem()
    {
        SynergyData t_syn = CurrentEmblemSynergy;
        CardView t_view = this.playerFieldView?.GetSlotView(this.emblemSlot);
        if (t_syn == null || t_view == null) return;

        if (t_syn.vfx == null || !t_syn.vfx.PlaysEmblemAt(this.emblemTiming))
        {
            Debug.LogWarning($"[AttackTest] {t_syn.name}: {this.emblemTiming} 타이밍 엠블럼 배선 없음"
                           + $"{(t_syn.vfx == null ? " (vfx SO 자체가 비어 있다)" : "")}");
            return;
        }
        SynergyEmblemVfx.Play(t_view, t_syn, this.emblemTiming);
    }

    /// <summary>고른 시너지가 바뀐 뒤 부른다. 자동반복 타이머를 리셋해 새 시너지가 바로 뜨게 한다.
    ///
    /// <b>인덱스 자체는 여기서 건드리지 않는다</b> — 인스펙터가 SerializedProperty로 쓰기 때문이다.
    /// 런타임 필드를 직접 바꾸면 그 프레임 끝의 ApplyModifiedProperties가 옛 값으로 되돌려 놓는다
    /// (드롭다운을 바꿔도 계속 첫 시너지가 재생되던 원인).</summary>
    public void OnSynergySelectionChanged() => this.emblemReplayTimer = 0f;

    /// <summary>드롭다운에 채울 시너지 이름 목록(배선 없는 것은 표시로 구분). 목록 자체는 자동 배선분이다.</summary>
    public string[] SynergyNames()
    {
        int t_n = this.emblemSynergies != null ? this.emblemSynergies.Length : 0;
        var t_names = new string[t_n];
        for (int i = 0; i < t_n; i++)
        {
            SynergyData t_s = this.emblemSynergies[i];
            t_names[i] = t_s == null ? "(비어 있음)"
                       : t_s.vfx == null ? t_s.name + "  · 연출 없음"
                       : t_s.name;
        }
        return t_names;
    }

    public int SynergyIndex => WrapIndex(this.synergyIndex);

    /// <summary>자동반복이 켜져 있나. 인스펙터가 "스스로 변하는 중"인지 판단해 다시 그릴 때 쓴다.</summary>
    public bool EmblemAutoReplayOn => this.emblemAutoReplay;

    // ── VFX 후보 넘기기 ──────────────────────────────────────────────────

    public void CycleAttackVfx(int _step) { this.attackVfx.Cycle(_step); PushArmedVfx(); }
    public void CycleHitVfx(int _step)    => this.hitVfx.Cycle(_step);

    public void RescanVfx()
    {
        this.attackVfx.Rescan();
        this.hitVfx.Rescan();
        PushArmedVfx();
    }

    /// <summary>무장 없이 슬롯0에 무장 VFX를 강제로 켜본다(다시 누르면 끔).</summary>
    public void ToggleArmedPreview()
    {
        this.armedPreview = !this.armedPreview;
        this.playerFieldView?.GetSlotView(0)?.SetArmedVfx(this.armedPreview);
    }

    public bool ArmedPreview => this.armedPreview;

    /// <summary>피격 VFX만 적 슬롯0 위치에 한 번 띄운다.</summary>
    public void SpawnHitVfxPreview() => this.hitVfx.Spawn(this.enemyFieldView?.GetSlotView(0)?.transform);

    public void LogVfxPaths()
        => Debug.Log($"[AttackTest] 무장VFX = {this.attackVfx.CurrentPath}\n          피격VFX = {this.hitVfx.CurrentPath}");

    /// <summary>지금 고른 후보를 모든 슬롯의 무장 VFX 프리팹으로 밀어넣는다.
    /// 스폰 시점은 CardView가 쥐고 있으므로(무장~접촉) 테스터는 "무엇을 쓸지"만 정한다.</summary>
    void PushArmedVfx()
    {
        GameObject t_prefab = this.attackVfx.Current;
        PushArmedVfx(this.playerFieldView, t_prefab);
        PushArmedVfx(this.enemyFieldView,  t_prefab);
    }

    static void PushArmedVfx(BattleFieldView _fv, GameObject _prefab)
    {
        if (_fv == null) return;
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
            _fv.GetSlotView(i)?.SetArmedVfxPrefab(_prefab);
    }

    // ── 내부 ────────────────────────────────────────────────────────────

    /// <summary>연출 SO 자동 배선(에디터 전용). 씬에 꽂힌 값이 있으면 그걸 존중하고, **비어 있을 때만** 채운다.
    ///
    /// 시너지 연출은 시너지가 늘 때마다 에셋이 하나씩 는다 — 씬에 손으로 꽂는 방식이면 새 연출을 만든 사람이
    /// 배선을 잊는 순간 "테스터에선 안 보인다"가 된다. 그래서 프로젝트의 SynergyData 전부를 목록으로 잡는다.
    /// 무리/흐름 고유 연출도 그 시너지의 <c>SynergyData.vfx</c>에서 꺼낸다 — 인게임이 타는 것과 같은 에셋이어야
    /// 테스트 결과가 인게임과 일치한다.</summary>
    void ResolveSynergyAssets()
    {
#if UNITY_EDITOR
        if (this.emblemSynergies == null || this.emblemSynergies.Length == 0)
        {
            string[] t_guids = UnityEditor.AssetDatabase.FindAssets("t:SynergyData");
            var t_list = new List<SynergyData>();
            foreach (string t_guid in t_guids)
            {
                var t_so = UnityEditor.AssetDatabase.LoadAssetAtPath<SynergyData>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(t_guid));
                if (t_so != null) t_list.Add(t_so);
            }
            t_list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));   // 실행마다 순서가 흔들리지 않게
            this.emblemSynergies = t_list.ToArray();
        }

        foreach (SynergyData t_syn in this.emblemSynergies)
        {
            if (this.swarmVfx == null) this.swarmVfx = t_syn?.vfx as SwarmSynergyVfxConfig;
            if (this.flowVfx  == null) this.flowVfx  = t_syn?.vfx as FlowSynergyVfxConfig;
        }
#endif
        this.synergyIndex = WrapIndex(this.synergyIndex);
    }

    /// <summary>쓸 SO를 정하고 GameTiming에 주입한다. **주입만 한다** — 값을 되돌려 쓰지 않는다.
    /// 필드가 비어 있으면 프로젝트에서 찾아 쓴다(에디터 전용).</summary>
    void ResolveConfig()
    {
#if UNITY_EDITOR
        if (this.timingConfig == null)
        {
            string[] t_guids = UnityEditor.AssetDatabase.FindAssets("t:BattleTimingConfig");
            if (t_guids.Length > 0)
                this.timingConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<BattleTimingConfig>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(t_guids[0]));
        }
#endif
        if (this.timingConfig != null) GameTiming.SetConfig(this.timingConfig);
    }

    SynergyData CurrentEmblemSynergy
        => (this.emblemSynergies != null && this.emblemSynergies.Length > 0)
            ? this.emblemSynergies[WrapIndex(this.synergyIndex)] : null;

    int WrapIndex(int _i)
    {
        int t_n = this.emblemSynergies != null ? this.emblemSynergies.Length : 0;
        return t_n > 0 ? ((_i % t_n) + t_n) % t_n : 0;
    }

    float emblemReplayTimer;
    int   emblemDirtyStamp = -1;

    /// <summary>연출 SO 핫 리로드. 값 자체는 원래 살아 있다(SynergyEmblemVfx가 매 재생마다 SO를 다시 읽는다)
    /// — 문제는 재생이 1회성이라 "고친 값을 보려면 다시 눌러야 한다"는 것. 그래서 ① 끝나는 대로 다시 재생,
    /// ② 인스펙터에서 SO를 고치는 순간 즉시 다시 재생(dirty 카운터 감시).</summary>
    void PollEmblemAutoReplay()
    {
        if (!this.emblemAutoReplay) return;

        SynergyData t_syn = CurrentEmblemSynergy;
        // 배선 없는 시너지에 걸려 있으면 조용히 쉰다 — 반복 재생이라 경고를 그대로 두면 콘솔이 초당 몇 줄로 찬다.
        if (t_syn == null || t_syn.vfx == null || !t_syn.vfx.PlaysEmblemAt(this.emblemTiming)) return;

#if UNITY_EDITOR
        int t_stamp = UnityEditor.EditorUtility.GetDirtyCount(t_syn.vfx);
        if (t_stamp != this.emblemDirtyStamp)
        {
            this.emblemDirtyStamp = t_stamp;
            this.emblemReplayTimer = 0f;
        }
#endif
        this.emblemReplayTimer -= Time.deltaTime;
        if (this.emblemReplayTimer > 0f) return;

        PlayEmblem();
        this.emblemReplayTimer = SynergyEmblemVfx.DurationOf(t_syn, this.emblemTiming) + this.emblemReplayGap;
    }

    /// <summary>대상 옆칸(오른쪽 우선)에서 카드가 있는 슬롯 하나. 인게임은 인접 중 무작위(MatchRandom)지만
    /// 테스터는 고정으로 뽑는다 — 연출 확인이 목적이라 매번 같은 쪽이 편하고, RNG 스트림도 건드리지 않는다.</summary>
    CardView FindSplashTarget(CardView _defender)
    {
        CardInstance t_d = _defender?.BoundCard;
        if (t_d == null) return null;

        BattleFieldView t_fv = t_d.ownerIndex == 0 ? this.playerFieldView : this.enemyFieldView;
        if (t_fv == null) return null;

        CardView t_right = SlotWithCard(t_fv, t_d.slotIndex + 1);
        return t_right != null ? t_right : SlotWithCard(t_fv, t_d.slotIndex - 1);
    }

    static CardView SlotWithCard(BattleFieldView _fv, int _slot)
    {
        if (_slot < 0 || _slot >= BattleField.SLOT_COUNT) return null;
        CardView t_v = _fv.GetSlotView(_slot);
        return (t_v != null && t_v.BoundCard != null) ? t_v : null;
    }

    /// <summary>_self의 총 체력이 _other보다 **낮을 때만** 즉사시킨다. 실제 데미지 계산은 하지 않는다 —
    /// 연출(사망/생존) 분기만 보려는 것이라 체력을 조금씩 깎으면 반복 확인이 번거롭다.
    /// 사망 판정은 AttackSequence가 hp&lt;=0으로 읽으므로 hp를 0으로 만든다.</summary>
    static void KillIfWeaker(CardView _self, CardView _other)
    {
        CardInstance t_a = _self?.BoundCard;
        CardInstance t_b = _other?.BoundCard;
        if (t_a == null || t_b == null) return;

        if (t_a.hp + t_a.bonusHp >= t_b.hp + t_b.bonusHp) return;
        t_a.bonusHp = 0;
        t_a.hp      = 0;
    }

    void Respawn(CardView _cv)
    {
        CardInstance t_old = _cv.BoundCard;
        if (t_old == null) return;
        var t_fresh = new CardInstance(t_old.data, t_old.ownerIndex) { isRevealed = true, slotIndex = t_old.slotIndex };
        _cv.Render(t_fresh);
    }
}
