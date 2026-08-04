using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using TMPro;

/// <summary>카드 한 장 **위에 얹히는 장식 계층 전부**를 소유한다.
/// 키워드 아이콘 줄 / 키워드 프레임 장식 / 시너지 배지 / 키워드 글로우 / 패시브 글로우가 여기 있다.
///
/// 이 넷을 한 덩어리로 묶은 이유: 전부 "카드가 가진 키워드·시너지를 카드 위에 그린다"는 같은 일이고,
/// 표시 대상·순서 판정을 <see cref="CardVisualRules"/> 하나에서 받아온다는 계약도 공유한다.
/// 반대로 카드 본체 바인딩(이름·HP·일러스트·앞뒤면·카드백)은 <see cref="CardView"/>에 남는다 —
/// 그쪽은 CardInstance를 그대로 읽어 그리는 일이라 장식과 갱신 축이 다르다.
///
/// MonoBehaviour가 아니라 순수 C# 객체다 — CardView가 필드로 들고 생성한다.
/// 인스펙터 배선(keywordIcon*·synergyBadge*·keywordFrames·keywordGlowPrefab·passiveGlowSystem)은
/// CardView의 SerializeField에 그대로 남고 값만 생성자로 주입된다(프리팹/씬 YAML 재직렬화 회피).
///
/// 경계는 단방향이다: CardDecorView → CardVisualRules / GameTiming / TutorialConfig.
/// CardView는 여기 상태를 읽지 않는다(입력 컨트롤러가 쓰는 <see cref="LastBadgeState"/> 전달 셰임만).</summary>
public class CardDecorView
{
    #region Fields
    // 취소 토큰(파괴 시 글로우 정리)의 기준. 글로우는 카드 자식이 아니라 월드에 뜨므로
    // 카드가 죽어도 자동 파괴되지 않는다 — 소유자 수명을 알아야 흘리지 않는다.
    readonly CardView owner;

    // ── 주입값(단일 진실원은 CardView의 SerializeField) ──
    readonly CardAnimator     bodyAlphaSource;   // 카드 몸통이 도달할 알파의 기준(= cardAnim.FadeTarget)
    readonly Transform        keywordIconRoot;
    readonly GameObject       keywordIconPrefab;
    readonly KeywordIconConfig keywordIconConfig;
    readonly float            iconSpacing;
    readonly bool             keywordIconsUseSynergySlot;
    readonly Vector2          keywordIconStart;   // 시너지 자리를 쓸 때 첫 아이콘 좌표
    readonly Vector2          keywordIconStep;    // 그 다음 아이콘마다 더할 간격
    readonly CardView.KeywordFrame[] keywordFrames;
    readonly Transform        synergyBadgeRoot;
    readonly SynergyBadgeView synergyBadgePrefab;
    readonly float            synergyBadgeXPos;
    readonly float            synergyBadgeYStart;
    readonly float            synergyBadgeYStep;
    readonly int              synergyMaxBadges;
    readonly GameObject       keywordGlowPrefab;
    readonly ParticleSystem   passiveGlowSystem;

    // ── 장식 상태 ──
    // 키워드 → 그 키워드로 만든 아이콘 오브젝트. PlayKeywordGlow가 "어디에 글로우를 띄울지" 역참조한다.
    readonly Dictionary<CardKeyword, GameObject> iconMap = new Dictionary<CardKeyword, GameObject>();

    CardInstance lastBadgeCard;
    SynergyState lastBadgeState;
    #endregion

    public CardDecorView(
        CardView                _owner,
        CardAnimator            _bodyAlphaSource,
        Transform               _keywordIconRoot,
        GameObject              _keywordIconPrefab,
        KeywordIconConfig       _keywordIconConfig,
        float                   _iconSpacing,
        bool                    _keywordIconsUseSynergySlot,
        Vector2                 _keywordIconStart,
        Vector2                 _keywordIconStep,
        CardView.KeywordFrame[] _keywordFrames,
        Transform               _synergyBadgeRoot,
        SynergyBadgeView        _synergyBadgePrefab,
        float                   _synergyBadgeXPos,
        float                   _synergyBadgeYStart,
        float                   _synergyBadgeYStep,
        int                     _synergyMaxBadges,
        GameObject              _keywordGlowPrefab,
        ParticleSystem          _passiveGlowSystem)
    {
        this.owner                      = _owner;
        this.bodyAlphaSource            = _bodyAlphaSource;
        this.keywordIconRoot            = _keywordIconRoot;
        this.keywordIconPrefab          = _keywordIconPrefab;
        this.keywordIconConfig          = _keywordIconConfig;
        this.iconSpacing                = _iconSpacing;
        this.keywordIconsUseSynergySlot = _keywordIconsUseSynergySlot;
        this.keywordIconStart           = _keywordIconStart;
        this.keywordIconStep            = _keywordIconStep;
        this.keywordFrames              = _keywordFrames;
        this.synergyBadgeRoot           = _synergyBadgeRoot;
        this.synergyBadgePrefab         = _synergyBadgePrefab;
        this.synergyBadgeXPos           = _synergyBadgeXPos;
        this.synergyBadgeYStart         = _synergyBadgeYStart;
        this.synergyBadgeYStep          = _synergyBadgeYStep;
        this.synergyMaxBadges           = _synergyMaxBadges;
        this.keywordGlowPrefab          = _keywordGlowPrefab;
        this.passiveGlowSystem          = _passiveGlowSystem;
    }

    /// <summary>이 카드에 마지막으로 그려진 확정 시너지 스냅샷(없으면 null). 보유 장수 조회용 — 재계산 금지.
    /// 롱프레스 시너지 팝업(CardInputController)이 CardView.LastBadgeState 셰임을 거쳐 읽는다.</summary>
    public SynergyState LastBadgeState => this.lastBadgeState;

    /// <summary>장식 전체 갱신. CardView.Render가 카드 본체를 그린 **뒤** 한 번만 부른다 —
    /// 순서(아이콘 → 프레임 → 배지)는 그대로 유지한다.
    /// 빈 슬롯/뒷면 은닉 판정은 각 항목이 스스로 한다(정보 은닉 규칙의 단일 지점).</summary>
    public void Refresh(CardInstance _card, SynergyState _synergy)
    {
        RefreshKeywordIcons(_card);
        RefreshKeywordFrames(_card);
        RefreshSynergyBadges(_card, _synergy);
    }

    /// <summary>소유자(CardView) 파괴 시 정리. 장식 자식들의 트윈을 끊고 참조를 놓는다 —
    /// 아이콘/배지는 CardAnimator FadeView·Pop 트윈 대상이라 파괴 전 DOKill이 규약이다(Refresh와 동일).</summary>
    public void Cleanup()
    {
        KillChildTweens(KeywordAnchor);
        if (this.synergyBadgeRoot != KeywordAnchor) KillChildTweens(this.synergyBadgeRoot);
        this.iconMap.Clear();
        this.lastBadgeCard  = null;
        this.lastBadgeState = null;
    }

    #region Keyword icons
    // 아이콘 줄에는 캐릭터 고유 특성만 그린다. 일회용/디버프(무적·추가체력·전투 중 걸린 표식)는
    // 아예 표시하지 않는다 — 무엇을 띄울지 판정은 CardVisualRules 단독(아웃게임과 같은 호출).
    /// <summary>키워드 아이콘이 실제로 붙는 앵커. 시너지 자리를 쓰면 synergyBadgeRoot(좌측 세로열),
    /// 아니면 종전 keywordIconRoot(우하단 가로줄). 앵커 미배선이면 기존 루트로 폴백한다.</summary>
    Transform KeywordAnchor => this.keywordIconsUseSynergySlot && this.synergyBadgeRoot != null
        ? this.synergyBadgeRoot : this.keywordIconRoot;

    void RefreshKeywordIcons(CardInstance _card)
    {
        Transform t_root = KeywordAnchor;
        if (t_root == null || this.keywordIconPrefab == null || this.keywordIconConfig == null) return;

        foreach (Transform t_child in t_root)
        {
            // 아이콘 스프라이트가 FadeView tween 대상일 수 있음. 파괴 전 DOKill (루트 SetLink는 안 걸림).
            foreach (SpriteRenderer t_sr in t_child.GetComponentsInChildren<SpriteRenderer>(true))
                t_sr.DOKill();
            UnityEngine.Object.Destroy(t_child.gameObject);
        }

        this.iconMap.Clear();

        // 뒷면/빈 슬롯이면 아무것도 노출하지 않는다(정보 은닉).
        if (_card == null || !_card.isRevealed) return;

        // 여기 남는 건 월드좌표 배치와 스프라이트 주입뿐. None/아이콘 미등록은 규칙 쪽에서 걸러져 빈 리스트가 온다.
        List<CardVisualRules.KeywordIcon> t_icons =
            CardVisualRules.CollectKeywordIcons(CardVisualRules.IconKeywords(_card), this.keywordIconConfig);

        // 배치 두 가지. 시너지 자리: 카드 하단 줄(이름 왼쪽)에서 keywordIconStep만큼 밀며 나열.
        // 기존 자리: keywordIconRoot를 카드 오른쪽 아래 코너에 두고 원점에서 왼쪽으로 가로 정렬.
        float t_alpha = CurrentBodyAlpha;
        for (int t_i = 0; t_i < t_icons.Count; t_i++)
        {
            GameObject t_obj = UnityEngine.Object.Instantiate(this.keywordIconPrefab, t_root);
            t_obj.transform.localPosition = this.keywordIconsUseSynergySlot && this.synergyBadgeRoot != null
                ? new Vector3(this.keywordIconStart.x + this.keywordIconStep.x * t_i,
                              this.keywordIconStart.y + this.keywordIconStep.y * t_i, 0f)
                : new Vector3(-t_i * this.iconSpacing, 0f, 0f);
            // prefab = 배경(루트 SpriteRenderer) + 아이콘(자식 SpriteRenderer). 배경 유지, 자식에만 키워드 스프라이트 주입.
            SpriteRenderer t_iconSr = t_obj.transform.childCount > 0
                ? t_obj.transform.GetChild(0).GetComponent<SpriteRenderer>()
                : t_obj.GetComponent<SpriteRenderer>();
            if (t_iconSr != null) t_iconSr.sprite = t_icons[t_i].Icon;

            // 아이콘은 **지금 막 생성**되므로 직전 페이드에 참여하지 못했다 → 프리팹 알파(1) 그대로다.
            // 죽은 카드가 알파 0으로 사라진 자리에 새 카드가 렌더되면, 몸통은 아직 투명한데
            // 아이콘만 불쑥 보인다. 태어나는 순간 카드 몸통 알파에 맞춰 둔다.
            // (이후 페이드는 GetComponentsInChildren 캐시에 자동으로 잡히므로 여기 한 번이면 된다.)
            ApplyAlpha(t_obj, t_alpha);

            this.iconMap[t_icons[t_i].Keyword] = t_obj;
        }
    }

    /// <summary>카드 몸통이 **도달할** 알파. 새로 만든 자식(아이콘/배지)을 여기에 맞춰야 카드와 따로 놀지 않는다.
    ///
    /// 렌더러의 현재 알파가 아니라 목표값을 쓴다: 페이드는 트윈이라 진행 중엔 둘이 다르고,
    /// 그 순간 태어난 자식은 이미 돌고 있는 트윈에 못 낀다 — 현재값으로 맞추면 중간 알파에 굳어버린다
    /// (공격 후 RestoreAllFades와 보드 재렌더가 같은 프레임에 겹쳐 키워드 아이콘만 흐리게 남던 원인).</summary>
    float CurrentBodyAlpha => this.bodyAlphaSource != null ? this.bodyAlphaSource.FadeTarget : 1f;

    static void ApplyAlpha(GameObject _go, float _alpha)
    {
        if (_go == null || _alpha >= 1f) return;   // 1이면 프리팹 기본값 그대로가 맞다(불필요한 순회 생략)

        // 카드 몸통 알파는 각 렌더러의 기준 알파(CardFadeAlpha)와 곱해서 건다 —
        // 반투명이 기본인 배경판을 여기서 불투명하게 덮어쓰지 않도록(CardAnimator의 페이드와 같은 규칙).
        foreach (SpriteRenderer t_sr in _go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            Color t_c = t_sr.color; t_c.a = _alpha * CardFadeAlpha.Of(t_sr); t_sr.color = t_c;
        }
        foreach (TMP_Text t_tmp in _go.GetComponentsInChildren<TMP_Text>(true))
        {
            Color t_c = t_tmp.color; t_c.a = _alpha * CardFadeAlpha.Of(t_tmp); t_tmp.color = t_c;
        }
    }

    /// <summary>장식 자식들의 트윈만 끊는다(파괴는 하지 않는다). Refresh의 정리 구간과 같은 규약 —
    /// SetLink는 CardView GO 기준이라 자식 단독 수명에는 안 걸린다.</summary>
    static void KillChildTweens(Transform _root)
    {
        if (_root == null) return;
        foreach (Transform t_child in _root)
        {
            foreach (SpriteRenderer t_sr in t_child.GetComponentsInChildren<SpriteRenderer>(true))
                t_sr.DOKill();
            foreach (TMP_Text t_tx in t_child.GetComponentsInChildren<TMP_Text>(true))
                t_tx.DOKill();
            t_child.DOKill();   // Pop 펀치 스케일(SetLink는 아이콘 GO 기준이라 살아있을 수 있다)
        }
    }
    #endregion

    #region Keyword frames
    // 프레임 키워드 장식. 기준은 TraitKeywords(아이콘 줄은 여기서 IconRowExcluded만 더 빼는 IconKeywords) —
    // 즉 표식은 프레임엔 뜨고 아이콘 줄엔 안 뜬다. 그 차이의 유일한 선언 지점은 CardVisualRules.IconRowExcluded다.
    // 빈 슬롯/뒷면은 전부 끈다(아이콘 줄과 동일한 정보 은닉).
    void RefreshKeywordFrames(CardInstance _card)
    {
        if (this.keywordFrames == null) return;

        CardKeyword t_keywords = _card != null && _card.isRevealed
            ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;

        foreach (CardView.KeywordFrame t_frame in this.keywordFrames)
        {
            if (t_frame.overlay == null) continue;
            // None 배선은 항상 꺼짐 — HasFlag(None)은 늘 true라 그대로 두면 모든 카드에서 켜진다.
            bool t_on = t_frame.keyword != CardKeyword.None && (t_keywords & t_frame.keyword) != 0;
            t_frame.overlay.SetActive(t_on);
        }
    }
    #endregion

    #region Synergy badges
    // 카드의 synergies 배열(있는 것만, 중복 제외)을 색+텍스트 배지로 세로 정렬 표시(최대 synergyMaxBadges개).
    // 선택·정렬 규칙(활성 우선 → requiredCount 내림차순)은 CardVisualRules 소유 — 아웃게임 타일과 순서가 갈라지지 않게.
    // 활성/티어 판정은 확정 SynergyState.Active 참조 조회(재계산·집계 금지).
    // _synergy는 이 카드가 속한 BattleField.Synergy(BattleFieldView가 Render로 주입). null이면 전부 비활성 취급.
    void RefreshSynergyBadges(CardInstance _card, SynergyState _synergy)
    {
        // 그 자리를 키워드 아이콘이 쓰는 모드면 배지를 아예 만들지 않는다(겹침 방지).
        // 배지가 존재하지 않으므로 FindBadgeAt은 null → 롱프레스는 카드 정보 팝업으로, PopSynergyBadge는 no-op.
        if (this.keywordIconsUseSynergySlot && this.synergyBadgeRoot != null) return;
        if (this.synergyBadgeRoot == null || this.synergyBadgePrefab == null) return;

        // 시너지는 덱 확정이라 전투 중 불변. 같은 카드+같은 SynergyState면 재생성 스킵 →
        // 매 Render(턴 시작 Refresh)마다 배지가 재-Set되어 pop이 반복되는 문제 방지.
        // 배지가 이미 존재할 때만 스킵(없으면 재생성 필요). 첫 등장/리바인드 시에만 rebuild+pop.
        if (_card == this.lastBadgeCard && _synergy == this.lastBadgeState
            && this.synergyBadgeRoot.childCount > 0)
            return;
        this.lastBadgeCard  = _card;
        this.lastBadgeState = _synergy;

        // 기존 배지 정리. 배경 SpriteRenderer/라벨 TMP_Text가 CardAnimator FadeView tween 대상일 수 있어
        // 파괴 전 직접 DOKill(SetLink는 CardView GO 기준이라 자식 단독 파괴 시 안 걸림). 키워드 아이콘과 동일 규약.
        foreach (Transform t_child in this.synergyBadgeRoot)
        {
            foreach (SpriteRenderer t_sr in t_child.GetComponentsInChildren<SpriteRenderer>(true))
                t_sr.DOKill();
            foreach (TMP_Text t_tx in t_child.GetComponentsInChildren<TMP_Text>(true))
                t_tx.DOKill();
            UnityEngine.Object.Destroy(t_child.gameObject);
        }

        // 튜토리얼: 시너지 개념 미도입 구간은 배지 숨김. 판정은 TutorialConfig.SynergyVisible 단독
        // (롱프레스 정보창의 아이콘 줄·설명 목록도 같은 게이트를 쓴다).
        if (!TutorialConfig.SynergyVisible) return;

        // 빈 슬롯·뒷면 카드는 배지 없음(뒷면 적의 종족/직업 정보 노출 방지).
        if (_card == null || _card.data == null || !_card.isRevealed) return;

        // 표시 대상·순서(중복 제외 → 활성 우선 → requiredCount 내림차순 → 상한 적용)는 CardVisualRules 단독.
        // 여기 남는 건 세로 배치와 배지 Set뿐. 활성 판정도 같은 규칙 헬퍼를 재사용해 정렬과 아이콘이 어긋나지 않게 한다.
        List<SynergyData> t_tags = CardVisualRules.CollectSynergyBadges(_card.data.synergies, _synergy, this.synergyMaxBadges);

        float t_alpha = CurrentBodyAlpha;
        for (int t_i = 0; t_i < t_tags.Count; t_i++)
        {
            SynergyBadgeView t_badge = UnityEngine.Object.Instantiate(this.synergyBadgePrefab, this.synergyBadgeRoot);
            t_badge.transform.localPosition = new Vector3(this.synergyBadgeXPos, this.synergyBadgeYStart + this.synergyBadgeYStep * t_i, 0f);
            t_badge.Set(t_tags[t_i], CardVisualRules.IsSynergyActive(_synergy, t_tags[t_i]));

            // 키워드 아이콘과 같은 이유: 배지는 **지금 막 생성**돼 직전 페이드에 참여하지 못했다.
            // 그대로 두면 죽은 카드가 사라진 자리에 몸통 없이 배지만 먼저 보인다.
            // Set() 뒤에 호출해야 한다 — Set이 색을 다시 칠하므로 먼저 맞추면 덮인다.
            ApplyAlpha(t_badge.gameObject, t_alpha);
        }
    }

    // 시너지 효과가 실제 발동한 순간, 이 카드의 해당 시너지 배지를 pop시킨다(순수 연출, 게임상태/RNG 무관).
    // synergyBadgeRoot 자식에는 활성 배지만 존재하므로 Synergy 참조 일치 배지를 찾아 PlayPop. null/미발견이면 no-op.
    public void PopSynergyBadge(SynergyData _synergy)
    {
        if (this.synergyBadgeRoot == null || _synergy == null) return;
        foreach (Transform t_child in this.synergyBadgeRoot)
        {
            SynergyBadgeView t_badge = t_child.GetComponent<SynergyBadgeView>();
            if (t_badge != null && t_badge.Synergy == _synergy)
            {
                t_badge.PlayPop();
                return;
            }
        }
    }
    #endregion

    #region Glow
    public async UniTask PlayPassiveGlow()
    {
        if (this.passiveGlowSystem == null) return;
        this.passiveGlowSystem.Play();
        float t_dur = this.passiveGlowSystem.main.duration;
        await UniTask.Delay((int)(t_dur * 1000), cancellationToken: this.owner.GetCancellationTokenOnDestroy()).SuppressCancellationThrow();
    }

    /// <summary>키워드 글로우 재생. 색·유지시간·프리팹은 전부 KeywordIconConfig(SO)가 소유하고,
    /// 미지정(hold 0 / prefab null)일 때만 전역 기본값(BattleTimingConfig.keywordGlowHold, CardView의 keywordGlowPrefab)으로 폴백한다.
    ///
    /// 키워드마다 유지시간이 다를 수 있으므로 <b>짧은 것부터 순서대로</b> 제거하고, await은 가장 긴 것 기준이다 —
    /// 전부 최댓값까지 살려두면 SO에서 짧게 잡은 글로우가 그 값대로 안 사라진다.</summary>
    public async UniTask PlayKeywordGlow(CardKeyword _kw)
    {
        if (_kw == CardKeyword.None) return;

        var t_spawned = new List<(GameObject Go, float Hold)>();
        foreach (CardKeyword t_flag in System.Enum.GetValues(typeof(CardKeyword)))
        {
            if (t_flag == CardKeyword.None) continue;
            if (!_kw.HasFlag(t_flag)) continue;
            if (!this.iconMap.TryGetValue(t_flag, out GameObject t_icon)) continue;

            KeywordIconConfig.GlowSpec t_spec = this.keywordIconConfig != null
                ? this.keywordIconConfig.GetGlow(t_flag)
                : KeywordIconConfig.GlowSpec.Default;

            GameObject t_prefab = t_spec.PrefabOverride != null ? t_spec.PrefabOverride : this.keywordGlowPrefab;
            if (t_prefab == null) continue;   // 전용도 기본도 없으면 이 키워드는 글로우 없음

            // SO 값은 raw 초 → 배속은 여기서 한 번만 먹인다(BattleTimingConfig.Scaled가 유일 출구).
            float t_hold = t_spec.HoldOverride > 0f
                ? GameTiming.Battle.Scaled(t_spec.HoldOverride)
                : GameTiming.Battle.KeywordGlowHold;

            GameObject t_glow = UnityEngine.Object.Instantiate(t_prefab, t_icon.transform.position, Quaternion.identity);

            // 크기는 프리팹을 건드리지 않고 SO 배율로 키운다 — 프리팹을 키우면 이걸 재사용하는
            // 다른 연출(시너지/힐 등)까지 같이 커진다.
            float t_scale = t_spec.ScaleOverride > 0f
                ? t_spec.ScaleOverride
                : (this.keywordIconConfig != null ? this.keywordIconConfig.DefaultGlowScale : 1f);
            if (!Mathf.Approximately(t_scale, 1f)) t_glow.transform.localScale *= t_scale;

            PopKeywordIcon(t_icon);   // 글로우 스폰과 같은 프레임 — 둘이 한 타격으로 읽히게

            var t_ps = t_glow.GetComponent<ParticleSystem>();
            if (t_ps != null)
            {
                var t_col  = t_ps.colorOverLifetime;
                t_col.enabled = true;
                var t_grad = new Gradient();
                t_grad.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(t_spec.Start, 0f), new GradientColorKey(t_spec.End, 1f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
                );
                t_col.color = new ParticleSystem.MinMaxGradient(t_grad);
                t_ps.Play();
            }
            t_spawned.Add((t_glow, t_hold));
        }

        if (t_spawned.Count == 0) return;

        t_spawned.Sort((_a, _b) => _a.Hold.CompareTo(_b.Hold));

        try
        {
            CancellationToken t_ct = this.owner.GetCancellationTokenOnDestroy();
            float t_elapsed = 0f;
            foreach ((GameObject t_go, float t_hold) in t_spawned)
            {
                int t_wait = (int)((t_hold - t_elapsed) * 1000);
                if (t_wait > 0)
                {
                    await UniTask.Delay(t_wait, cancellationToken: t_ct);
                    t_elapsed = t_hold;
                }
                if (t_go != null) UnityEngine.Object.Destroy(t_go);
            }
            return;   // 정상 종료 — 아래 일괄 정리는 취소된 경우만.
        }
        catch (OperationCanceledException) { }

        // 취소(씬/오브젝트 파괴)로 중간에 끊긴 경우 남은 글로우를 흘리지 않는다.
        foreach ((GameObject t_go, float _) in t_spawned)
            if (t_go != null) UnityEngine.Object.Destroy(t_go);
    }

    /// <summary>키워드 아이콘 튀기기. 글로우 스폰과 같은 프레임에 불러서 둘이 한 타격으로 읽히게 한다.
    /// 기다리지 않는다 — 글로우 유지시간(hold)이 연출 길이의 단일 기준이라, Pop을 await 하면
    /// SO의 hold 값과 무관하게 대기가 늘어난다.
    ///
    /// 펀치는 <b>현재</b> localScale 기준이라 이전 펀치가 살아 있으면 배율이 곱해져 계속 커진다 →
    /// DOKill(complete: true)로 기준 스케일까지 되돌린 뒤 새로 건다.</summary>
    void PopKeywordIcon(GameObject _icon)
    {
        if (_icon == null || this.keywordIconConfig == null) return;

        float t_pop = this.keywordIconConfig.IconPopScale;
        if (t_pop <= 1f) return;

        Transform t_tr = _icon.transform;
        t_tr.DOKill(complete: true);
        t_tr.DOPunchScale(t_tr.localScale * (t_pop - 1f),
                          GameTiming.Battle.Scaled(this.keywordIconConfig.IconPopDuration),
                          vibrato: 1, elasticity: 0.6f)
            .SetLink(_icon);
    }
    #endregion
}
