using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;

// 아웃게임(uGUI) 카드 한 장의 비주얼 단일 진실원. 도감 칸·덱편집 타일·드래그 고스트·팩 카드가 공유한다.
//
// 인게임 CardView는 월드스페이스 SpriteRenderer라 프리팹을 복사할 수 없다 → 렌더러만 Image/TMP_Text로 바꾸고
// "무엇을 어떤 순서로 몇 개까지 보이는가"는 CardVisualRules(인게임과 같은 호출)에 위임한다.
// 규칙을 복제하면 로비와 전투의 카드가 조용히 달라진다.
public class CardVisualView : MonoBehaviour
{
    [SerializeField] Image portrait;
    [SerializeField] TMP_Text nameText;
    [Tooltip("이름 뒤 판(TextBG). 이름과 한 몸이라 늘 같이 켜고 끈다 — 따로 두면 글자만 사라지고 검은 띠가 남는다. 미배선이면 조용히 건너뛴다.")]
    [SerializeField] GameObject nameBackground;
    [SerializeField] GameObject lockOverlay;  // 미소유 시 활성(어두운 오버레이 + 잠김 표시)

    [Header("인게임 미러 요소")]
    [SerializeField] Image      frame;
    [SerializeField] GameObject hpPanel;
    [SerializeField] TMP_Text   hpText;
    [SerializeField] TMP_Text   bonusHpText;
    [Tooltip("HP 아이콘. 새 값이 드러나는 한 박에 숫자와 같은 축으로 부푼다(FlashGrowth). 미배선이면 맥박만 조용히 빠진다.")]
    [SerializeField] Image      hpIcon;
    [Tooltip("새 값이 드러나는 한 박에 Lv·HP 글자가 물드는 색(FlashGrowth). 카드 위 숫자는 프레임 장식에 묻히므로 색이 있어야 눈이 먼저 온다.")]
    [FormerlySerializedAs("hpRollFlashColor")]
    [SerializeField] Color      growthFlashColor = new Color(0.45f, 1f, 0.55f, 1f);
    [Tooltip("그 한 박에 Lv·HP 글자가 부푸는 최대 비율.")]
    [SerializeField] float      growthTextPulse = 0.18f;
    [Tooltip("그 한 박에 HP 아이콘이 부푸는 최대 비율. 아이콘(167.1)이 HpPanel(192.4) 안에 있어야 하므로 0.15를 넘기면 삐져나온다.")]
    [FormerlySerializedAs("hpIconPulse")]
    [SerializeField] float      growthIconPulse = 0.12f;
    [Tooltip("부풀었다 돌아오는 데 걸리는 시간. 섬광이 물러나는 동안 안에서 끝나야 '드러나며 강조된다'로 읽힌다.")]
    [SerializeField] float      growthFlashDuration = 0.45f;
    [Tooltip("성장 성급 표시(카드 위쪽). 미배선이면 조용히 건너뛴다 — 작은 타일은 노드를 두지 않으면 된다.")]
    [SerializeField] TMP_Text   levelText;
    [Tooltip("고정 3칸 성장 별. 프리팹에서 미리 저작하고 런타임에는 채움 상태만 바꾼다.")]
    [SerializeField] Image[]    growthStars;
    [SerializeField] CardKeywordIconView[]  keywordIconSlots;
    [SerializeField] CardSynergyBadgeView[] synergyBadgeSlots;
    [SerializeField] Transform  keywordIconRoot;
    [SerializeField] Transform  synergyBadgeRoot;  // 인게임처럼 그 자리를 키워드가 쓰는 프리팹은 비워 둔다 — 미배선이면 배지를 그리지 않는다.

    // 두 장이 같이 켜지면 겹쳐 그려진다 — 어느 쪽을 켤지는 RefreshKeywordBg 한 곳에서만 정한다.
    [SerializeField] GameObject keywordBg;      // SynergyKewordBG (시너지가 열린 카드 = 키워드 + 시너지 칸)
    [SerializeField] GameObject keywordOnlyBg;  // SynergyKewordBG_kewordOnly (시너지 없음·미해금 = 키워드 칸만)
    [SerializeField] CardKeywordIconView   keywordIconPrefab;
    [SerializeField] CardSynergyBadgeView  synergyBadgePrefab;
    [SerializeField] KeywordIconConfig     keywordIconConfig;

    /// <summary>프레임에 얹는 키워드별 장식 이미지 한 칸.</summary>
    // 이름 매칭이 아니라 참조 배선인 이유는 인게임과 같다: 오브젝트 이름을 바꿔도 조용히 꺼지지 않게.
    [System.Serializable]
    public struct KeywordFrame
    {
        public CardKeyword keyword;
        public GameObject  overlay;
    }
    [SerializeField] KeywordFrame[] keywordFrames;

    [Header("시너지 배지 자리")]
    // 저작 픽셀값을 각 프리팹 rect로 나누면 세 프리팹이 같은 비율로 수렴한다:
    //   151/420 = 243.5/677 = 143.9/400 = 0.3596 · 65/558 = 105.2/900 = 70.2/600 = 0.1166
    //   step -88/558 = -141.8/900 = -94.5/600 = -0.1576
    // 기본값의 출처는 키워드 아이콘과 같다 — CardUIView.prefab에 저작된 배지 슬롯 픽셀
    // (155, 45.9 / 간격 88)을 설계 rect(420x558) 비율로 옮긴 값이다. 슬롯을 옮기면 여기와
    // 슬롯 없는 프리팹(CardElement·PooledCardElement)의 저작값도 같이 고친다.
    [Tooltip("첫 배지(i=0) 자리. 배지 루트 rect 왼쪽아래(0,0)~오른쪽위(1,1) 비율.")]
    [SerializeField] Vector2 synergyBadgeStart = new Vector2(155f / CardDesignWidth, 45.9f / CardDesignHeight);
    [Tooltip("배지 간 간격(루트 rect 비율. 아래로 쌓기라 y는 음수).")]
    [SerializeField] Vector2 synergyBadgeStep  = new Vector2(0f, -88f / CardDesignHeight);

    [Header("표시 옵션")]
    // 작은 타일에서 요소를 끄기 위한 프리팹별 스위치. 호출부에 표시 분기를 만들지 않으려고 프리팹이 결정한다.
    [SerializeField] bool showName      = true;
    [SerializeField] bool showHp        = true;
    [SerializeField] bool showLevel     = true;
    [SerializeField] bool showKeywords  = true;
    [SerializeField] bool showSynergies = true;
    // 해금 전 키워드까지 띄우는 화면(카드 정보창)만 true. 전투 인스턴스 바인딩에는 적용되지 않는다 —
    // 적 카드에 실제로 못 쓰는 키워드를 띄우면 오정보다.
    [SerializeField] bool showLockedKeywords;
    // 아이콘 누름이 다른 뜻인 화면(카드 상세 열기·드래그 시작)에서 설명 팝업이 끼어들지 않게 하는 스위치.
    [SerializeField] bool keywordExplainOnPress;
    // 기본값은 인게임과 같은 코드 상수 하나에서 온다(각자 3을 적어두면 한쪽만 바뀌어도 조용히 갈라진다).
    [SerializeField] int  synergyMaxBadges = CardVisualRules.MaxSynergyBadges;

    // 전투 인스턴스로 바인딩됐다면 그쪽이 값의 진실원이다. 카드 ID로 바인딩하면 null로 돌아가
    // 아웃게임(내 성장) 기준을 탄다 — 적 카드에 내 강화를 얹지 않기 위함.
    CardInstance m_instance;

    // 프레임과 아트만 남기는 런타임 마스크. 프리팹 스위치(show*)를 끄지 않고 그 위에 얹는다 —
    // 직접 끄면 껐다 켤 때 그 타일이 원래 무엇을 보이던 타일이었는지를 잃는다.
    bool m_artOnly;

    // 소비 지점이 show* 와 m_artOnly 를 각자 곱하면 한 군데 빠뜨렸을 때 그 요소만 살아남는다 → 곱은 여기서만.
    bool ShowName      => this.showName      && !this.m_artOnly;
    bool ShowHp        => this.showHp        && !this.m_artOnly;
    bool ShowLevel     => this.showLevel     && !this.m_artOnly;
    bool ShowKeywords  => this.showKeywords  && !this.m_artOnly;
    bool ShowSynergies => this.showSynergies && !this.m_artOnly;

    /// <summary>프레임·아트만 남기고 카드 위 정보(이름·HP·레벨·키워드 아이콘·프레임 장식·시너지 배지·아이콘 줄 배경판)를 전부 가린다.</summary>
    // 값만 세우고 다시 그리지는 않는다 — 호출부가 이걸 세운 뒤 Bind를 다시 태워야 반영된다.
    public void SetArtOnly(bool _on) => this.m_artOnly = _on;

    // ── 인게임 좌표를 uGUI로 옮기는 환산값 ──────────────────────────────────
    //
    // 카드 내부(Background)는 인게임과 같은 비율의 고정 크기 rect다(420x558). 칸 크기 차이는 UniformFitContent가
    // 배율로 흡수하므로 정적 요소는 프리팹 앵커에 박아두고, 코드가 계산할 것은 런타임 생성물의 자리뿐이다.

    /// <summary>카드 설계 rect. 카드 프리팹(CardUIView)의 Contents가 이 크기로 고정이고,
    /// 아이콘 슬롯 좌표도 이 안의 픽셀로 저작돼 있다.</summary>
    const float CardDesignWidth  = 420f;
    const float CardDesignHeight = 558f;

    // 아이콘 자리의 진실원은 **CardUIView.prefab에 저작된 슬롯**이다 — 덱편집·도감·상세창은 그 슬롯
    // 경로(keywordIconSlots)를 타고, 슬롯이 없는 프리팹(팝업 CardElement 등)만 아래 값으로 만들어 놓는다.
    // 그래서 두 경로가 같은 자리를 내야 한다: 값은 저작 슬롯 픽셀(87.2, 71.3 / 간격 73.85 / 크기 114.301)을
    // 설계 rect 비율로 옮긴 사본이다. 슬롯을 옮기면 여기도 같이 고친다.
    //
    // 예전엔 인게임 CardView의 월드 좌표(-0.65,-1.14)를 카드 월드 크기로 나눠 썼는데, 그 값이 저작 슬롯과
    // 갈라져 있어 슬롯 없는 프리팹에서만 아이콘 줄이 8~10px 밀려 보였다.
    const float KeywordIconStartX = 87.2f   / CardDesignWidth;
    const float KeywordIconStartY = 71.3f   / CardDesignHeight;
    const float KeywordIconStepX  = 73.85f  / CardDesignWidth;
    const float KeywordIconSizePx = 114.301f;

    Tween m_growthFlash;

    // 물든 중간값이나 부푼 중간 배율을 기준으로 잡으면 강조할 때마다 색과 크기가 밀린다 → 1회만, 같은 시점에 캡처한다.
    Color   m_hpBaseColor;
    Color   m_levelBaseColor;
    Vector3 m_hpTextBaseScale;
    Vector3 m_levelBaseScale;
    Vector3 m_hpIconBaseScale;
    bool    m_hpBaseCaptured;

    /// <summary>카드 ID·소유여부로 타일을 바인딩한다. _cardId가 0 이하면 빈칸으로 숨긴다.</summary>
    // _mine=false는 "성장 없음"이 아니라 "상대 기준"이다 — 내 강화분을 얹지 않되 상대가 서 있는 레벨로 그린다.
    // 둘을 같게 두면 매치 화면에서 상대가 실제보다 약해 보인다(유일한 false 호출부가 MatchDeckPanelView.enemySlots).
    public void Bind(int _cardId, bool _owned, bool _mine = true)
    {
        this.m_instance = null;   // 카드 ID 바인딩 = 아웃게임(내 성장) 기준으로 되돌린다
        BindInternal(_cardId, _owned, _mine);
    }

    /// <summary>전투 인스턴스로 바인딩한다(전투 덱 목록·카드 정보창).</summary>
    // 적 카드에 내 강화·진화를 얹지 않기 위해 값의 출처는 인스턴스가 이긴다.
    public void Bind(CardInstance _instance)
    {
        this.m_instance = _instance;
        BindInternal(_instance?.cardId ?? 0, _owned: true, _mine: true);
    }

    void BindInternal(int _card, bool _owned, bool _mine)
    {
        if (_card <= 0)
        {
            gameObject.SetActive(false);
            return;
        }
        gameObject.SetActive(true);

        // 앞 카드의 강조(물든 색·부푼 배율)가 이 카드 위에 남게 두지 않는다.
        RestoreGrowthFlash();

        RefreshArt(_card, _mine);

        // 프레임은 카드별로 바뀌지 않는다. 스프라이트 미배선 시 흰 사각형이 뜨는 것만 막는다.
        if (this.frame != null) this.frame.enabled = this.frame.sprite != null;

        {
            bool t_showName = _owned && this.ShowName;
            if (this.nameBackground != null) this.nameBackground.SetActive(t_showName);
            if (this.nameText != null)
            {
                this.nameText.gameObject.SetActive(t_showName);
                if (t_showName) this.nameText.text = CardCatalog.RequireSpec(_card).DisplayName;
            }
        }

        // 미소유는 실루엣만 노출한다 → 이름뿐 아니라 HP/키워드/시너지 같은 "정보"도 전부 숨긴다.
        SetHpDisplay(_card, _owned && this.ShowHp, _mine);
        SetLevelDisplay(_card, _owned && this.ShowLevel && this.m_instance == null, _mine);
        RefreshKeywordIcons(_card, _owned && this.ShowKeywords);
        RefreshKeywordFrames(_card, _owned && this.ShowKeywords);
        RefreshSynergyBadges(_card, _owned && this.ShowSynergies, _mine);
        RefreshKeywordBg(_card, _owned && this.ShowSynergies, _mine);

        if (this.lockOverlay != null) this.lockOverlay.SetActive(!_owned);
    }

    /// <summary>강화로 바뀌는 값(최대 체력·레벨)만 다시 그린다. 인자 의미는 <see cref="Bind"/>와 같다.</summary>
    // Bind를 통째로 부르면 바뀌지도 않은 아이콘·배지가 매번 Destroy + Instantiate 된다.
    // 카드·소유여부를 캐싱하지 않고 인자로 받는 이유는 바인딩 상태의 진실원을 둘로 만들지 않기 위함이다.
    public void RefreshHp(int _card, bool _owned, bool _mine = true)
    {
        if (_card <= 0) return;

        SetHpDisplay(_card, _owned && this.ShowHp, _mine);
        SetLevelDisplay(_card, _owned && this.ShowLevel, _mine);
    }

    /// <summary>현재 표시 주체의 레벨에 맞는 진화 아트만 다시 그린다.</summary>
    public void RefreshArt(int _card, bool _mine = true)
    {
        if (_card <= 0 || this.portrait == null) return;

        // 인스턴스가 있으면 진화 단계도 그 인스턴스의 값이다 — 적 카드에 내 진화 단계를 얹지 않는다.
        Sprite t_art = this.m_instance != null
            ? CardVisualRules.PickBattleArt(this.m_instance)
            : CardVisualRules.PickCardArt(_card, DeckPower.EvolutionStageOf(_card, _mine));
        this.portrait.sprite  = t_art;
        this.portrait.enabled = t_art != null;
    }

    /// <summary>강화로 키워드가 해금된 프레임에 카드 위 아이콘 줄·프레임 장식·시너지 배지를 다시 그린다.</summary>
    // 아이콘은 Destroy + Instantiate라 호출부는 키워드가 실제로 바뀐 프레임에만 부른다.
    public void RefreshKeywords(int _card, bool _owned)
    {
        if (_card <= 0) return;

        RefreshKeywordIcons(_card, _owned && this.ShowKeywords);
        RefreshKeywordFrames(_card, _owned && this.ShowKeywords);
        // 시너지 해금(1차 진화 레벨)도 이 프레임에 같이 일어난다 — 여기서 안 다시 그리면
        // 강화 화면에서 레벨만 오르고 시너지는 다음 재바인딩까지 안 보인다.
        RefreshSynergyBadges(_card, _owned && this.ShowSynergies, _mine: true);
        RefreshKeywordBg(_card, _owned && this.ShowSynergies, _mine: true);
    }

    /// <summary>지금 꺼져 있지만 _card 기준으로는 켜져야 할 프레임 장식들 = 이번 성장으로 새로 열릴 문양.</summary>
    // 진화 연출이 그것들을 새겨 보이려고 켜지기 전에 묻는다 — 켜고 나면 무엇이 새것인지 알 수 없다.
    public void CollectPendingKeywordFrames(int _card, bool _owned, List<Graphic> _into)
    {
        if (_into == null) return;
        _into.Clear();

        if (_card <= 0 || this.keywordFrames == null) return;

        CardKeyword t_keywords = _owned && this.ShowKeywords ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;

        foreach (KeywordFrame t_frame in this.keywordFrames)
        {
            if (t_frame.overlay == null || t_frame.overlay.activeSelf) continue;
            if (t_frame.keyword == CardKeyword.None || (t_keywords & t_frame.keyword) == 0) continue;

            var t_graphic = t_frame.overlay.GetComponent<Graphic>();
            if (t_graphic != null) _into.Add(t_graphic);
        }
    }

    /// <summary>새 Lv·HP가 드러나는 한 박을 강조한다(강화 결과 공개용). 값은 손대지 않는다 —
    /// 호출부가 이 프레임에 이미 새 값을 찍어 둔 상태로 부른다.</summary>
    // 색과 배율을 한 파형 위에 얹는다: 축을 나누면 따로 놀고, 잘렸을 때 한쪽만 물든 채 굳는다.
    public void FlashGrowth()
    {
        if (this.hpText == null && this.levelText == null) return;

        RestoreGrowthFlash();
        CaptureHpVisual();

        Transform t_link = this.hpText != null ? this.hpText.transform : this.levelText.transform;

        this.m_growthFlash = DOVirtual.Float(0f, 1f, Mathf.Max(0.05f, this.growthFlashDuration),
                                             _p =>
                                             {
                                                 float t_wave = Mathf.Sin(Mathf.Clamp01(_p) * Mathf.PI);

                                                 ApplyFlash(this.hpText,    this.m_hpBaseColor,
                                                            this.m_hpTextBaseScale, t_wave);
                                                 ApplyFlash(this.levelText, this.m_levelBaseColor,
                                                            this.m_levelBaseScale, t_wave);

                                                 // 아이콘은 색을 건드리지 않는다 — 그림이 물들면 강조가 아니라 고장으로 읽힌다.
                                                 if (this.hpIcon != null)
                                                     this.hpIcon.transform.localScale =
                                                         this.m_hpIconBaseScale * (1f + this.growthIconPulse * t_wave);
                                             })
                                       .SetLink(t_link.gameObject)
                                       .OnKill(() =>
                                       {
                                           this.m_growthFlash = null;
                                           RestoreHpVisual();
                                       });
    }

    /// <summary>강조를 걷고 authoring 상태로 못 박는다(멱등). 물든 색·부푼 배율이 다음 화면까지 따라가지 않게.</summary>
    public void RestoreGrowthFlash()
    {
        Tween t_flash      = this.m_growthFlash;
        this.m_growthFlash = null;
        t_flash?.Kill();   // OnKill이 색·배율을 되돌린다.

        RestoreHpVisual();   // 트윈이 이미 죽어 있던 경우(위 Kill이 못 잡는 잔상)까지 여기서 걷는다.
    }

    void ApplyFlash(TMP_Text _text, Color _baseColor, Vector3 _baseScale, float _wave)
    {
        if (_text == null) return;

        _text.color                = Color.Lerp(_baseColor, this.growthFlashColor, _wave);
        _text.transform.localScale = _baseScale * (1f + this.growthTextPulse * _wave);
    }

    void CaptureHpVisual()
    {
        if (this.m_hpBaseCaptured) return;
        if (this.hpText == null && this.levelText == null) return;

        this.m_hpBaseCaptured = true;

        if (this.hpText != null)
        {
            this.m_hpBaseColor      = this.hpText.color;
            this.m_hpTextBaseScale  = this.hpText.transform.localScale;
        }

        if (this.levelText != null)
        {
            this.m_levelBaseColor = this.levelText.color;
            this.m_levelBaseScale = this.levelText.transform.localScale;
        }

        if (this.hpIcon != null) this.m_hpIconBaseScale = this.hpIcon.transform.localScale;
    }

    // 강조가 끝나든 잘리든 여기 한 곳에서 기준 상태로 못 박는다(멱등).
    void RestoreHpVisual()
    {
        if (!this.m_hpBaseCaptured) return;

        if (this.hpText != null)
        {
            this.hpText.color                = this.m_hpBaseColor;
            this.hpText.transform.localScale = this.m_hpTextBaseScale;
        }

        if (this.levelText != null)
        {
            this.levelText.color                = this.m_levelBaseColor;
            this.levelText.transform.localScale = this.m_levelBaseScale;
        }

        if (this.hpIcon != null) this.hpIcon.transform.localScale = this.m_hpIconBaseScale;
    }

    // 상대 덱도 레벨을 띄운다 — 상대가 몇 레벨 카드로 나오는지가 트레이드 판단의 핵심이다.
    // 값의 기준만 갈리고(내 카드=내 진행도, 상대=AI 레벨) 판정은 DeckPower가 소유한다.
    void SetLevelDisplay(int _card, bool _show, bool _mine)
    {
        int t_level = DeckPower.LevelOf(_card, _mine);
        if (this.levelText != null)
        {
            this.levelText.gameObject.SetActive(_show);
            if (_show) this.levelText.text = GrowthStar.Label(t_level);
        }

        if (this.growthStars == null) return;

        int t_star = GrowthStar.FromLevel(t_level);
        for (int t_i = 0; t_i < this.growthStars.Length; t_i++)
        {
            Image t_icon = this.growthStars[t_i];
            if (t_icon == null) continue;

            t_icon.gameObject.SetActive(_show);
            Color t_color = t_icon.color;
            t_color.a = t_i < t_star ? 1f : 0.22f;
            t_icon.color = t_color;
        }
    }

    // 내 카드는 마스터 데이터의 maxHp가 아니라 강화 반영 최대 체력(DeckPower.MaxHpOf)을 그린다 —
    // 직접 읽으면 강화한 카드가 로비에서만 안 오른 것처럼 보인다.
    void SetHpDisplay(int _card, bool _show, bool _mine)
    {
        if (this.hpPanel != null) this.hpPanel.SetActive(_show);

        if (this.hpText != null)
        {
            this.hpText.gameObject.SetActive(_show);
            if (_show)
                this.hpText.text = (this.m_instance != null
                    ? this.m_instance.hp
                    : DeckPower.MaxHpOf(_card, _mine)).ToString();
        }

        if (this.bonusHpText != null)
        {
            int t_bonus = this.m_instance != null ? this.m_instance.bonusHp : 0;
            bool t_hasBonus = _show && t_bonus > 0;
            this.bonusHpText.gameObject.SetActive(t_hasBonus);
            if (t_hasBonus) this.bonusHpText.text = $"+{t_bonus}";
        }
    }

    void RefreshKeywordIcons(int _card, bool _show)
    {
        if (HasWiredSlot(this.keywordIconSlots))
        {
            foreach (CardKeywordIconView t_slot in this.keywordIconSlots)
            {
                if (t_slot == null) continue;
                t_slot.BindExplain(null, null);
                t_slot.SetIcon(null);
                t_slot.gameObject.SetActive(false);
            }

            if (!_show || this.keywordIconConfig == null) return;

            List<CardVisualRules.KeywordIcon> t_entries =
                CardVisualRules.CollectKeywordIcons(KeywordIconSet(_card), this.keywordIconConfig);
            int t_count = Mathf.Min(t_entries.Count, this.keywordIconSlots.Length);
            for (int t_i = 0; t_i < t_count; t_i++)
            {
                CardKeywordIconView t_view = this.keywordIconSlots[t_i];
                if (t_view == null) continue;

                CardVisualRules.KeywordIcon t_entry = t_entries[t_i];
                t_view.gameObject.SetActive(true);
                t_view.SetIcon(t_entry.Icon);
                BindKeywordExplain(t_view, t_entry.Keyword);
            }
            return;
        }

        if (this.keywordIconRoot == null) return;
        ClearChildren(this.keywordIconRoot);

        if (!_show || this.keywordIconPrefab == null || this.keywordIconConfig == null) return;

        int t_index = 0;
        foreach (CardVisualRules.KeywordIcon t_entry in
                 CardVisualRules.CollectKeywordIcons(KeywordIconSet(_card), this.keywordIconConfig))
        {
            CardKeywordIconView t_view = Instantiate(this.keywordIconPrefab, this.keywordIconRoot);
            t_view.SetIcon(t_entry.Icon);
            PlaceKeywordIcon(t_view.transform as RectTransform, t_index++);
            BindKeywordExplain(t_view, t_entry.Keyword);
        }
    }

    // 판 선택 기준은 키워드가 아니라 시너지 하나다 — 넓은 판에는 시너지 배지 자리가 딸려 있어
    // 시너지가 없거나 아직 안 열린 카드에서 켜면 빈 칸이 남는다. 배지를 그리는 SynergyBadges와 같은 호출이다.
    void RefreshKeywordBg(int _card, bool _show, bool _mine)
    {
        // 프레임·아트만 남기는 화면에는 판도 없다. 미소유 은닉(_show=false)은 반대로 좁은 판을 켜 두는 것이
        // 맞아서(도감 잠김 칸) 같은 축으로 합치지 않는다.
        if (this.m_artOnly)
        {
            if (this.keywordBg     != null) this.keywordBg.SetActive(false);
            if (this.keywordOnlyBg != null) this.keywordOnlyBg.SetActive(false);
            return;
        }

        bool t_synergy = _show && SynergyBadges(_card, _mine).Count > 0;

        if (this.keywordBg     != null) this.keywordBg.SetActive(t_synergy);
        if (this.keywordOnlyBg != null) this.keywordOnlyBg.SetActive(!t_synergy);
    }

    // 인스턴스 경로에는 잠김 표시가 없다: 그 카드가 지금 실제로 가진 것이 곧 정답이다.
    CardKeyword KeywordIconSet(int _card)
    {
        if (this.m_instance != null)
            return this.showLockedKeywords
                ? CardVisualRules.InfoKeywords(this.m_instance)
                : CardVisualRules.IconKeywords(this.m_instance);

        return this.showLockedKeywords
            ? CardVisualRules.InfoKeywordsWithLocked(_card)
            : CardVisualRules.IconKeywords(_card);
    }

    // 폴백 아이콘(Keyword.None)은 실제 보유 키워드가 아니라 설명할 것이 없다.
    void BindKeywordExplain(CardKeywordIconView _view, CardKeyword _keyword)
    {
        if (!this.keywordExplainOnPress) return;
        if (_view == null || _keyword == CardKeyword.None || this.keywordIconConfig == null) return;
        if (!this.keywordIconConfig.TryGetEntry(_keyword, out KeywordIconConfig.Entry t_entry)) return;

        var t_rect = _view.transform as RectTransform;
        _view.BindExplain(() => ShowKeywordExplain(t_entry, t_rect), HideKeywordExplain);
    }

    static void ShowKeywordExplain(KeywordIconConfig.Entry _entry, RectTransform _iconRect)
    {
        UIPoolManager.Instance?.AddOrUpdateUI<ExplainPopupUI>(new ExplainPopupData
        {
            icon        = _entry.icon,
            displayName = _entry.displayName,
            explain     = _entry.explain,
            iconRect    = _iconRect,
        });
    }

    static void HideKeywordExplain() => UIPoolManager.Instance?.HideUI<ExplainPopupUI>();

    // LayoutGroup에 맡기지 않고 인게임 좌표를 정규화 앵커로 옮긴다 — LayoutGroup은 간격·크기를 픽셀로 잡아서
    // 카드 셀 크기가 바뀌면(도감 386px vs 팩개봉 930px) 인게임과 비율이 어긋난다.
    static void PlaceKeywordIcon(RectTransform _rect, int _index)
    {
        if (_rect == null) return;

        var t_center = new Vector2(KeywordIconStartX + KeywordIconStepX * _index, KeywordIconStartY);

        // 자리는 비율 한 점, 크기는 픽셀이다. 비율 상자(anchorMin~Max)로 크기까지 잡으면 아이콘 루트의
        // 가로세로 비가 설계 rect와 다른 화면에서 아이콘이 눌린다 — 롱프레스 팝업의 CardElement가 400x600이라
        // 정사각 아이콘이 세로로 늘어나 있었다. 폭 비율로만 키워 정사각을 유지한다.
        RectTransform t_root  = _rect.parent as RectTransform;
        float         t_scale = t_root != null && t_root.rect.width > 0f
            ? t_root.rect.width / CardDesignWidth
            : 1f;
        float t_size = KeywordIconSizePx * t_scale;

        _rect.anchorMin        = t_center;
        _rect.anchorMax        = t_center;
        _rect.pivot            = new Vector2(0.5f, 0.5f);
        _rect.sizeDelta        = new Vector2(t_size, t_size);
        _rect.anchoredPosition = Vector2.zero;
        _rect.localScale       = Vector3.one;
    }

    // 기준은 인게임 CardView.RefreshKeywordFrames와 같은 TraitKeywords(아이콘 줄만 표식을 더 뺀다).
    void RefreshKeywordFrames(int _card, bool _show)
    {
        if (this.keywordFrames == null) return;

        CardKeyword t_keywords = !_show ? CardKeyword.None
                               : this.m_instance != null ? CardVisualRules.TraitKeywords(this.m_instance)
                                                         : CardVisualRules.TraitKeywords(_card);

        foreach (KeywordFrame t_frame in this.keywordFrames)
        {
            if (t_frame.overlay == null) continue;
            // None 배선은 항상 꺼짐 — HasFlag(None)은 늘 true라 그대로 두면 모든 카드에서 켜진다.
            bool t_on = t_frame.keyword != CardKeyword.None && (t_keywords & t_frame.keyword) != 0;
            t_frame.overlay.SetActive(t_on);
        }
    }

    // 배지와 배경판이 같은 이 목록을 본다 — 갈리면 "배지는 없는데 배경만 넓은" 카드가 생긴다.
    // 게이트 둘(튜토리얼 미도입 구간·시너지 해금 레벨)은 인게임 CardDecorView.RefreshSynergyBadges와 같다:
    // 해금 전 카드는 실제로 시너지에 참여하지 않으므로 띄우면 오정보다.
    List<SynergyData> SynergyBadges(int _card, bool _mine)
    {
        if (_card <= 0 || !TutorialConfig.SynergyVisible) return EmptySynergies;

        bool t_open = this.m_instance != null
            ? this.m_instance.synergyEnabled
            : DeckPower.SynergyUnlockedOf(_card, _mine);
        if (!t_open) return EmptySynergies;

        // 아웃게임엔 전투 스냅샷(SynergyState)이 없어 활성 판정의 진실원이 없다 → null을 넘긴다.
        // 활성은 전부 false가 되지만 requiredCount 내림차순 정렬은 그대로 성립해 배지 세로 순서가 전투와 일치한다.
        return CardVisualRules.CollectSynergyBadges(CardCatalog.RequireSynergies(_card), null, this.synergyMaxBadges);
    }

    static readonly List<SynergyData> EmptySynergies = new List<SynergyData>();

    void RefreshSynergyBadges(int _card, bool _show, bool _mine)
    {
        if (HasWiredSlot(this.synergyBadgeSlots))
        {
            foreach (CardSynergyBadgeView t_slot in this.synergyBadgeSlots)
                if (t_slot != null) t_slot.Set(null, _active: false);

            if (!_show) return;

            List<SynergyData> t_slotTags = SynergyBadges(_card, _mine);
            int t_count = Mathf.Min(t_slotTags.Count, this.synergyBadgeSlots.Length);
            for (int t_i = 0; t_i < t_count; t_i++)
            {
                CardSynergyBadgeView t_badge = this.synergyBadgeSlots[t_i];
                if (t_badge != null) t_badge.Set(t_slotTags[t_i], _active: true);
            }
            return;
        }

        if (this.synergyBadgeRoot == null) return;
        ClearChildren(this.synergyBadgeRoot);

        if (!_show || this.synergyBadgePrefab == null) return;

        List<SynergyData> t_tags = SynergyBadges(_card, _mine);

        for (int t_i = 0; t_i < t_tags.Count; t_i++)
        {
            CardSynergyBadgeView t_badge = Instantiate(this.synergyBadgePrefab, this.synergyBadgeRoot);
            PlaceSynergyBadge(t_badge.transform as RectTransform, t_i);
            // 도감·덱편집은 "이 카드가 가진 시너지" 소개가 목적이라, 전투 스냅샷이 없다는 이유로
            // 전부 흐린 inactiveIcon을 보여줄 이유가 없다. 정렬만 인게임 규칙을 따른다.
            t_badge.Set(t_tags[t_i], _active: true);
        }
    }

    static bool HasWiredSlot<T>(T[] _slots) where T : Component
    {
        if (_slots == null) return false;
        foreach (T t_slot in _slots)
            if (t_slot != null) return true;
        return false;
    }

    // 앵커 한 점을 비율에 찍고 오프셋은 0으로 둔다 — 픽셀로 두면 카드 rect 크기가 바뀌는 화면
    // (셀에 stretch되는 덱편집 타일·팩 카드)에서 배지만 카드를 따라가지 못하고 자리가 밀린다.
    // 크기를 건드리지 않는 이유는 배지 프리팹이 authoring 크기를 들고 있기 때문이다.
    void PlaceSynergyBadge(RectTransform _rect, int _index)
    {
        if (_rect == null) return;

        Vector2 t_anchor = this.synergyBadgeStart + this.synergyBadgeStep * _index;

        _rect.anchorMin        = t_anchor;
        _rect.anchorMax        = t_anchor;
        _rect.anchoredPosition = Vector2.zero;
        _rect.localScale       = Vector3.one;
    }

    // 인게임은 파괴 전 DOKill로 tween을 정리하지만 아웃게임 타일은 자식에 걸린 tween 자체가 없다 → 불필요.
    static void ClearChildren(Transform _root)
    {
        for (int t_i = _root.childCount - 1; t_i >= 0; t_i--)
            Destroy(_root.GetChild(t_i).gameObject);
    }
}
