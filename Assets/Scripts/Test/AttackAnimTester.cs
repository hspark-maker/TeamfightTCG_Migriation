using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 공격 "연출만" 확인용 테스터(3v3, BattleScene 구성 재사용). 실제 데미지 없음(_onEffect 미전달)
/// → HP 불변·사망 없음 → 무한 반복. 두 BattleFieldView의 슬롯에 카드를 직접 렌더해 채운다.
/// 조작: [P] 플레이어 슬롯0 → 적 슬롯0 / [E] 적 슬롯0 → 플레이어 슬롯0 / 카드 탭·드래그 제스처로 임의 대상.
/// 인스펙터에서 연출 종류(일반/특별)와 박치기 각 단계 초/거리/각을 Play 중 실시간 조정.
/// </summary>
public class AttackAnimTester : MonoBehaviour
{
    [Header("Field Views (3v3)")]
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;

    [Header("Cards (슬롯 0~2)")]
    [SerializeField] CardData[] playerCards = new CardData[3];
    [SerializeField] CardData[] enemyCards  = new CardData[3];

    [Header("연출 종류")]
    [Tooltip("체크=특별(시네마 1vs1), 해제=일반(박치기).")]
    [SerializeField] bool useSpecialCinema = false;

    [Tooltip("체크=무쌍 연출. 공격자에게 무쌍 키워드를 임시로 주고 대상 옆칸을 광역 대상으로 넘긴다. [O] 토글.\n" +
             "옆칸이 비어 있으면 광역 대상이 없어 일반(박치기)으로 떨어진다 — 인게임 규칙과 같다.")]
    [SerializeField] bool usePeerless = false;

    [Tooltip("체크=처치에 성공하면 처형 연출(무기 양 끝 스파크 + 마법진) 재생. [X] 토글, [Z] 즉시 미리보기.\n" +
             "인게임에선 AttackFlow가 처형 권한을 보고 띄우지만 테스터는 규칙 계층을 안 돌리므로 여기서 대신 부른다.")]
    [SerializeField] bool useExecution = true;

    [Header("무장 VFX (공격자 기준, 무장~접촉까지 부착 유지)")]
    // 스폰/해제 시점은 CardView가 쥔다(무장=FocusWeapon(true), 해제=AttackSequence 접촉 시점).
    // 여기선 "어떤 프리팹을 쓸지"만 넘긴다 → localOffset/lifetime 같은 VfxSlot 자체 배치값은 안 쓰인다.
    [SerializeField] VfxSlot attackVfx = new VfxSlot { localOffset = new Vector3(0f, 0f, -0.5f), lifetime = 2f };
    [Header("피격 VFX (방어자 기준, 접촉 시점)")]
    [SerializeField] VfxSlot hitVfx    = new VfxSlot { localOffset = new Vector3(0f, 0f, -0.5f), lifetime = 1.5f };
    [Tooltip("반격 데미지가 있으면 공격자에게도 피격 VFX 재생.")]
    [SerializeField] bool hitVfxOnCounter = true;

    [Header("사망 판정")]
    [Tooltip("체력이 상대보다 낮은 쪽만 죽는다. 체력은 깎지 않는다(연출 확인용) — 같으면 둘 다 생존.")]
    [SerializeField] bool killWeakerSide = true;

    [Header("타이밍 SO (인게임 진실원)")]
    [Tooltip("비우면 프로젝트에서 자동으로 찾는다. [T] 불러오기 / [K] 이 값들을 SO에 저장(에디터 전용).")]
    [SerializeField] BattleTimingConfig timingConfig;
    [Tooltip("SO 값이 바뀌면 자동으로 다시 불러온다(인스펙터에서 SO를 직접 고칠 때). 끄면 [T]로 수동.")]
    [SerializeField] bool autoReloadConfig = true;

    [Header("박치기 타이밍(초) / 거리 / 각도  — Play 중 조정 가능")]
    [SerializeField] float windDur    = 0.07f;
    [SerializeField] float windDist   = 0.22f;
    [SerializeField] float inDur      = 0.09f;
    [SerializeField] float recoilDur  = 0.09f;
    [SerializeField] float recoilDist = 0.35f;
    [SerializeField] float outDur     = 0.16f;
    [Range(0f, 1f)]  [SerializeField] float lungeT  = 0.62f;
    [Range(0f, 80f)] [SerializeField] float maxLean = 40f;

    bool busy;
    bool armedPreview;   // [V] 토글로 켜둔 미리보기 상태(무장 없이 강제 표시)

    void Start()
    {
        TurnState.LocalOwnerIndex = 0;   // 플레이어(owner 0)가 로컬 → 탭 무장 대상.
        TurnState.InputAllowed    = true;

        // 부트스트랩이 없는 씬이라 GameTiming에 아무도 SO를 안 넣어준다 → 그대로 두면
        // GameTiming.Battle이 **코드 기본값짜리 임시 인스턴스**를 만들어 쓰고, 그걸 읽는 연출
        // (무쌍 등 슬라이더가 없는 것들)은 인스펙터에서 SO를 고쳐도 아무 반응이 없다.
        ResolveConfig();

        // 부트스트랩(GameInitializer) off라 애니메이터 캐시 초기화가 안 됨 → 첫 연출/가이드 누락 방지 위해 직접 호출.
        this.playerFieldView.InitializeAnimators();
        this.enemyFieldView.InitializeAnimators();

        RenderField(this.playerFieldView, this.playerCards, 0);
        RenderField(this.enemyFieldView,  this.enemyCards,  1);

        PushArmedVfx();

        CardView.OnAttack += HandleAttack;
    }

    void OnDestroy() => CardView.OnAttack -= HandleAttack;

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

    void Update()
    {
        if (this.autoReloadConfig) PollConfigChanged();
        HandleVfxKeys();
        if (this.busy) return;
        if (Input.GetKeyDown(KeyCode.P)) TryAttack(this.playerFieldView, 0, this.enemyFieldView, 0);
        if (Input.GetKeyDown(KeyCode.E)) TryAttack(this.enemyFieldView, 0, this.playerFieldView, 0);
    }

    // ── VFX 브라우징 키 ────────────────────────────────────────────────
    // ←/→ 공격 VFX 넘기기, ↑/↓ 피격 VFX 넘기기, 1/2 각각 on/off,
    // V 공격 VFX 미리보기, B 피격 VFX 미리보기, R 폴더 재스캔, L 현재 선택 경로 로그.
    void HandleVfxKeys()
    {
        bool t_armedChanged = false;
        if (Input.GetKeyDown(KeyCode.RightArrow)) { this.attackVfx.Cycle(+1); t_armedChanged = true; }
        if (Input.GetKeyDown(KeyCode.LeftArrow))  { this.attackVfx.Cycle(-1); t_armedChanged = true; }
        if (Input.GetKeyDown(KeyCode.UpArrow))    this.hitVfx.Cycle(+1);
        if (Input.GetKeyDown(KeyCode.DownArrow))  this.hitVfx.Cycle(-1);

        if (Input.GetKeyDown(KeyCode.Alpha1)) { this.attackVfx.use = !this.attackVfx.use; t_armedChanged = true; }
        if (Input.GetKeyDown(KeyCode.Alpha2)) this.hitVfx.use = !this.hitVfx.use;

        if (Input.GetKeyDown(KeyCode.O)) this.usePeerless  = !this.usePeerless;
        if (Input.GetKeyDown(KeyCode.X)) this.useExecution = !this.useExecution;
        // 처치 없이 처형 연출만 보기. 무기가 꺼져 있어도 좌표는 유효하므로 그대로 뜬다.
        if (Input.GetKeyDown(KeyCode.Z)) ExecutionVfx.Play(this.playerFieldView?.GetSlotView(0));
        if (Input.GetKeyDown(KeyCode.C)) PreviewCunningExit().Forget();
        if (Input.GetKeyDown(KeyCode.R)) { this.attackVfx.Rescan(); this.hitVfx.Rescan(); t_armedChanged = true; }
        if (Input.GetKeyDown(KeyCode.T)) PullTuningFromConfig();
        if (Input.GetKeyDown(KeyCode.K)) ApplyTuningToConfig();
        if (Input.GetKeyDown(KeyCode.L))
            Debug.Log($"[AttackTest] 무장VFX = {this.attackVfx.CurrentPath}\n          피격VFX = {this.hitVfx.CurrentPath}");

        if (t_armedChanged) PushArmedVfx();

        // 미리보기: 무장 없이 슬롯0에 무장 VFX를 강제로 켜본다(다시 누르면 끔).
        if (Input.GetKeyDown(KeyCode.V))
        {
            this.armedPreview = !this.armedPreview;
            this.playerFieldView?.GetSlotView(0)?.SetArmedVfx(this.armedPreview);
        }
        if (Input.GetKeyDown(KeyCode.B)) this.hitVfx.Spawn(this.enemyFieldView?.GetSlotView(0)?.transform);
    }

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

    void TryAttack(BattleFieldView _atkFv, int _atkSlot, BattleFieldView _defFv, int _defSlot)
    {
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

    async UniTask RunAttack(CardView _attacker, CardView _defender)
    {
        this.busy = true;
        TurnState.InputAllowed = false;

        PushTuning();
        AttackEffect t_effect = _attacker.BoundCard?.data?.attackEffect;

        // 적(비로컬) 공격이면 오프셋/회전 반전 — AttackSequence의 t_flip 규칙과 동일.
        bool t_flip = _attacker.BoundCard?.ownerIndex != TurnState.LocalOwnerIndex;

        // 무쌍: 키워드를 런타임으로 주고 광역 대상을 직접 골라 넘긴다. 인게임에선 AttackFlow가 하는 일인데
        // 테스터는 규칙 계층을 안 돌리므로(데미지 없음) 여기서 같은 입력만 만들어 준다.
        // 대상 선정이 RNG(MatchRandom)를 소비하지 않는 것도 인게임과 다른 점 — 옆칸 고정으로 뽑는다.
        ApplyPeerlessKeyword(_attacker);
        CardView t_splash = this.usePeerless ? FindSplashTarget(_defender) : null;

        // 무장 VFX는 무장 시점에 켜진다. [P]/[E] 키 공격은 무장 단계를 안 거치므로 여기서 대신 켜준다
        // (드래그/탭 공격은 FocusWeapon에서 이미 켜져 있고, SetArmedVfx는 중복 호출에 안전).
        // 끄는 건 양쪽 다 AttackSequence의 접촉 시점이 담당한다.
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

        // 광역 대상이 있으면 splash 경로 — AttackSequence가 거기서 무쌍 연출로 갈린다.
        await AttackSequence.PlaySplash(_attacker, _defender, t_effect,
            _onEffect: OnEffect, _splashView: t_splash, _forceSpecial: this.useSpecialCinema);

        // 처형 연출. 인게임 조건과 같게 **처치 + 공격자 생존**일 때만 — 처형은 "죽였으니 한 번 더"라
        // 공격자가 반격에 같이 죽었으면 뜨면 안 된다. 리스폰 전에 불러야 죽은 상태 기준으로 판정된다.
        if (this.useExecution
            && _defender.BoundCard != null && _defender.BoundCard.hp <= 0
            && _attacker.BoundCard != null && _attacker.BoundCard.hp > 0)
            ExecutionVfx.Play(_attacker);

        // 사망하면 다시 채워 반복 가능하게.
        if (_defender.BoundCard != null && _defender.BoundCard.hp <= 0) Respawn(_defender);
        if (_attacker.BoundCard != null && _attacker.BoundCard.hp <= 0) Respawn(_attacker);
        if (t_splash?.BoundCard != null && t_splash.BoundCard.hp <= 0) Respawn(t_splash);

        TurnState.InputAllowed = true;
        this.busy = false;
    }

    /// <summary>교활 퇴장 연출만 따로 보기(슬롯0). 인게임에선 대기 큐와 스왑이 있어야 발동하는데
    /// 테스터엔 대기 큐가 없다 — 연출이 요구하는 입력(뒷면 상태)만 흉내내고 끝나면 되돌린다.
    /// 되돌리지 않으면 카드가 "???"로 남아 다음 실험이 뒷면으로 시작한다.</summary>
    async UniTaskVoid PreviewCunningExit()
    {
        if (this.busy) return;

        CardView t_cv = this.playerFieldView?.GetSlotView(0);
        CardInstance t_card = t_cv?.BoundCard;
        if (t_card == null) return;

        this.busy = true;
        t_card.isRevealed = false;   // 연출이 반 바퀴 지점에서 재렌더할 때 뒷면이 나오게

        await CunningVfx.PlayExit(t_cv);

        // 교대 카드 등장분. 테스터엔 대기 큐가 없어 같은 카드가 앞면으로 되돌아오며 들어온다.
        t_card.isRevealed = true;
        if (t_cv != null) t_cv.Render(t_card);
        await CunningVfx.PlayEnter(t_cv);

        this.busy = false;
    }

    /// <summary>토글 상태를 공격자 카드에 반영. **끌 때 반드시 지운다** — 런타임 키워드는 카드 인스턴스에
    /// 남으므로, 켜고 한 번 때린 카드가 토글을 꺼도 계속 무쌍으로 남으면 무엇을 보고 있는지 알 수 없게 된다.</summary>
    void ApplyPeerlessKeyword(CardView _attacker)
    {
        CardInstance t_c = _attacker?.BoundCard;
        if (t_c == null) return;

        if (this.usePeerless) t_c.runtimeKeywords |=  CardKeyword.Peerless;
        else                  t_c.runtimeKeywords &= ~CardKeyword.Peerless;
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

    /// <summary>_self의 총 체력이 _other보다 **낮을 때만** 즉사시킨다. 같거나 높으면 그대로 생존.
    /// 실제 데미지 계산은 하지 않는다 — 연출(사망/생존) 분기만 보려는 테스트용이라
    /// 체력을 조금씩 깎으면 몇 대 때려야 죽어 반복 확인이 번거롭다.
    /// 사망 판정은 AttackSequence가 hp<=0으로 읽으므로 hp를 0으로 만든다.</summary>
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

    BattleTimingConfig Config => this.timingConfig != null ? this.timingConfig : GameTiming.Battle;

    float configStamp;   // SO 값 지문. 바뀌면 "누가 인스펙터에서 SO를 고쳤다"는 뜻.

    /// <summary>쓸 SO를 정하고 **GameTiming에 주입**한다. 주입이 핵심 — 슬라이더가 있는 박치기와 달리
    /// 나머지 연출은 GameTiming.Battle을 직접 읽으므로, 여기서 넣어주지 않으면 이 씬에선 SO가 무시된다.
    /// 필드가 비어 있으면 프로젝트에서 찾아 쓴다(에디터 전용) — 씬 배선을 잊어도 인게임과 같은 값을 본다.</summary>
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
        if (this.timingConfig == null) return;

        GameTiming.SetConfig(this.timingConfig);
        this.configStamp = ConfigStamp(this.timingConfig);
        PullTuningFromConfig();
    }

    /// <summary>SO가 바뀌었는지 매 프레임 확인하고, 바뀌었으면 슬라이더까지 다시 채운다.
    /// SO는 살아 있는 오브젝트라 주입만 돼 있으면 값 변경은 즉시 먹지만, **테스터 슬라이더는
    /// 자기 값을 매 공격 덮어쓰므로**(PushTuning) 여기서 같이 갱신하지 않으면 박치기만 옛 값으로 남는다.</summary>
    void PollConfigChanged()
    {
        BattleTimingConfig t_so = this.timingConfig;
        if (t_so == null) { ResolveConfig(); return; }

        float t_stamp = ConfigStamp(t_so);
        if (Mathf.Approximately(t_stamp, this.configStamp)) return;

        this.configStamp = t_stamp;
        GameTiming.SetConfig(t_so);   // 에셋 자체가 교체됐을 수도 있으니 매번 다시 넣는다
        PullTuningFromConfig();       // 로그는 여기서 한 번만 나간다
    }

    /// <summary>SO 값 전체를 하나의 실수로 접은 지문. 항목마다 다른 계수를 곱해 **맞바꿔도 같은 합이
    /// 되지 않게** 한다(0.1↑ 0.1↓ 같은 변경을 놓치지 않으려고). 정확한 해시가 아니라 변경 감지용이다.</summary>
    static float ConfigStamp(BattleTimingConfig _so)
    {
        AttackSequence.NormalTuning   t_n = _so.NormalAttack;
        AttackSequence.PeerlessTuning t_p = _so.PeerlessAttack;

        return t_n.windDur      +  3f * t_n.windDist  +  5f * t_n.inDur     +  7f * t_n.recoilDur
             + 11f * t_n.recoilDist + 13f * t_n.outDur + 17f * t_n.lungeT   + 19f * t_n.maxLean
             + 23f * t_p.approachDur + 29f * t_p.turnDur + 31f * t_p.returnDur + 37f * t_p.hitStop
             + 41f * t_p.approachT   + 43f * t_p.maxTurn + 47f * t_p.swingFront;
    }

    /// <summary>SO 값을 테스터 슬라이더로 끌어온다 — 튜닝 시작점을 인게임과 같게 맞춘다.
    /// SO는 배속이 적용된 값을 노출하므로 그대로 받으면 배속 1일 때 raw와 같다.</summary>
    void PullTuningFromConfig()
    {
        AttackSequence.NormalTuning t_cfg = Config.NormalAttack;
        this.windDur    = t_cfg.windDur;
        this.windDist   = t_cfg.windDist;
        this.inDur      = t_cfg.inDur;
        this.recoilDur  = t_cfg.recoilDur;
        this.recoilDist = t_cfg.recoilDist;
        this.outDur     = t_cfg.outDur;
        this.lungeT     = t_cfg.lungeT;
        this.maxLean    = t_cfg.maxLean;
        Debug.Log("[AttackTest] SO 값 불러옴 — 인게임과 같은 시작점");
    }

    /// <summary>테스터에서 맞춘 값을 SO에 저장. 이게 있어야 "테스트 씬에서 튜닝 → 인게임 반영"이 닫힌다.
    /// 에디터 전용(빌드에선 저장할 대상 자체가 없다).</summary>
    void ApplyTuningToConfig()
    {
#if UNITY_EDITOR
        BattleTimingConfig t_so = Config;
        var t_obj = new UnityEditor.SerializedObject(t_so);
        void Set(string _name, float _v) => t_obj.FindProperty(_name).floatValue = _v;
        Set("atkWindDur",    this.windDur);
        Set("atkWindDist",   this.windDist);
        Set("atkInDur",      this.inDur);
        Set("atkRecoilDur",  this.recoilDur);
        Set("atkRecoilDist", this.recoilDist);
        Set("atkOutDur",     this.outDur);
        Set("atkLungeT",     this.lungeT);
        Set("atkMaxLean",    this.maxLean);
        t_obj.ApplyModifiedProperties();
        UnityEditor.EditorUtility.SetDirty(t_so);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"[AttackTest] 현재 값을 {t_so.name} 에 저장했다.");
#else
        Debug.LogWarning("[AttackTest] SO 저장은 에디터에서만 된다.");
#endif
    }

    void PushTuning()
    {
        AttackSequence.Normal = new AttackSequence.NormalTuning
        {
            windDur    = this.windDur,
            windDist   = this.windDist,
            inDur      = this.inDur,
            recoilDur  = this.recoilDur,
            recoilDist = this.recoilDist,
            outDur     = this.outDur,
            lungeT     = this.lungeT,
            maxLean    = this.maxLean,
        };
    }

    void OnGUI()
    {
        GUI.Label(new Rect(12, 10, 1100, 24),
            $"[P] 플레이어 공격   [E] 적 공격   |   연출: {(this.useSpecialCinema ? "특별(시네마)" : "일반(박치기)")}"
            + $"   |   [O] 무쌍 {(this.usePeerless ? "ON(옆칸 광역)" : "off")}"
            + $"   |   [X] 처형 {(this.useExecution ? "ON" : "off")} / [Z] 즉시   |   [C] 교활 퇴장   |   카드 탭/드래그도 가능");
        GUI.Label(new Rect(12, 34, 1100, 24),
            $"박치기 초: wind {this.windDur:0.00} / in {this.inDur:0.00} / recoil {this.recoilDur:0.00} / out {this.outDur:0.00}"
            + $"   [T] SO에서 불러오기  [K] SO에 저장   |   SO: {(this.timingConfig != null ? this.timingConfig.name : "없음")}"
            + $" {(this.autoReloadConfig ? "(자동 반영)" : "(수동)")}");
        GUI.Label(new Rect(12, 58, 1100, 24),
            $"무장VFX [←/→] {this.attackVfx.Index + 1}/{this.attackVfx.Count}  {this.attackVfx.CurrentName}   (무장~접촉까지 카드에 부착)");
        GUI.Label(new Rect(12, 82, 1100, 24),
            $"피격VFX [↑/↓] {this.hitVfx.Index + 1}/{this.hitVfx.Count}  {this.hitVfx.CurrentName}");
        GUI.Label(new Rect(12, 106, 1100, 24),
            $"[1] 무장VFX on/off  [2] 피격VFX on/off  [V] 무장VFX 미리보기({(this.armedPreview ? "ON" : "off")})  [B] 피격VFX 미리보기  [R] 폴더 재스캔  [L] 선택 경로 로그");
    }
}
