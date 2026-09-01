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
    readonly Vector2          keywordIconStart;   // 첫 아이콘 좌표(keywordIconRoot 기준)
    readonly Vector2          keywordIconStep;    // 그 다음 아이콘마다 더할 간격
    readonly GameObject       keywordBg;          // 시너지 칸이 있는 넓은 배경판(활성 시너지 배지가 있을 때만)
    readonly GameObject       keywordOnlyBg;      // 시너지 칸이 없는 좁은 배경판(그 외 전부)
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

    // 저작된 첫 아이콘의 자리. **인스펙터에서 옮긴 위치가 곧 첫 칸**이라, 코드가 keywordIconStart로 덮지 않는다
    // (덮으면 프리팹에서 아무리 옮겨도 실행하는 순간 다른 자리로 튄다). 저작 슬롯이 없을 때만 keywordIconStart를 쓴다.
    readonly Vector3 iconSlotBase;
    readonly bool    hasIconSlotBase;

    CardInstance lastBadgeCard;
    SynergyState lastBadgeState;
    #endregion

    public CardDecorView(
        CardView                _owner,
        CardAnimator            _bodyAlphaSource,
        Transform               _keywordIconRoot,
        GameObject              _keywordIconPrefab,
        KeywordIconConfig       _keywordIconConfig,
        Vector2                 _keywordIconStart,
        Vector2                 _keywordIconStep,
        GameObject              _keywordBg,
        GameObject              _keywordOnlyBg,
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
        this.keywordIconStart           = _keywordIconStart;
        this.keywordIconStep            = _keywordIconStep;
        this.keywordBg                  = _keywordBg;
        this.keywordOnlyBg              = _keywordOnlyBg;
        this.keywordFrames              = _keywordFrames;
        this.synergyBadgeRoot           = _synergyBadgeRoot;
        this.synergyBadgePrefab         = _synergyBadgePrefab;
        this.synergyBadgeXPos           = _synergyBadgeXPos;
        this.synergyBadgeYStart         = _synergyBadgeYStart;
        this.synergyBadgeYStep          = _synergyBadgeYStep;
        this.synergyMaxBadges           = _synergyMaxBadges;
        this.keywordGlowPrefab          = _keywordGlowPrefab;
        this.passiveGlowSystem          = _passiveGlowSystem;

        // 첫 Refresh가 자리를 덮기 전에 저작값을 잡아 둔다 — 여기서 놓치면 되돌릴 원본이 없다.
        if (_keywordIconRoot != null && _keywordIconRoot.childCount > 0)
        {
            this.iconSlotBase    = _keywordIconRoot.GetChild(0).localPosition;
            this.hasIconSlotBase = true;
        }
    }

    /// <summary>이 카드에 마지막으로 그려진 확정 시너지 스냅샷(없으면 null). 보유 장수 조회용 — 재계산 금지.
    /// 롱프레스 시너지 팝업(CardInputController)이 CardView.LastBadgeState 셰임을 거쳐 읽는다.</summary>
    public SynergyState LastBadgeState => this.lastBadgeState;

    /// <summary>장식 전체 갱신. CardView.Render가 카드 본체를 그린 **뒤** 한 번만 부른다 —
    /// 순서(아이콘 → 프레임 → 배지)는 그대로 유지한다.
    /// 빈 슬롯/뒷면 은닉 판정은 각 항목이 스스로 한다(정보 은닉 규칙의 단일 지점).</summary>
    public void Refresh(CardInstance _card, SynergyState _synergy)
    {
        // 배경판 선택과 배지 생성이 **같은 목록**을 보게 여기서 한 번만 뽑아 둘에 나눠 준다.
        // 따로 계산하면 "배지는 없는데 시너지 칸이 있는 판" 같은 어긋남이 생긴다.
        List<SynergyData> t_badges = CollectVisibleSynergyBadges(_card, _synergy);

        RefreshKeywordIcons(_card);
        RefreshKeywordBg(t_badges.Count > 0);
        RefreshKeywordFrames(_card);
        RefreshSynergyBadges(_card, _synergy, t_badges);
    }

    /// <summary>소유자(CardView) 파괴 시 정리. 장식 자식들의 트윈을 끊고 참조를 놓는다 —
    /// 아이콘/배지는 CardAnimator FadeView·Pop 트윈 대상이라 파괴 전 DOKill이 규약이다(Refresh와 동일).</summary>
    public void Cleanup()
    {
        KillChildTweens(this.keywordIconRoot);
        if (this.synergyBadgeRoot != this.keywordIconRoot) KillChildTweens(this.synergyBadgeRoot);
        this.iconMap.Clear();
        this.lastBadgeCard  = null;
        this.lastBadgeState = null;
    }

    #region Keyword icons
    // 아이콘 줄에는 캐릭터 고유 특성만 그린다. 일회용/디버프(무적·추가체력·전투 중 걸린 표식)는
    // 아예 표시하지 않는다 — 무엇을 띄울지 판정은 CardVisualRules 단독(아웃게임과 같은 호출).
    /// <summary>아이콘 줄 배경판 선택. 넓은 판(Card_Icon_Frame)은 시너지 칸이 있는 판이라
    /// **실제로 그려질 활성 시너지 배지가 있을 때만** 쓴다. 시너지가 없거나(미충족 · 미해금 ·
    /// 튜토리얼 은닉 · 뒷면/빈 슬롯) 배지를 못 그리면 시너지 칸이 없는 좁은 판(_kewordOnly)을 쓴다.
    ///
    /// 판정 기준은 배지 생성과 **같은 목록**(<see cref="CollectVisibleSynergyBadges"/>) 하나다 —
    /// 기준이 갈리면 배지는 없는데 시너지 칸만 빈 채로 남는 카드가 생긴다(이전 동작: 키워드 유무로 갈랐다).
    /// 두 판이 동시에 켜지면 겹쳐 그려지므로 언제나 정확히 한 장만 켠다.</summary>
    void RefreshKeywordBg(bool _hasSynergyBadge)
    {
        if (this.keywordBg     != null) this.keywordBg.SetActive(_hasSynergyBadge);
        if (this.keywordOnlyBg != null) this.keywordOnlyBg.SetActive(!_hasSynergyBadge);
    }

    /// <summary>아이콘 줄 갱신. <b>keywordIconRoot의 자식(저작된 아이콘)을 켜고 끄며 스프라이트만 갈아끼운다</b> —
    /// 매 Refresh마다 파괴·생성하지 않는다. 예전 방식은 프리팹에 저작해 둔 아이콘까지 첫 Refresh에 지워 버려
    /// 인스펙터에서 손본 것이 화면에 남지 않았고, 카드 한 장이 턴마다 오브젝트를 새로 만들었다.
    ///
    /// 저작 슬롯보다 띄울 아이콘이 많을 때만 프리팹으로 한 번 늘리고, 늘린 것도 다음부터는 재사용한다.</summary>
    void RefreshKeywordIcons(CardInstance _card)
    {
        Transform t_root = this.keywordIconRoot;
        if (t_root == null || this.keywordIconConfig == null) return;

        this.iconMap.Clear();

        // 뒷면/빈 슬롯이면 아무것도 노출하지 않는다(정보 은닉) — 슬롯은 지우지 않고 끄기만 한다.
        // None/아이콘 미등록은 규칙 쪽에서 걸러져 빈 리스트가 온다.
        List<CardVisualRules.KeywordIcon> t_icons =
            _card == null || !_card.isRevealed
                ? null
                : CardVisualRules.CollectKeywordIcons(CardVisualRules.IconKeywords(_card), this.keywordIconConfig);

        int t_count = t_icons != null ? t_icons.Count : 0;

        // 배치는 한 가지. keywordIconRoot(= 배경판의 큰 칸) 기준으로 keywordIconStart에서 시작해
        // keywordIconStep만큼 밀며 나열한다. 시너지 배지 자리와는 서로 독립이다.
        float t_alpha = CurrentBodyAlpha;
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            GameObject t_obj = SlotAt(t_root, t_i);
            if (t_obj == null) break;   // 저작 슬롯도 없고 프리팹도 미배선 → 더 띄울 자리가 없다

            // 기준점은 저작된 첫 슬롯이다(없을 때만 keywordIconStart). 간격만 코드가 준다.
            Vector3 t_base = this.hasIconSlotBase
                ? this.iconSlotBase
                : new Vector3(this.keywordIconStart.x, this.keywordIconStart.y, 0f);
            t_obj.transform.localPosition = new Vector3(
                t_base.x + this.keywordIconStep.x * t_i,
                t_base.y + this.keywordIconStep.y * t_i, t_base.z);

            // prefab = 배경(루트 SpriteRenderer) + 아이콘(자식 SpriteRenderer). 배경 유지, 자식에만 키워드 스프라이트 주입.
            SpriteRenderer t_iconSr = t_obj.transform.childCount > 0
                ? t_obj.transform.GetChild(0).GetComponent<SpriteRenderer>()
                : t_obj.GetComponent<SpriteRenderer>();
            if (t_iconSr != null) t_iconSr.sprite = t_icons[t_i].Icon;

            if (!t_obj.activeSelf) t_obj.SetActive(true);

            // 재사용 슬롯은 직전 카드의 페이드 값을 그대로 들고 있다(알파 0으로 죽은 카드 자리일 수 있다) →
            // 생성 시점만 맞추던 예전과 달리 **매번** 몸통 알파로 되돌린다. 트윈이 물려 있으면 먼저 끊는다.
            KillTweens(t_obj);
            ForceAlpha(t_obj, t_alpha);

            this.iconMap[t_icons[t_i].Keyword] = t_obj;
        }

        // 남는 슬롯은 끈다 — 저작된 아이콘을 파괴하지 않는다(꺼도 CardAnimator의 렌더러 캐시에는 남는다).
        for (int t_i = t_count; t_i < t_root.childCount; t_i++)
        {
            GameObject t_slot = t_root.GetChild(t_i).gameObject;
            KillTweens(t_slot);
            if (t_slot.activeSelf) t_slot.SetActive(false);
        }
    }

    /// <summary>_index번째 아이콘 슬롯. 저작 자식이 모자랄 때만 늘린다(늘린 것도 이후 재사용된다).
    ///
    /// 늘릴 때는 <b>프리팹이 아니라 저작된 첫 슬롯을 복제한다</b> — 프리팹 원본과 저작본은 크기·정렬 같은
    /// 오버라이드가 다를 수 있어(예: SpriteRenderer size 0.5 vs 0.65) 프리팹으로 늘리면 둘째 칸만 다르게 보인다.</summary>
    GameObject SlotAt(Transform _root, int _index)
    {
        if (_index < _root.childCount) return _root.GetChild(_index).gameObject;

        if (_root.childCount > 0)
            return UnityEngine.Object.Instantiate(_root.GetChild(0).gameObject, _root);

        if (this.keywordIconPrefab == null) return null;

        return UnityEngine.Object.Instantiate(this.keywordIconPrefab, _root);
    }

    // 슬롯 재사용 전 정리. 파괴하지 않으므로 SetLink가 아니라 여기서 직접 끊어야 옛 페이드가 새 카드에 남지 않는다.
    static void KillTweens(GameObject _go)
    {
        if (_go == null) return;
        foreach (SpriteRenderer t_sr in _go.GetComponentsInChildren<SpriteRenderer>(true)) t_sr.DOKill();
        foreach (TMP_Text t_tmp in _go.GetComponentsInChildren<TMP_Text>(true)) t_tmp.DOKill();
    }

    // ApplyAlpha와 같은 규칙이되 알파 1도 실제로 쓴다 — 재사용 슬롯은 "기본값 그대로"가 보장되지 않는다.
    static void ForceAlpha(GameObject _go, float _alpha)
    {
        if (_go == null) return;

        foreach (SpriteRenderer t_sr in _go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            Color t_c = t_sr.color; t_c.a = _alpha * CardFadeAlpha.Of(t_sr); t_sr.color = t_c;
        }
        foreach (TMP_Text t_tmp in _go.GetComponentsInChildren<TMP_Text>(true))
        {
            Color t_c = t_tmp.color; t_c.a = _alpha * CardFadeAlpha.Of(t_tmp); t_tmp.color = t_c;
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
    /// <summary>이 카드 위에 **실제로 그려질** 시너지 배지 목록(없으면 빈 리스트). 배지 생성과
    /// 배경판 선택이 공유하는 단일 판정 지점이다.
    ///
    /// 게이트 순서: 튜토리얼 은닉 → 빈 슬롯/뒷면(정보 은닉) → 시너지 해금(1차 진화) →
    /// 표시 대상·순서(CardVisualRules 단독: 중복 제외 → 활성 우선 → requiredCount 내림차순 → 상한) →
    /// **지금 켜진 것만** 남기기. 비활성 배지는 카드 위에서 켜진 것과 구분이 어렵고 자리만 차지한다.
    /// 활성 판정은 확정 SynergyState 조회다(재계산·집계 금지).
    ///
    /// 배선(root/prefab)이 비어 있으면 배지를 못 그리므로 여기서도 "없음"으로 친다 —
    /// 배경판이 못 그릴 배지를 기다리며 시너지 칸을 열어두지 않게.</summary>
    List<SynergyData> CollectVisibleSynergyBadges(CardInstance _card, SynergyState _synergy)
    {
        if (this.synergyBadgeRoot == null || this.synergyBadgePrefab == null) return new List<SynergyData>();
        if (!TutorialConfig.SynergyVisible)                                   return new List<SynergyData>();
        if (_card == null || !CardCatalog.Contains(_card.cardId) || !_card.isRevealed) return new List<SynergyData>();
        if (!_card.synergyEnabled)                                            return new List<SynergyData>();

        List<SynergyData> t_tags = CardVisualRules.CollectSynergyBadges(CardCatalog.RequireSynergies(_card.cardId), _synergy, this.synergyMaxBadges);
        t_tags.RemoveAll(_tag => !CardVisualRules.IsSynergyActive(_synergy, _tag));
        return t_tags;
    }

    // 카드의 synergies 배열(있는 것만, 중복 제외)을 색+텍스트 배지로 세로 정렬 표시(최대 synergyMaxBadges개).
    // 선택·정렬 규칙(활성 우선 → requiredCount 내림차순)은 CardVisualRules 소유 — 아웃게임 타일과 순서가 갈라지지 않게.
    // 활성/티어 판정은 확정 SynergyState.Active 참조 조회(재계산·집계 금지).
    // _synergy는 이 카드가 속한 BattleField.Synergy(BattleFieldView가 Render로 주입). null이면 전부 비활성 취급.
    void RefreshSynergyBadges(CardInstance _card, SynergyState _synergy, List<SynergyData> _badges)
    {
        // 스냅샷은 **배지를 그리든 안 그리든 항상** 기록한다. 롱프레스 정보창이 활성/비활성을 가르는 근거라
        // 여기서 빠지면 정보창이 상태를 못 받아 전부 활성으로 그린다.
        bool t_sameAsBefore = _card == this.lastBadgeCard && _synergy == this.lastBadgeState;
        this.lastBadgeCard  = _card;
        this.lastBadgeState = _synergy;

        if (this.synergyBadgeRoot == null || this.synergyBadgePrefab == null) return;

        // 시너지는 덱 확정이라 전투 중 불변. 같은 카드+같은 SynergyState면 재생성 스킵 →
        // 매 Render(턴 시작 Refresh)마다 배지가 재-Set되어 pop이 반복되는 문제 방지.
        // 배지가 이미 존재할 때만 스킵(없으면 재생성 필요). 첫 등장/리바인드 시에만 rebuild+pop.
        if (t_sameAsBefore && this.synergyBadgeRoot.childCount > 0) return;

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

        float t_alpha = CurrentBodyAlpha;
        for (int t_i = 0; t_i < _badges.Count; t_i++)
        {
            SynergyBadgeView t_badge = UnityEngine.Object.Instantiate(this.synergyBadgePrefab, this.synergyBadgeRoot);
            t_badge.transform.localPosition = new Vector3(this.synergyBadgeXPos, this.synergyBadgeYStart + this.synergyBadgeYStep * t_i, 0f);
            t_badge.Set(_badges[t_i], _active: true);

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
