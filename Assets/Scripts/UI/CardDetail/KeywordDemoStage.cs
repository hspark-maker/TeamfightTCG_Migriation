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
public class KeywordDemoStage : MonoBehaviour
{
    const string ResourcePath = "UI/KeywordDemoStage";

    // 로비 카메라의 시야 밖. UiRectCapture가 같은 이유로 같은 자리를 쓴다.
    static readonly Vector3 StageOrigin = new Vector3(0f, 20000f, 0f);

    static KeywordDemoStage s_instance;

    [Header("무대 배선")]
    [Tooltip("이 무대만 담는 카메라. tag는 반드시 Untagged — MainCamera를 달면 로비 배경 영상이 이쪽으로 튄다.")]
    [SerializeField] Camera demoCamera;

    [Tooltip("공격자 자리. 방금 강화한 그 카드가 선다.")]
    [SerializeField] CardView slotAttacker;

    [Tooltip("맞는 쪽.")]
    [SerializeField] CardView slotDefender;

    [Tooltip("곁에 서는 쪽(무쌍 광역 대상·도발이 노리던 카드·힐러가 살리는 아군). 쓰지 않는 대본에서는 꺼둔다.")]
    [SerializeField] CardView slotNeighbor;

    [SerializeField] KeywordDemoConfig config;

    [Header("텍스처")]
    [Tooltip("구분선과 설명 사이의 띠에 들어갈 그림이라 클 이유가 없다. 모바일에서 이 값이 곧 비용이다.\n" +
             "비율은 띠 모양을 따라간다 — 그 자리가 거의 정사각이라 가로세로를 같게 둔다(RawImage 쪽 " +
             "AspectRatioFitter가 이 비율을 그대로 읽으므로 여기만 고치면 된다).")]
    [SerializeField] int textureWidth  = 512;
    [SerializeField] int textureHeight = 512;

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

    /// <summary>무대를 세운다. 위치를 Instantiate 인자로 주는 것이 중요하다 —
    /// <c>CardAnimator.Awake</c>가 그 프레임의 <c>transform.position</c>을 슬롯 자리로 못 박기 때문에,
    /// 세운 뒤에 옮기면 카드가 공격하고 **원점으로 돌아간다**.</summary>
    public static bool TryGet(out KeywordDemoStage _stage)
    {
        if (s_instance == null)
        {
            var t_prefab = Resources.Load<GameObject>(ResourcePath);
            if (t_prefab == null)
            {
                Debug.LogWarning($"[KeywordDemoStage] Resources/{ResourcePath} 를 찾지 못해 데모를 세울 수 없습니다.");
            }
            else
            {
                GameObject t_go = Instantiate(t_prefab, StageOrigin, Quaternion.identity);
                s_instance = t_go.GetComponent<KeywordDemoStage>();

                if (s_instance == null)
                {
                    Debug.LogWarning($"[KeywordDemoStage] Resources/{ResourcePath} 에 KeywordDemoStage가 없습니다(프리팹 배선 확인).");
                    Destroy(t_go);
                }
            }
        }

        _stage = s_instance;
        return _stage != null;
    }

    /// <summary>_card가 _keyword를 쓰는 모습을 반복 재생하고, 그 그림이 담긴 텍스처를 돌려준다.
    /// 세울 수 없으면 null — 부른 쪽은 띠를 끄고 글자만 보여주면 된다.</summary>
    public Texture Begin(CardData _card, CardKeyword _keyword)
    {
        if (_card == null || this.demoCamera == null || this.slotAttacker == null) return null;

        Stop();

        this.m_ownerIndex0   = TurnState.LocalOwnerIndex;
        this.m_inputAllowed0 = TurnState.InputAllowed;

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

    /// <summary>무대를 걷는다. 텍스처는 여기서 해제되므로 부른 쪽은 RawImage에서 먼저 떼야 한다.</summary>
    public void End()
    {
        Stop();
        Restore();
        ReleaseTexture();

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

    // 카드를 세운다. 공격자는 언제나 그 카드, 나머지는 저작(KeywordDemoConfig)이 정한다.
    // 힐러만 상대가 **아군**이라 owner가 갈린다 — 회복은 적에게 쏘는 것이 아니다.
    bool BindRoles(CardData _card, CardKeyword _keyword)
    {
        CardData t_opponent = null;
        CardData t_neighbor = null;
        this.config?.Roles(_keyword, out t_opponent, out t_neighbor);

        if (t_opponent == null)
        {
            Debug.LogWarning($"[KeywordDemoStage] {_keyword} 데모의 상대 카드가 저작되지 않았습니다(KeywordDemoConfig 확인).");
            return false;
        }

        int t_otherOwner = _keyword == CardKeyword.Healer ? 0 : 1;

        Render(this.slotAttacker, _card,       0, 0);
        Render(this.slotDefender, t_opponent,  t_otherOwner, 1);

        // 곁자리는 이 셋만 쓴다. 나머지 대본에서 세워두면 화면만 복잡해지고 시선이 갈린다.
        bool t_useNeighbor = _keyword == CardKeyword.Peerless
                          || _keyword == CardKeyword.Taunt
                          || _keyword == CardKeyword.Healer;

        if (this.slotNeighbor != null)
        {
            this.slotNeighbor.gameObject.SetActive(t_useNeighbor && t_neighbor != null);
            if (t_useNeighbor && t_neighbor != null) Render(this.slotNeighbor, t_neighbor, t_otherOwner, 2);
        }

        return true;
    }

    static void Render(CardView _view, CardData _data, int _owner, int _slot)
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
            await PlayOnce(_keyword, _token);
            if (_token.IsCancellationRequested) return;

            await UniTask.Delay(Ms(this.loopGap), cancellationToken: _token).SuppressCancellationThrow();
        }
    }

    async UniTask PlayOnce(CardKeyword _keyword, CancellationToken _token)
    {
        CardView t_atk = this.slotAttacker;
        CardView t_def = this.slotDefender;
        if (t_atk == null || t_def == null || t_atk.BoundCard == null) return;

        switch (_keyword)
        {
            // 도발은 "못 때린다"가 본체라 공격 자체가 일어나지 않는다 — 유일하게 AttackSequence를 안 탄다.
            case CardKeyword.Taunt:  await PlayTaunt(t_atk, t_def, _token);  return;
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

        AttackEffect t_effect = _atk.BoundCard?.data?.attackEffect;
        var (t_preKw, t_atKw) = AttackFlow.Keywords(_atk.BoundCard);

        // 무장 이펙트는 무장(탭) 단계에서 켜진다. 여기엔 그 단계가 없어 대신 켜준다(AttackAnimTester와 같은 이유).
        _atk.SetArmedVfx(true);

        CardView t_splashView = _splash != null && _splash.gameObject.activeSelf ? _splash : null;

        return AttackSequence.PlaySplash(_atk, _def, t_effect,
                                         _onEffect: null, _splashView: t_splashView,
                                         _preEffectKw: t_preKw, _atEffectKw: t_atKw,
                                         _forceSpecial: false);
    }

    // 도발: 공격자가 곁의 카드를 노리다 도발 카드에 막힌다. 파티클이 아니라 **거절**이 이 키워드의 그림이다.
    async UniTask PlayTaunt(CardView _atk, CardView _taunter, CancellationToken _token)
    {
        CardView t_wanted = this.slotNeighbor != null && this.slotNeighbor.gameObject.activeSelf
                                ? this.slotNeighbor : _taunter;

        t_wanted.SetHighlight(true);
        await UniTask.Delay(Ms(0.35f), cancellationToken: _token).SuppressCancellationThrow();
        if (_token.IsCancellationRequested) { t_wanted.SetHighlight(false); return; }

        // 노리던 쪽은 튕기고, 도발 카드가 "이쪽을 쳐라"로 대답한다.
        _atk.PlayRejectShake(_focus: true);
        _taunter.PlayKeywordGlow(CardKeyword.Taunt).Forget();
        _taunter.PlayAttentionPulse();

        await UniTask.Delay(Ms(0.6f), cancellationToken: _token).SuppressCancellationThrow();
        t_wanted.SetHighlight(false);
        if (_token.IsCancellationRequested) return;

        // 결국 도발 카드를 친다 — 막히는 것으로 끝내면 "그래서 어디를 치나"가 안 남는다.
        await Swing(_atk, _taunter, null, _token);
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
        this.m_texture = new RenderTexture(t_w, t_h, 24, RenderTextureFormat.ARGB32);
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
        TurnState.LocalOwnerIndex = this.m_ownerIndex0;
        TurnState.InputAllowed    = this.m_inputAllowed0;
    }

    static int Ms(float _seconds) => Mathf.Max(0, (int)(_seconds * 1000f));
}
