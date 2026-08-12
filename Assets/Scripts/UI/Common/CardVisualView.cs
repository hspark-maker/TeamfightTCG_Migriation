using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 아웃게임(uGUI) 카드 한 장의 비주얼 단일 진실원.
// 도감 그리드 타일 / 도감 생산행 타일 / 덱편집 컬렉션 타일 / 덱편집 드래그 고스트가 모두 이 컴포넌트를 공유한다.
//
// 인게임 CardView는 월드스페이스 SpriteRenderer, 로비는 ScreenSpaceOverlay uGUI라 프리팹을 복사할 수 없다.
// 그래서 렌더러만 Image/TMP_Text로 바꾸고, "무엇을 어떤 순서로 몇 개까지 보이는가"라는 규칙은 복제하지 않고
// CardVisualRules(인게임 CardView와 동일 호출)에 위임한다. 규칙이 갈라지면 로비와 전투의 카드가 달라 보인다.
// 배치도 인게임 좌표를 카드 크기 대비 비율로 환산해 그대로 옮긴다(아래 "인게임 좌표 환산값" 참고).
// 여기 남은 것은 소유여부에 따른 은닉 같은 아웃게임 고유 표현뿐이다.
//
// 소유 = 정상 표시, 미소유 = 잠김 오버레이(어둡게+실루엣) + 정보(이름/HP/키워드/시너지) 은닉.
// null 카드(부분행 빈칸)는 타일 자체를 숨긴다.
public class CardVisualView : MonoBehaviour
{
    [SerializeField] Image portrait;          // 카드 아트
    [SerializeField] TMP_Text nameText;       // 카드 이름(미소유 시 숨김)
    [Tooltip("이름 뒤 판(TextBG). 이름과 한 몸이라 늘 같이 켜고 끈다 — 따로 두면 글자만 사라지고 검은 띠가 남는다. 미배선이면 조용히 건너뛴다.")]
    [SerializeField] GameObject nameBackground;
    [SerializeField] GameObject lockOverlay;  // 미소유 시 활성(어두운 오버레이 + 잠김 표시)

    [Header("인게임 미러 요소")]
    [SerializeField] Image      frame;            // 카드 프레임(인게임과 동일 스프라이트). 카드별 데이터가 아니라 프리팹 고정값.
    [SerializeField] GameObject hpPanel;          // HP 표시 묶음(우상단)
    [SerializeField] TMP_Text   hpText;           // 강화 반영 최대 체력(DeckPower.MaxHpOf)
    [SerializeField] TMP_Text   bonusHpText;      // bonusHp > 0 일 때만 "+N"
    [Tooltip("HP 아이콘. 체력이 굴러 오르는 동안 숫자와 같은 축으로 맥박친다(RollHp). 미배선이면 맥박만 조용히 빠진다.")]
    [SerializeField] Image      hpIcon;
    [Tooltip("체력이 굴러 오르는 동안 물드는 색(RollHp). 카드 위 숫자는 프레임 장식에 묻히므로 색이 있어야 눈이 먼저 온다.")]
    [SerializeField] Color      hpRollFlashColor = new Color(0.45f, 1f, 0.55f, 1f);
    [Tooltip("굴리는 동안 HP 아이콘이 부푸는 최대 비율. 아이콘(167.1)이 HpPanel(192.4) 안에 있어야 하므로 0.15를 넘기면 삐져나온다.")]
    [SerializeField] float      hpIconPulse = 0.12f;
    [Tooltip("굴리기가 끝나는 프레임 아이콘을 튀기는 세기. 숫자 펀치(UiPunch 기본값)보다 작아야 시선이 숫자에 남는다.")]
    [SerializeField] float      hpIconPunch = 0.15f;
    [Tooltip("강화 레벨 표시(카드 위쪽). 미배선이면 조용히 건너뛴다 — 작은 타일은 노드를 두지 않으면 된다.")]
    [SerializeField] TMP_Text   levelText;
    [SerializeField] Transform  keywordIconRoot;  // 키워드 아이콘 부모. 카드 rect 전체를 덮는 빈 컨테이너(배치는 코드가 앵커로).
    [SerializeField] Transform  synergyBadgeRoot; // 시너지 배지 부모. 인게임처럼 그 자리를 키워드가 쓰면 미배선(null)이라 배지는 안 그려진다.
    [SerializeField] CardKeywordIconView   keywordIconPrefab;
    [SerializeField] CardSynergyBadgeView  synergyBadgePrefab;
    [SerializeField] KeywordIconConfig     keywordIconConfig;

    // 프레임에 얹는 키워드별 장식 이미지. 인게임 CardView.keywordFrames와 같은 (키워드 → 오브젝트) 배선이며
    // 판정도 같은 CardVisualRules.TraitKeywords를 쓴다 — 기준이 갈리면 전투에선 뜨는 장식이 로비에선 안 뜬다.
    // 이름 매칭이 아니라 참조 배선인 이유도 인게임과 동일: 오브젝트 이름을 바꿔도 조용히 꺼지지 않게.
    [System.Serializable]
    public struct KeywordFrame
    {
        public CardKeyword keyword;
        public GameObject  overlay;
    }
    [SerializeField] KeywordFrame[] keywordFrames;

    [Header("표시 옵션")]
    // 작은 타일에서 요소를 끄기 위한 프리팹별 스위치. 소비자 코드는 Bind만 호출하고
    // "무엇을 보일지"는 프리팹이 결정한다(호출부에 표시 분기를 만들지 않기 위함).
    [SerializeField] bool showName      = true;
    [SerializeField] bool showHp        = true;
    [SerializeField] bool showLevel     = true;
    [SerializeField] bool showKeywords  = true;
    [SerializeField] bool showSynergies = true;
    // 표시 최대 배지 수. 기본값은 인게임과 같은 코드 상수 하나에서 온다(각자 3을 적어두면 한쪽만 바뀌어도 조용히 갈라진다).
    [SerializeField] int  synergyMaxBadges = CardVisualRules.MaxSynergyBadges;

    // 프레임과 아트만 남기는 런타임 마스크(도감 "일러스트만 보기"). 프리팹 스위치(show*)를 끄지 않고 **그 위에 얹는다** —
    // 직접 끄면 껐다 켤 때 프리팹이 원래 무엇을 보이던 타일이었는지(작은 타일은 이름·HP가 애초에 꺼져 있다)를 잃는다.
    bool m_artOnly;

    // show* 는 프리팹의 뜻, m_artOnly는 지금 화면의 뜻이다. 소비 지점이 둘을 각자 곱하면 한 군데 빠뜨렸을 때
    // 그 요소만 살아남으므로, 곱은 여기 한 줄씩에서만 한다.
    bool ShowName      => this.showName      && !this.m_artOnly;
    bool ShowHp        => this.showHp        && !this.m_artOnly;
    bool ShowLevel     => this.showLevel     && !this.m_artOnly;
    bool ShowKeywords  => this.showKeywords  && !this.m_artOnly;
    bool ShowSynergies => this.showSynergies && !this.m_artOnly;

    /// <summary>프레임·아트만 남기고 카드 위 정보(이름·HP·레벨·키워드 아이콘·프레임 장식·시너지 배지)를 전부 가린다.
    /// 값만 세우고 다시 그리지는 않는다 — 아이콘/배지는 Destroy + Instantiate라 갱신 시점을 호출부가 쥐어야 한다
    /// (호출부는 이걸 세운 뒤 <see cref="Bind"/>를 다시 태운다).</summary>
    public void SetArtOnly(bool _on) => this.m_artOnly = _on;

    // ── 인게임 좌표를 uGUI로 옮기는 환산값 ──────────────────────────────────
    //
    // 카드 내부(Background)는 인게임 카드와 같은 비율의 **고정 크기** rect다(420x558). 칸 크기가
    // 화면마다 달라도(도감 255x323, 덱편집 270x360, 팩개봉 1000x1230) 그 차이는 UniformFitContent가
    // 배율 하나로 흡수한다 → 정적인 요소(아트·프레임·프레임 장식·이름·HP)는 프리팹에 픽셀 앵커로 박아두고
    // 폰트 크기도 프리팹 값을 그대로 쓴다. 코드가 계산할 게 남은 건 런타임 생성물(키워드 아이콘)의 자리뿐이다.

    /// <summary>인게임 카드 한 장의 월드 크기 = Frame.png(1024x1361 @PPU100) × CardView Frame localScale 0.233245.
    /// 인게임 프레임 스케일이 바뀌면 여기도 같이 바꿔야 로비 카드가 따라간다.</summary>
    const float IngameCardWidth  = 2.388429f;
    const float IngameCardHeight = 3.174464f;

    // 키워드 아이콘 가로줄. 인게임은 keywordIconsUseSynergySlot=true 경로를 타므로 기준은
    // synergyBadge* 가 아니라 CardView의 keywordIconStart(-0.65,-1.14) / keywordIconStep(0.42,0)이다
    // (CardDecorView.RefreshKeywordIcons). kewordIcon 크기(0.65x0.65)와 함께 위 카드 크기로 나눈 값이
    // 카드 중심 기준 정규화 좌표가 된다. 이 모드에선 인게임이 시너지 배지를 아예 그리지 않는다.
    const float KeywordIconStartX = 0.5f + -0.65f / IngameCardWidth;
    const float KeywordIconStartY = 0.5f + -1.14f / IngameCardHeight;
    const float KeywordIconStepX  =        0.42f / IngameCardWidth;
    const float KeywordIconStepY  =        0f    / IngameCardHeight;
    const float KeywordIconWidth  =        0.65f / IngameCardWidth;
    const float KeywordIconHeight =        0.65f / IngameCardHeight;

    // 굴러 오르는 중인 체력. 도는 동안에는 이쪽이 숫자의 주인이다 — RefreshHp가 최종값을 먼저 찍으면 굴릴 것이 사라진다.
    Tween m_hpRoll;

    // hpText의 authoring 색과 hpIcon의 authoring 배율. 물든 중간값이나 부푼 중간 배율을 기준으로 잡으면
    // 굴릴 때마다 색과 크기가 밀린다 → 둘 다 1회만, 같은 시점에 캡처한다.
    Color   m_hpBaseColor;
    Vector3 m_hpIconBaseScale;
    bool    m_hpBaseCaptured;

    // 카드 데이터·소유여부로 타일을 바인딩. _card가 null이면 빈칸으로 숨긴다.
    // 배선이 null인 필드는 조용히 건너뛴다 — 프리팹마다 일부 노드만 가질 수 있다(고스트/작은 타일).
    //
    // _mine: 이 칸이 내 카드인가. 기본 true인 이유는 호출부 전수가 "내 카드"이기 때문이다
    // (도감 그리드·생산행·상세, 덱편집 슬롯/타일/고스트, 강화 화면, 팩 개봉·획득 연출).
    // 유일한 예외가 매치 화면의 상대 덱 6칸(MatchDeckPanelView.enemySlots).
    // **false는 "성장 없음"이 아니라 "상대 기준"이다** — 내 강화분을 얹지 않되, 상대가 서 있는
    // 레벨(랭크 티어가 정한 AI 레벨)로 체력·레벨을 그린다. 둘을 같게 두면 상대가 실제보다 약해 보인다.
    public void Bind(CardData _card, bool _owned, bool _mine = true)
    {
        if (_card == null)
        {
            gameObject.SetActive(false);
            return;
        }
        gameObject.SetActive(true);

        // 다른 카드를 그리는 참이다 — 남은 굴리기가 이 카드 위에 옛 카드의 숫자를 마저 찍게 두지 않는다.
        KillHpRoll();

        RefreshArt(_card, _mine);

        // 프레임은 카드별로 바뀌지 않는다. 스프라이트 미배선 시 흰 사각형이 뜨는 것만 막는다.
        if (this.frame != null) this.frame.enabled = this.frame.sprite != null;

        {
            // 미소유는 이름을 숨겨 실루엣만 노출.
            bool t_showName = _owned && this.ShowName;
            if (this.nameBackground != null) this.nameBackground.SetActive(t_showName);
            if (this.nameText != null)
            {
                this.nameText.gameObject.SetActive(t_showName);
                if (t_showName) this.nameText.text = _card.displayName;   // 표시명 정본은 displayName(에셋 name 아님)
            }
        }

        // 미소유는 실루엣만 노출하는 게 기존 의도 → 이름뿐 아니라 HP/키워드/시너지 같은 "정보"도 전부 숨긴다.
        SetHpDisplay(_card, _owned && this.ShowHp, _mine);
        SetLevelDisplay(_card, _owned && this.ShowLevel, _mine);
        RefreshKeywordIcons(_card, _owned && this.ShowKeywords);
        RefreshKeywordFrames(_card, _owned && this.ShowKeywords);
        RefreshSynergyBadges(_card, _owned && this.ShowSynergies);

        // 미소유 = 잠김 오버레이 on(아트를 어둡게 덮어 실루엣화).
        if (this.lockOverlay != null) this.lockOverlay.SetActive(!_owned);
    }

    /// <summary>강화로 바뀌는 값(최대 체력)만 다시 그린다. 인자 의미는 <see cref="Bind"/>와 같다.
    ///
    /// 성장 통지처럼 잦은 갱신에서 Bind를 통째로 부르면 바뀌지도 않은 키워드 아이콘·시너지 배지가
    /// 매번 Destroy + Instantiate 된다. 반대로 이걸 안 부르면 체력이 옛 값에 굳는다 —
    /// hpText를 쓰는 곳은 SetHpDisplay 하나뿐이라 호출부가 텍스트를 직접 만지면 진실원이 갈린다.
    ///
    /// 카드·소유여부를 캐싱하지 않고 인자로 받는 이유: 바인딩 상태의 진실원을 호출부와 여기 둘로 만들지 않기 위함.</summary>
    public void RefreshHp(CardData _card, bool _owned, bool _mine = true)
    {
        if (_card == null) return;

        // 굴리는 중이면 숫자의 주인은 그쪽이다 — 여기서 최종값을 먼저 찍으면 카운트업이 사라진다(끝나면 그쪽이 못 박는다).
        if (this.m_hpRoll != null && this.m_hpRoll.IsActive()) return;

        SetHpDisplay(_card, _owned && this.ShowHp, _mine);
        SetLevelDisplay(_card, _owned && this.ShowLevel, _mine);
    }

    /// <summary>현재 표시 주체의 레벨에 맞는 진화 아트만 다시 그린다.</summary>
    public void RefreshArt(CardData _card, bool _mine = true)
    {
        if (_card == null || this.portrait == null) return;

        Sprite t_art = CardVisualRules.PickCardArt(_card, DeckPower.EvolutionStageOf(_card, _mine));
        this.portrait.sprite  = t_art;
        this.portrait.enabled = t_art != null;
    }

    /// <summary>강화로 키워드가 해금된 프레임에 카드 위 아이콘 줄과 프레임 장식을 다시 그린다.
    /// 판정은 <see cref="Bind"/>와 같은 CardVisualRules 호출이라 표시 기준이 갈리지 않는다.
    ///
    /// <see cref="RefreshHp"/>처럼 값만 고칠 수 없는 갱신이다(아이콘은 Destroy + Instantiate) →
    /// 호출부는 **키워드가 실제로 바뀐 프레임에만** 부른다. 성장 통지마다 부르면 매번 다시 짓는다.</summary>
    public void RefreshKeywords(CardData _card, bool _owned)
    {
        if (_card == null) return;

        RefreshKeywordIcons(_card, _owned && this.ShowKeywords);
        RefreshKeywordFrames(_card, _owned && this.ShowKeywords);
    }

    /// <summary>지금 꺼져 있지만 _card 기준으로는 켜져야 할 프레임 장식들 = 이번 성장으로 새로 열릴 문양.
    /// 진화 연출이 그것들을 새겨 보이기 위해 **켜지기 전에** 묻는다(켜고 나면 무엇이 새것인지 알 수 없다).
    ///
    /// 판정은 <see cref="RefreshKeywordFrames"/>와 같은 CardVisualRules 호출 하나다 — 기준이 갈리면
    /// 연출이 새기는 문양과 실제로 켜지는 문양이 어긋난다. 일러스트만 보기 모드면 자연히 빈 목록이다.</summary>
    public void CollectPendingKeywordFrames(CardData _card, bool _owned, List<Graphic> _into)
    {
        if (_into == null) return;
        _into.Clear();

        if (_card == null || this.keywordFrames == null) return;

        CardKeyword t_keywords = _owned && this.ShowKeywords ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;

        foreach (KeywordFrame t_frame in this.keywordFrames)
        {
            if (t_frame.overlay == null || t_frame.overlay.activeSelf) continue;
            if (t_frame.keyword == CardKeyword.None || (t_keywords & t_frame.keyword) == 0) continue;

            var t_graphic = t_frame.overlay.GetComponent<Graphic>();
            if (t_graphic != null) _into.Add(t_graphic);
        }
    }

    /// <summary>바뀐 최대 체력을 _from에서부터 굴려 보여준다(강화 결과 공개용).
    /// 표시 문장·최종값의 정본은 여전히 <see cref="SetHpDisplay"/>다 — 굴리는 동안의 중간 숫자만 여기서 만들고,
    /// 끝나든 잘리든 그쪽으로 되돌려 못 박는다(반올림 중간값이 남지 않는다).
    ///
    /// 굴릴 것이 없으면(미소유·표시 꺼짐·오르지 않음) 즉시 반영하고 null을 돌려준다 — 강화 실패가 이 길로 온다.
    /// _duration은 호출부가 정한다: 결과판의 체력 행과 같은 길이여야 두 숫자가 한 박에 움직인다.</summary>
    public Tween RollHp(CardData _card, bool _owned, int _from, float _duration)
    {
        if (_card == null) return null;

        KillHpRoll();

        int t_to = DeckPower.MaxHpOf(_card);

        if (this.hpText == null || !(_owned && this.ShowHp) || _duration <= 0f || t_to <= _from)
        {
            RefreshHp(_card, _owned);
            return null;
        }

        // 패널·보너스는 먼저 최종 상태로 세운다 — 굴러야 하는 것은 숫자 하나뿐이다.
        SetHpDisplay(_card, true, true);
        CaptureHpVisual();
        this.hpText.text = _from.ToString();

        float t_shown = _from;
        float t_span  = t_to - _from;
        bool  t_done  = false;   // 정상 종료 여부. 잘렸으면 마무리 박자도 없다.

        this.m_hpRoll = DOTween.To(() => t_shown, _v =>
                                   {
                                       t_shown = _v;
                                       if (this.hpText == null) return;

                                       this.hpText.text = Mathf.RoundToInt(_v).ToString();

                                       // 색은 굴리는 도중에 가장 짙고 끝에서 원래 색으로 돌아온다.
                                       // 별도 트윈으로 두면 잘렸을 때 물든 채 굳으므로 같은 축에 얹는다.
                                       float t_p    = Mathf.Clamp01((_v - _from) / t_span);
                                       float t_wave = Mathf.Sin(t_p * Mathf.PI);

                                       this.hpText.color = Color.Lerp(this.m_hpBaseColor, this.hpRollFlashColor, t_wave);

                                       // 아이콘도 같은 파형 위에서 부풀었다 돌아온다 — 축을 나누면 숫자와 따로 논다.
                                       if (this.hpIcon != null)
                                           this.hpIcon.transform.localScale =
                                               this.m_hpIconBaseScale * (1f + this.hpIconPulse * t_wave);
                                   },
                                   (float)t_to, _duration)
                               .SetEase(Ease.OutQuad)   // 결과판의 체력 행과 같은 곡선 — 두 숫자가 따로 놀지 않는다.
                               .SetLink(this.hpText.gameObject)
                               .OnComplete(() => t_done = true)
                               .OnKill(() =>
                               {
                                   this.m_hpRoll = null;

                                   // 복원이 먼저다 — autoKill이라 OnComplete와 같은 프레임에 여기 오므로,
                                   // 펀치를 앞에 두면 배율 복원이 방금 시작한 펀치를 도로 걷어낸다.
                                   RestoreHpVisual();

                                   if (t_done)
                                   {
                                       UiPunch.Play(this.hpText.transform);
                                       if (this.hpIcon != null) UiPunch.Play(this.hpIcon.transform, this.hpIconPunch);
                                   }

                                   RefreshHp(_card, _owned);
                               });

        return this.m_hpRoll;
    }

    void KillHpRoll()
    {
        Tween t_roll  = this.m_hpRoll;
        this.m_hpRoll = null;
        t_roll?.Kill();   // OnKill이 숫자·색·배율을 되돌린다.

        // 굴리기가 이미 끝났어도 마무리 펀치는 남아 있을 수 있다(그땐 m_hpRoll이 null이라 위 Kill이 못 잡는다).
        // 카드 교체·연속 강화가 모두 여기를 지나므로 그 잔상도 여기서 걷는다.
        RestoreHpVisual();
    }

    void CaptureHpVisual()
    {
        if (this.m_hpBaseCaptured || this.hpText == null) return;

        this.m_hpBaseCaptured = true;
        this.m_hpBaseColor    = this.hpText.color;

        if (this.hpIcon != null) this.m_hpIconBaseScale = this.hpIcon.transform.localScale;
    }

    // 굴리기가 끝나든 잘리든 여기 한 곳에서 기준 상태로 못 박는다(멱등).
    void RestoreHpVisual()
    {
        if (!this.m_hpBaseCaptured) return;

        if (this.hpText != null) this.hpText.color = this.m_hpBaseColor;

        if (this.hpIcon != null)
        {
            // 대입만 하면 아직 도는 펀치가 다음 프레임에 제 배율을 다시 쓴다 → 먼저 완료시켜 소유권을 회수한다.
            this.hpIcon.transform.DOComplete();
            this.hpIcon.transform.localScale = this.m_hpIconBaseScale;
        }
    }

    // HP 표시. 인게임 CardView.SetHpDisplay 규약과 동일 — bonus는 값이 있을 때만 오브젝트를 켠다.
    // 아웃게임엔 전투 인스턴스(CardInstance.hp)가 없으므로 내 카드는 강화 반영 최대 체력(DeckPower.MaxHpOf)을 그린다 —
    // 마스터 데이터의 maxHp를 직접 읽으면 강화한 카드가 로비에서만 안 오른 것처럼 보인다.
    // 반대로 상대 카드(_mine=false)는 내 진행도가 아니라 상대 레벨 기준이다.
    /// <summary>강화 레벨 표시. 상대 덱도 띄운다 — 상대가 몇 레벨 카드로 나오는지가 트레이드 판단의 핵심이다.
    /// 값의 기준만 갈린다(내 카드=내 진행도, 상대=랭크 티어 AI 레벨). 판정은 DeckPower가 소유.</summary>
    void SetLevelDisplay(CardData _card, bool _show, bool _mine)
    {
        if (this.levelText == null) return;

        this.levelText.gameObject.SetActive(_show);
        if (_show) this.levelText.text = $"Lv{DeckPower.LevelOf(_card, _mine)}";
    }

    void SetHpDisplay(CardData _card, bool _show, bool _mine)
    {
        if (this.hpPanel != null) this.hpPanel.SetActive(_show);

        if (this.hpText != null)
        {
            this.hpText.gameObject.SetActive(_show);
            if (_show) this.hpText.text = DeckPower.MaxHpOf(_card, _mine).ToString();
        }

        if (this.bonusHpText != null)
        {
            bool t_hasBonus = _show && _card.bonusHp > 0;
            this.bonusHpText.gameObject.SetActive(t_hasBonus);
            if (t_hasBonus) this.bonusHpText.text = $"+{_card.bonusHp}";
        }
    }

    // 키워드 아이콘 갱신. 표시 대상·순서는 인게임과 같은 CardVisualRules 호출로 얻는다.
    // 아웃게임엔 런타임 부여 키워드(CardInstance.runtimeKeywords)가 없으므로 마스터 데이터의 keywords만 넘긴다.
    void RefreshKeywordIcons(CardData _card, bool _show)
    {
        if (this.keywordIconRoot == null) return;
        ClearChildren(this.keywordIconRoot);

        if (!_show || this.keywordIconPrefab == null || this.keywordIconConfig == null) return;

        // 아웃게임엔 전투 런타임 상태가 없다 → 상태 전용 키워드(무적·추가체력)는 애초에 제외.
        // 판정은 인게임 특성 줄과 같은 CardVisualRules.IconKeywords 하나. 로비/전투 표시가 갈리지 않는다.
        // (아이콘 줄 전용 제외분 = 표식. 프레임 장식은 아래 RefreshKeywordFrames가 TraitKeywords로 그대로 띄운다.)
        int t_index = 0;
        foreach (CardVisualRules.KeywordIcon t_entry in
                 CardVisualRules.CollectKeywordIcons(CardVisualRules.IconKeywords(_card), this.keywordIconConfig))
        {
            CardKeywordIconView t_view = Instantiate(this.keywordIconPrefab, this.keywordIconRoot);
            t_view.SetIcon(t_entry.Icon);
            PlaceKeywordIcon(t_view.transform as RectTransform, t_index++);
        }
    }

    // 인게임은 keywordIconStart에서 keywordIconStep만큼 밀며 아이콘을 직접 찍는다. uGUI 미러도 LayoutGroup에
    // 맡기지 않고 같은 좌표를 정규화 앵커로 옮긴다 — LayoutGroup은 간격·크기를 픽셀로 잡아서 카드 셀 크기가
    // 바뀌면(도감 386px vs 팩개봉 930px) 인게임과 비율이 어긋난다. 앵커는 부모 rect 비율이라 어긋나지 않는다.
    static void PlaceKeywordIcon(RectTransform _rect, int _index)
    {
        if (_rect == null) return;

        var t_center = new Vector2(KeywordIconStartX + KeywordIconStepX * _index,
                                   KeywordIconStartY + KeywordIconStepY * _index);
        var t_half   = new Vector2(KeywordIconWidth, KeywordIconHeight) * 0.5f;

        _rect.anchorMin        = t_center - t_half;
        _rect.anchorMax        = t_center + t_half;
        _rect.sizeDelta        = Vector2.zero;
        _rect.anchoredPosition = Vector2.zero;
        _rect.localScale       = Vector3.one;
    }

    // 프레임 키워드 장식(처형·도발·힐러·원거리·교활·무쌍·표식). 인게임 CardView.RefreshKeywordFrames와 같은 규약:
    // 기준은 TraitKeywords(아이콘 줄만 IconKeywords로 표식을 더 뺀다), 미소유/빈 카드는 전부 끈다(정보 은닉).
    void RefreshKeywordFrames(CardData _card, bool _show)
    {
        if (this.keywordFrames == null) return;

        CardKeyword t_keywords = _show ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;

        foreach (KeywordFrame t_frame in this.keywordFrames)
        {
            if (t_frame.overlay == null) continue;
            // None 배선은 항상 꺼짐 — HasFlag(None)은 늘 true라 그대로 두면 모든 카드에서 켜진다.
            bool t_on = t_frame.keyword != CardKeyword.None && (t_keywords & t_frame.keyword) != 0;
            t_frame.overlay.SetActive(t_on);
        }
    }

    // 시너지 배지 갱신. 표시 대상·순서는 인게임과 같은 CardVisualRules 호출로 얻는다.
    void RefreshSynergyBadges(CardData _card, bool _show)
    {
        if (this.synergyBadgeRoot == null) return;
        ClearChildren(this.synergyBadgeRoot);

        if (!_show || this.synergyBadgePrefab == null) return;

        // 아웃게임엔 전투 스냅샷(SynergyState)이 없어 활성 판정의 진실원이 없다 → null을 넘긴다.
        // 활성 판정은 전부 false가 되지만 requiredCount 내림차순 정렬은 그대로 성립한다
        // (GetBadgeRequiredCount가 스냅샷이 없으면 tiers 최고값으로 폴백) → 배지 세로 순서가 전투와 일치한다.
        List<SynergyData> t_tags = CardVisualRules.CollectSynergyBadges(_card.synergies, null, this.synergyMaxBadges);

        foreach (SynergyData t_syn in t_tags)
        {
            CardSynergyBadgeView t_badge = Instantiate(this.synergyBadgePrefab, this.synergyBadgeRoot);
            // 아이콘만은 활성(active=true)으로 그린다 — 도감/덱편집은 "이 카드가 가진 시너지" 소개가 목적이라
            // 전투 스냅샷이 없다는 이유로 전부 흐린 inactiveIcon을 보여줄 이유가 없다. 정렬만 인게임 규칙을 따른다.
            t_badge.Set(t_syn, true);
        }
    }

    // 재바인딩 시 이전 아이콘/배지를 제거. 인게임은 파괴 전 DOKill로 tween을 정리하지만
    // 아웃게임 타일은 CardAnimator 페이드 대상이 아니라 자식에 걸린 tween 자체가 없다 → DOKill 불필요.
    static void ClearChildren(Transform _root)
    {
        for (int t_i = _root.childCount - 1; t_i >= 0; t_i--)
            Destroy(_root.GetChild(t_i).gameObject);
    }
}
