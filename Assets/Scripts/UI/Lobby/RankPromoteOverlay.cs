using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// 승급 오버레이가 세울 사건의 갈래. 안무가 통째로 갈리므로 부르는 쪽이 정해서 넘긴다.
public enum EPromoteKind
{
    // 언랭크 → 첫 티어. 옛 배지가 없어 파열 박이 빠진다.
    FirstEntry,

    // 등급이 갈린다(브론즈 4 → 실버 1). 배지 교체가 주인공이다.
    GradeUp,
}

// 등급 승급(승급전 승리)·첫 진입을 화면 전체를 멈춰 세우는 **한 개의 사건**으로 세우는 오버레이.
//
// **단계 상승은 여기 오지 않는다.** 4승마다 오는 사건이라 전면 암전으로 멈춰 세우면 방해가 돼서,
// 로비 RankInfo가 제자리에서 떠오르는 안 멈추는 연출(RankHud.BuildDivisionUpInPlace)로 갈라져 나갔다.
// 드물게 오는 것만 여기서 멈춰 세우는 것이 두 사건의 격을 벌리는 방법이다.
//
// 배지 안에서 여덟 단계로 흩어져 벌어지던 것을 여기로 옮긴다 — 약한 사건 여덟 개보다 강한 사건 하나가 크다.
// 그래서 안무의 모든 축(섬광·링·광선·킥·배지)이 **같은 한 프레임**에 겹친다. 시간축에 흩지 말 것.
//
// 등급 승급은 암전 → 정적 → 꽂힘 → 충격 → 여운의 순서이고, 정적(silence)이 이 안무의 절반이다.
// 어두운 화면에 아무것도 없는 그 빈 박이 다음 프레임을 만든다.
//
// 판때기는 전부 프리팹 저작이고 코드는 알파·배율·회전만 민다(RankPromoStandby·CardEvolveRays와 같은 규약).
// 씬에 저작하지 않고 Addressables 타입 색인에서 독립 Canvas로 세운다(UnlockIntroOverlay와 같은 규약).
public class RankPromoteOverlay : SingletonOverlay<RankPromoteOverlay>
{
    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [Tooltip("화면 어디를 눌러도 받는 투명 버튼. 안무 도중에 눌리면 건너뛰고 곧바로 닫힌다.")]
    [SerializeField] Button tapButton;

    [Tooltip("암전 위에 서는 내용 전체(배지·광선·링·등급명)를 묶는 그룹.\n" +
             "암전만 먼저 깔려야 정적이 성립하므로 여기 알파는 0에서 출발해 배지가 꽂히는 프레임에 켜진다.")]
    [SerializeField] CanvasGroup contentGroup;

    [Tooltip("충격 프레임에 킥을 먹일 뿌리. 미배선이면 contentGroup 노드를 쓴다.")]
    [SerializeField] RectTransform kickRoot;

    [Tooltip("도달한 등급의 배지. 이 노드가 2.2배에서 제자리로 꽂힌다.")]
    [SerializeField] Image badgeImage;

    [Tooltip("도달한 등급 이름. **배지가 꽂히기 전에 이미 찍혀 있다** — 굴리거나 뒤늦게 페이드인시키지 않는다.")]
    [SerializeField] TMP_Text tierNameText;

    [Tooltip("충격 프레임에 퍼져 사라지는 버스트 링 한 장. 비면 링 축을 통째로 건너뛴다.\n" +
             "배지보다 앞선 형제여야 배지를 가리지 않는다(uGUI는 나중 형제를 위에 그린다).")]
    [SerializeField] Graphic burstRing;

    [Tooltip("배지 뒤에서 뻗는 광선 판들. 비면 광선 축을 통째로 건너뛴다.\n" +
             "\n" +
             "저작 규약 세 가지 (어기면 광선으로 안 읽힌다 — RankPromoStandby와 같다):\n" +
             "  · 피벗은 (0.5, 0) — 뿌리가 배지 중심이고 거기서 바깥으로 뻗는다.\n" +
             "  · 폭은 길이의 0.12 안팎. 0.2를 넘기면 광선이 아니라 꽃잎이 된다.\n" +
             "  · 각도를 등간격으로 두지 말 것 — 나란하면 바람개비로 읽힌다.\n" +
             "배지보다 **앞선 형제**여야 배지를 가리지 않는다.")]
    [SerializeField] Graphic[] rays;

    [Tooltip("\"탭하여 계속\" 안내 그룹. 안무가 다 끝난 뒤에만 뜬다 — 처음부터 보이면 사건을 안 보고 넘긴다.")]
    [SerializeField] CanvasGroup hintGroup;

    [Tooltip("등급 승급에서 파열하는 **옛 등급의 배지**. 비면 옛 배지·파열 두 박을 통째로 건너뛴다.\n" +
             "새 배지(badgeImage)와 같은 자리·같은 크기여야 '교체'로 읽힌다. 형제 순서는 새 배지보다 앞.")]
    [SerializeField] Image fromBadgeImage;

    [Tooltip("화면 위쪽 문구. 갈래마다 갈린다(titleGradeUp / titleFirstEntry).\n" +
             "비면 문구 축을 건너뛰므로 프리팹에 저작된 문구가 두 갈래 모두에 그대로 뜬다.")]
    [SerializeField] TMP_Text titleText;

    [Tooltip("도달한 **등급** 이름(SILVER 등 대문자). 등급 승급·첫 진입에서만 떠오른다.\n" +
             "비면 등급명 축을 통째로 건너뛴다.")]
    [SerializeField] TMP_Text gradeNameText;

    [Header("갈래별 문구·색")]
    [Tooltip("등급 팔레트. **배열 순서 = ERankGrade 순서(브론즈 / 실버 / 골드 / 플래티넘 / 다이아)** 다 — " +
             "등급을 늘리면 여기도 같은 자리에 색을 채워야 한다.\n" +
             "**배열이 비었거나 도달 등급보다 짧으면 그 등급은 흰색으로 떨어진다**(연출은 그대로 돈다).\n" +
             "이 색은 섬광·버스트 링·광선에 실린다. RGB만 갈아끼우고 알파는 각 판의 저작값을 그대로 쓴다.\n" +
             "저작 후보: 브론즈 #C88A46 / 실버 #D8E0E8 / 골드 #F2C14E / 플래티넘 #7FD4C1 / 다이아 #8FC4F5")]
    [SerializeField] Color[] gradeColors;

    [Tooltip("등급 승급(승급전 승리)에 세우는 문구.")]
    [SerializeField] string titleGradeUp = "승급전 승리!";

    [Tooltip("첫 진입(언랭크 → 첫 티어)에 세우는 문구.")]
    [SerializeField] string titleFirstEntry = "랭크 배정";

    [Header("연출")]
    [Tooltip("암전 자체. openDuration이 곧 급암전 시간이다 — 0.1을 넘기면 '멈춰 세웠다'가 아니라 '어두워진다'로 읽힌다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("암전이 끝나고 첫 그림이 서기까지, 어두운 화면에 **아무것도 없는** 시간.\n" +
             "**이 빈 박이 다음 걸 만든다 — 줄이지 마라.**")]
    [SerializeField] float silence = 0.28f;

    [Tooltip("배지가 출발하는 배율. 화면 밖에서 날아드는 크기라 2를 밑돌면 '꽂혔다'가 안 읽힌다.")]
    [SerializeField] float slamFromScale = 2.2f;

    [Tooltip("배지가 제자리에 꽂히는 시간(가속). 짧게 둔다 — 이 끝이 모든 사건이 겹치는 한 프레임이다.")]
    [SerializeField] float slamDuration = 0.08f;

    [Header("충격 — 배지가 꽂히는 한 프레임")]
    [Tooltip("화면 전체를 덮는 섬광 한 벌.\n" +
             "**burstSprite를 반드시 채울 것**(후보: Sprites/CardPack/Glow_Radial) — 배경이 어두운 자리라 " +
             "빛이 들어가야 살고, 비면 단색 흰 판만 지나간다.\n" +
             "여기 색은 도달 등급의 색(gradeColors)이 RGB를 덮는다 — 저작색은 알파만 쓰인다.")]
    [SerializeField] ScreenFlashCover flash = new ScreenFlashCover();

    [Tooltip("링이 출발하는 배율(저작 크기 대비).")]
    [SerializeField] float ringFromScale = 0.2f;
    [Tooltip("링이 퍼져 나가 사라지는 배율(저작 크기 대비). 1보다 커야 '터졌다'로 읽힌다.")]
    [SerializeField] float ringToScale = 1.7f;
    [Tooltip("링이 퍼지며 사라지는 시간.")]
    [SerializeField] float ringDuration = 0.4f;

    [Tooltip("화면이 받는 킥(내용 뿌리 펀치). 작게 둔다 — 크면 배지가 아니라 화면이 주인공이 된다.")]
    [SerializeField] float kickPunch = 0.06f;
    [SerializeField] float kickDuration = 0.3f;

    [Tooltip("배지가 눌렸다 돌아오는 정도. 부풀리는 것이 아니라 눌리는 것이라 부호가 반대다.")]
    [SerializeField] float badgeSquash = 0.12f;
    [SerializeField] float badgeSquashDuration = 0.26f;

    [Header("광선 점화")]
    [Tooltip("점화 직전 광선의 길이 배율(저작 대비). 여기서 뻗어 나온다.")]
    [SerializeField] float igniteSeedLength = 0.3f;
    [Tooltip("광선이 저작 길이까지 뻗는 시간.")]
    [SerializeField] float igniteDuration = 0.14f;
    [Tooltip("점화 순간 잠깐 과다 노출되는 밝기. 여기서 저작 알파로 정착한다.")]
    [Range(0f, 1f)]
    [SerializeField] float igniteOverAlpha = 1f;
    [Tooltip("과다 노출이 저작 알파로 가라앉는 시간.")]
    [SerializeField] float igniteSettle = 0.2f;

    [Header("여운")]
    [Tooltip("광선 뭉치가 한 바퀴 도는 각도. 부호로 방향이 갈린다.")]
    [SerializeField] float spinDegrees = 360f;
    [Tooltip("한 바퀴에 걸리는 시간. 길수록 '떠 있다'에 가깝고, 짧으면 시선을 잡아먹는다.")]
    [SerializeField] float spinDuration = 20f;
    [Tooltip("배지 호흡의 최대 배율. 1.05를 넘기면 여운이 아니라 새 사건으로 읽힌다.")]
    [SerializeField] float breatheScale = 1.04f;
    [Tooltip("호흡 한 결의 시간(들숨 = 날숨).")]
    [SerializeField] float breatheDuration = 0.9f;

    [Header("안내")]
    [Tooltip("충격 프레임부터 \"탭하여 계속\"이 뜨기까지의 뜸. 여운을 보는 시간이다.")]
    [SerializeField] float hintDelay = 1.1f;
    [SerializeField] float hintFade = 0.2f;

    [Header("티어명 교체")]
    [Tooltip("티어명이 출발하는 배율(저작 크기 대비).")]
    [FormerlySerializedAs("divisionNameSlamFrom")]
    [SerializeField] float tierNameSlamFrom = 1.9f;

    [Tooltip("티어명이 제자리로 오는 시간(가속). 굴리지 않고 드러내는 것이라 짧게 둔다.")]
    [FormerlySerializedAs("divisionNameSlam")]
    [SerializeField] float tierNameSlam = 0.1f;

    [Tooltip("티어명이 갈리는 프레임의 킥.")]
    [FormerlySerializedAs("divisionNameKick")]
    [SerializeField] float tierNameKick = 0.05f;

    [Header("등급 승급 — 배지 교체가 주인공")]
    [Tooltip("옛 배지가 홀로 서 있는 시간. 여기서 '무엇이 갈리는지'를 먼저 보여준다.")]
    [SerializeField] float gradeFromHold = 0.3f;

    [Tooltip("옛 배지 뒤로 빛이 번지는 시간(광선이 씨앗 알파까지 밝아진다). **이 끝이 파열 프레임이다.**")]
    [SerializeField] float gradeSeedRise = 0.55f;

    [Tooltip("번지는 동안 광선이 머무는 알파. 저작 알파보다 크면 저작 알파로 잘린다 — " +
             "여기서 다 밝아지면 뒤따르는 점화가 안 읽힌다.")]
    [Range(0f, 1f)]
    [SerializeField] float gradeSeedAlpha = 0.22f;

    [Tooltip("빛이 번지는 동안 옛 배지가 떠는 폭(px). 0이면 진동 축을 건너뛴다.")]
    [SerializeField] float gradeShiver = 6f;

    [Tooltip("옛 배지가 파열하며 튀는 배율(저작 크기 대비).")]
    [SerializeField] float gradeShatterScale = 1.35f;

    [Tooltip("옛 배지가 튀며 사라지는 시간. 새 배지가 꽂히는 시간(slamDuration)과 겹쳐야 '교체'로 읽힌다.")]
    [SerializeField] float gradeShatterDuration = 0.12f;

    [Tooltip("배지가 출발한 뒤 티어명·등급명이 갈리기까지의 뜸.")]
    [SerializeField] float gradeNameDelay = 0.4f;

    [Tooltip("등급명이 떠오르며 밝아지는 시간.")]
    [SerializeField] float gradeNameFade = 0.25f;

    [Tooltip("등급명이 떠오르는 높이(px). 저작 자리보다 이만큼 아래에서 출발한다.")]
    [SerializeField] float gradeNameRise = 40f;

    [Tooltip("첫 진입에서 배지가 꽂히는 시각(암전이 덮인 뒤부터 잰다). 옛 배지·파열 두 박이 없어 그만큼 당긴다 — " +
             "등급 승급과 같은 시각에 두면 빈 화면만 오래 본다.\n" +
             "침묵(silence)보다 짧으면 침묵 끝으로 밀린다.")]
    [SerializeField] float firstEntrySlamAt = 0.45f;

    // 진행 중 안무. 건너뛰기·닫기가 도중에 와도 저작 상태로 되돌린 뒤 이어가야 한다.
    Sequence m_choreo;

    // 닫힘 콜백. 한 번 쓰면 비워 연타를 막는다.
    Action m_onClose;

    // 무한 루프 트윈(광선 회전·배지 호흡). 참조를 들고 있다가 정리에서 걷는다.
    Tween[] m_spins;
    Tween   m_breathe;

    // 옛 배지 축을 태울 판인가(등급 승급이고 배지 배선·스프라이트가 둘 다 있을 때만).
    bool m_hasFrom;

    // 티어명 두 벌. 교체 프레임과 최종 자세가 같은 문자열을 봐야 한다.
    string m_fromName = string.Empty;
    string m_toName   = string.Empty;

    // 저작 상태 1회 캡처. 안무가 미는 값의 원본이자 점화가 정착할 목표다.
    Vector3[] m_rayScales;
    Vector3[] m_rayEulers;
    float[]   m_rayAlphas;
    Vector3   m_ringScale = Vector3.one;
    float     m_ringAlpha = 1f;
    Color     m_flashColor = Color.white;
    float     m_hintAlpha = 1f;
    float     m_badgeAlpha      = 1f;
    Vector3   m_tierNameScale   = Vector3.one;
    float     m_fromBadgeAlpha  = 1f;
    Vector2   m_fromBadgePos;
    Vector3   m_fromBadgeScale  = Vector3.one;
    float     m_gradeNameAlpha  = 1f;
    Vector2   m_gradeNamePos;
    bool      m_captured;

    /// <summary>승급 오버레이를 얻는다. 평소 꺼져 있는 노드라 이미 선 것을 찾을 때는 비활성까지 뒤진다
    /// (UnlockIntroOverlay와 같은 규약).</summary>
    public static bool TryGet(out RankPromoteOverlay _overlay)
        => TryGetOrCreate(RuntimeOverlayPrefabs.Get<RankPromoteOverlay>, out _overlay);

    /// <summary>등급이 갈린 판(또는 첫 진입)을 전면에 세우고 탭을 기다린다.
    /// _kind는 옛 배지 파열 두 박이 서는지를 가른다 — 첫 진입은 옛 배지가 없어 그 박이 빠진다.
    /// _from은 등급 승급에서만 읽는다(첫 진입은 RankTier.None을 넘기면 된다).
    /// _onCovered는 암전이 완전히 덮인 프레임에 정확히 한 번 온다 — 로비 표시를 갈아끼우는 자리다
    /// (PackPurchaseImpact.Play의 덮임 통지와 같은 규약). 건너뛰기로 안무가 잘려도 반드시 한 번은 온다.
    /// _onClose는 걷힌 뒤 정확히 한 번 온다.
    /// 안무 도중이라도 탭 한 번이면 최종 상태로 점프한 뒤 닫힌다 — 두 번 봐야 하는 화면이 아니다.</summary>
    public void Show(RankTier _from, RankTier _to, EPromoteKind _kind, Action _onCovered, Action _onClose)
    {
        // 직전 표시의 안무를 걷는다 — 시퀀스에 중첩된 트윈은 대상의 DOKill이 잡지 못해 새 안무와 같은 노드를 함께 민다.
        KillChoreo();
        Capture();

        this.m_onClose = _onClose;

        // 옛 배지 축은 셋이 다 있어야 선다 — 하나라도 비면 파열 없이 새 배지만 꽂힌다(첫 진입과 같은 그림).
        this.m_hasFrom = _kind == EPromoteKind.GradeUp && this.fromBadgeImage != null && _from.Badge != null;

        this.m_toName   = _to.DisplayName;
        this.m_fromName = _kind != EPromoteKind.FirstEntry && !string.IsNullOrEmpty(_from.DisplayName)
                              ? _from.DisplayName
                              : _to.DisplayName;

        if (this.badgeImage != null && _to.Badge != null) this.badgeImage.sprite = _to.Badge;
        if (this.m_hasFrom) this.fromBadgeImage.sprite = _from.Badge;

        // 티어명은 **옛 이름으로 서 있다가** 한 박에 갈린다(굴리지 않고 드러낸다).
        if (this.tierNameText != null) this.tierNameText.text = this.m_fromName;
        if (this.titleText != null) this.titleText.text = TitleOf(_kind);
        if (this.gradeNameText != null) this.gradeNameText.text = _to.Grade.ToString().ToUpperInvariant();

        ApplyGradeTint(_to.Grade);

        if (this.tapButton != null)
        {
            this.tapButton.onClick.RemoveAllListeners();   // 재표시마다 중복 등록 방지
            this.tapButton.onClick.AddListener(OnTapped);
        }

        IsOpen = true;
        SetVisible(true);

        // 손은 처음부터 열어 둔다 — 이 화면의 유일한 문이라, 안무가 어디서 끊겨도 잠긴 모달로 남지 않는다.
        SetInputEnabled(true);

        // 덮임 통지는 정확히 1회. 시간축과 중단 안전망 양쪽에서 부르므로 여기서 잠근다 —
        // 건너뛰기로 안무가 잘려도 이것이 빠지면 로비가 옛 등급에 고착된다.
        bool t_fired = false;
        void Fire()
        {
            if (t_fired) return;
            t_fired = true;
            _onCovered?.Invoke();
        }

        this.m_choreo = BuildChoreography(Fire);
        this.m_choreo.OnKill(Fire);   // 정상 종료든 중단이든 여기로 온다.
        this.m_choreo.Play();
    }

    /// <summary>밖에서 걷는다(화면이 통째로 넘어가는 경로). 콜백은 흘리지 않는다 —
    /// 이 길로 닫는 쪽은 이미 자기 흐름을 쥐고 있다.</summary>
    public void Hide()
    {
        this.m_onClose = null;

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        KillChoreo();
        ResetChoreography();
        SetVisible(false);

        if (t_wasOpen) RaiseClosed();
    }

    // Show를 거치지 않고 뜨는 경로(부모가 다시 켜짐)에서도 문이 잠기지 않게 열어 둔다.
    void OnEnable()
    {
        SetInputEnabled(true);
    }

    // 오버레이는 자기 자신이 토글 대상이라 OnDisable이 정상 동작한다 — 잘린 퇴장 마무리를 여기서 위임한다.
    void OnDisable()
    {
        this.transition.HandleDisabled(ResolveTarget());
        KillChoreo();
        ResetChoreography();

        // 꺼진 화면은 떠 있는 것이 아니다. Hide를 거치지 않고 꺼지는 경로(부모 비활성·씬 언로드)에서
        // 이 플래그가 남으면 "로비 표면이 보이는가" 판정이 영영 false가 된다.
        IsOpen = false;
    }

    // 탭 한 번이 곧 끝이다. 안무 중이면 최종 상태로 점프한 뒤 닫는다.
    void OnTapped()
    {
        // 연타는 여기서 막는다. **콜백 유무로 막지 않는다** — 닫기와 콜백 소비는 별개라,
        // 콜백을 안 넘긴 호출자에게 화면이 영영 남는 길을 만들면 안 된다.
        if (!IsOpen) return;

        // 콜백은 먼저 비운다. 닫기 도중에 다시 들어와도 두 번 흐르지 않는다.
        var t_callback = this.m_onClose;
        this.m_onClose = null;

        CloseNow();

        // 넘겨주기는 정리가 다 끝난 뒤다 — 받는 쪽이 이 화면의 상태를 다시 물어볼 수 있어야 한다.
        t_callback?.Invoke();
    }

    // 화면을 걷고 안무를 최종 자세로 확정한다. 콜백 소비와 갈라 둔 자리다 — 받을 사람이 없어도 닫히는 건 닫힌다.
    void CloseNow()
    {
        SetInputEnabled(false);

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        KillChoreo();
        ResetChoreography();
        SetVisible(false);

        if (t_wasOpen) RaiseClosed();
    }

    // 암전 → 덮임 통지 → 안무. 갈래는 하나뿐이고 옛 배지 유무만 안에서 갈린다.
    Sequence BuildChoreography(TweenCallback _onCovered)
    {
        PrimeChoreography();

        var t_seq = DOTween.Sequence().SetLink(gameObject);

        // 덮인 시각은 암전 자신이 낸다 — 이 값을 따로 재면 아직 비치는 화면 위에서 로비 표시가 갈린다.
        float t_covered = Mathf.Max(0f, this.transition.OpenDuration);

        // 암전이 완전히 덮인 프레임. 뒤가 빈 박이라 여기서 갈아끼우는 것은 보이지 않는다.
        t_seq.InsertCallback(t_covered, _onCovered);

        BuildGradeUp(t_seq, t_covered);

        t_seq.OnComplete(() => this.m_choreo = null);
        return t_seq;
    }

    // 등급 승급·첫 진입 — 정적 → 옛 배지 → 빛 번짐 → 파열·꽂힘 → 이름 → 여운 → 탭 대기.
    // 첫 진입은 옛 배지가 없어 앞 두 박이 빠지고, 빈 화면만 오래 보지 않게 꽂힘을 앞으로 당긴다.
    void BuildGradeUp(Sequence _seq, float _covered)
    {
        float t_quiet = _covered + Mathf.Max(0f, this.silence);

        float t_slamAt = this.m_hasFrom
                             ? t_quiet + Mathf.Max(0f, this.gradeFromHold) + Mathf.Max(0.05f, this.gradeSeedRise)
                             : Mathf.Max(t_quiet, _covered + Mathf.Max(0f, this.firstEntrySlamAt));

        float t_slam   = Mathf.Max(0.02f, this.slamDuration);
        float t_impact = t_slamAt + t_slam;

        // 옛 배지가 서는 판은 그 프레임에 내용을 켠다 — 첫 진입은 켤 것이 없어 꽂힘까지 어두운 채로 둔다.
        float t_contentAt = this.m_hasFrom ? t_quiet : t_slamAt;
        if (this.contentGroup != null)
            _seq.InsertCallback(t_contentAt, () => this.contentGroup.alpha = 1f);

        if (this.m_hasFrom)
        {
            float t_seedAt = t_quiet + Mathf.Max(0f, this.gradeFromHold);

            StageFromBadge(_seq, t_quiet, t_seedAt);
            StageSeed(_seq, t_seedAt);
            StageShatter(_seq, t_slamAt);
        }

        RectTransform t_badge = this.badgeImage != null ? this.badgeImage.rectTransform : null;

        // 가속으로 꽂힌다. OutBack으로 되튀기면 '내려앉았다'가 되어 타격이 죽는다.
        if (t_badge != null)
        {
            _seq.InsertCallback(t_slamAt, () =>
            {
                t_badge.localScale = Vector3.one * Mathf.Max(0.01f, this.slamFromScale);
                SetAlpha(this.badgeImage, this.m_badgeAlpha);
            });

            _seq.Insert(t_slamAt, t_badge.DOScale(1f, t_slam).SetEase(Ease.InQuad));
        }

        StageImpact(_seq, t_impact, t_badge);
        StageRays(_seq, t_impact);
        StageAfterglow(_seq, t_impact);

        float t_nameAt = t_slamAt + Mathf.Max(0f, this.gradeNameDelay);
        StageNameSwap(_seq, t_nameAt);
        StageGradeName(_seq, t_nameAt);

        StageHint(_seq, t_impact + Mathf.Max(0f, this.hintDelay));
    }

    // 티어명이 옛 이름에서 새 이름으로 **한 프레임에** 갈린다. 문구 교체가 곧 사건이라 굴리지 않는다.
    void StageNameSwap(Sequence _seq, float _at)
    {
        RectTransform t_kick = ResolveKickRoot();
        if (t_kick != null)
            _seq.InsertCallback(_at, () => UiPunch.Play(t_kick, this.tierNameKick, this.kickDuration));

        if (this.tierNameText == null) return;

        RectTransform t_rect = this.tierNameText.rectTransform;
        float         t_dur  = Mathf.Max(0.02f, this.tierNameSlam);

        _seq.InsertCallback(_at, () =>
        {
            this.tierNameText.text = this.m_toName;
            t_rect.localScale      = this.m_tierNameScale * Mathf.Max(1f, this.tierNameSlamFrom);
        });

        _seq.Insert(_at, t_rect.DOScale(this.m_tierNameScale, t_dur).SetEase(Ease.InQuad));
    }

    // 옛 배지가 홀로 선다. 곧 갈릴 것을 먼저 보여줘야 교체가 사건이 된다.
    void StageFromBadge(Sequence _seq, float _at, float _shiverAt)
    {
        if (!this.m_hasFrom) return;

        RectTransform t_rect = this.fromBadgeImage.rectTransform;

        _seq.InsertCallback(_at, () =>
        {
            t_rect.anchoredPosition = this.m_fromBadgePos;
            t_rect.localScale       = this.m_fromBadgeScale;
            SetAlpha(this.fromBadgeImage, this.m_fromBadgeAlpha);
        });

        if (this.gradeShiver <= 0f) return;

        // fadeOut으로 폭이 0까지 잦아든다 — 진동이 끝나는 프레임이 곧 파열 프레임이라 자리가 흔들린 채 넘어가면 안 된다.
        _seq.Insert(_shiverAt, t_rect.DOShakeAnchorPos(Mathf.Max(0.05f, this.gradeSeedRise), this.gradeShiver,
                                                       vibrato: 14, randomness: 90f, snapping: false, fadeOut: true));
    }

    // 빛이 번진다 — 광선이 씨앗 길이에서 낮은 알파까지만 밝아진다. 완전 점화는 파열 프레임의 몫이다.
    void StageSeed(Sequence _seq, float _at)
    {
        if (this.rays == null || this.m_rayScales == null) return;

        float t_dur = Mathf.Max(0.05f, this.gradeSeedRise);

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            Graphic t_ray = this.rays[t_i];
            if (t_ray == null) continue;

            RectTransform t_rt   = t_ray.rectTransform;
            Vector3       t_seed = Vector3.Scale(this.m_rayScales[t_i], new Vector3(1f, this.igniteSeedLength, 1f));
            float         t_a    = Mathf.Min(Mathf.Clamp01(this.gradeSeedAlpha), this.m_rayAlphas[t_i]);

            _seq.InsertCallback(_at, () =>
            {
                t_rt.localScale = t_seed;
                SetAlpha(t_ray, 0f);
            });

            _seq.Insert(_at, t_ray.DOFade(t_a, t_dur).SetEase(Ease.InQuad));
        }
    }

    // 옛 배지가 튀며 사라진다. 새 배지가 꽂히는 것과 같은 구간이라 둘이 한 번의 교체로 읽힌다.
    void StageShatter(Sequence _seq, float _at)
    {
        if (!this.m_hasFrom) return;

        RectTransform t_rect = this.fromBadgeImage.rectTransform;
        float         t_dur  = Mathf.Max(0.02f, this.gradeShatterDuration);

        // 진동이 방금 끝난 자리라 저작 좌표로 한 번 찍고 시작한다(중단으로 끊긴 진동이 자리를 남겨 둘 수 있다).
        _seq.InsertCallback(_at, () => t_rect.anchoredPosition = this.m_fromBadgePos);

        _seq.Insert(_at, t_rect.DOScale(this.m_fromBadgeScale * Mathf.Max(1f, this.gradeShatterScale), t_dur)
                               .SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.fromBadgeImage.DOFade(0f, t_dur).SetEase(Ease.OutQuad));
    }

    // 등급명이 아래에서 떠오른다. 등급이 갈린 판에서만 뜨는 표식이라 단계 상승에는 없다.
    void StageGradeName(Sequence _seq, float _at)
    {
        if (this.gradeNameText == null) return;

        RectTransform t_rect = this.gradeNameText.rectTransform;
        float         t_dur  = Mathf.Max(0.01f, this.gradeNameFade);

        _seq.InsertCallback(_at, () =>
        {
            t_rect.anchoredPosition = this.m_gradeNamePos - new Vector2(0f, this.gradeNameRise);
            SetAlpha(this.gradeNameText, 0f);
        });

        _seq.Insert(_at, t_rect.DOAnchorPos(this.m_gradeNamePos, t_dur).SetEase(Ease.OutCubic));
        _seq.Insert(_at, this.gradeNameText.DOFade(this.m_gradeNameAlpha, t_dur).SetEase(Ease.OutQuad));
    }

    // 섬광 · 링 · 킥 · 배지 눌림. 넷이 같은 시각이라 하나의 타격으로 읽힌다.
    void StageImpact(Sequence _seq, float _at, RectTransform _badge)
    {
        if (ScreenFlash.TryGet(out ScreenFlash t_flash))
        {
            Sequence t_cover = t_flash.BuildCover(this.flash);
            if (t_cover != null) _seq.Insert(_at, t_cover);
        }

        if (this.burstRing != null)
        {
            RectTransform t_ring = this.burstRing.rectTransform;
            float         t_dur  = Mathf.Max(0.05f, this.ringDuration);

            _seq.InsertCallback(_at, () =>
            {
                t_ring.localScale = this.m_ringScale * this.ringFromScale;
                SetAlpha(this.burstRing, this.m_ringAlpha);
            });

            _seq.Insert(_at, t_ring.DOScale(this.m_ringScale * this.ringToScale, t_dur).SetEase(Ease.OutQuad));
            _seq.Insert(_at, this.burstRing.DOFade(0f, t_dur).SetEase(Ease.OutQuad));
        }

        RectTransform t_kick = ResolveKickRoot();
        if (t_kick != null)
            _seq.InsertCallback(_at, () => UiPunch.Play(t_kick, this.kickPunch, this.kickDuration));

        if (_badge == null) return;

        // 부호가 음수라 부푸는 것이 아니라 눌린다.
        _seq.Insert(_at, _badge.DOPunchScale(Vector3.one * -this.badgeSquash, this.badgeSquashDuration,
                                             vibrato: 2, elasticity: 0.8f));
    }

    // 씨앗 길이에서 저작 길이까지 뻗으며 과다 노출됐다가 저작 알파로 정착한다(RankPromoStandby와 같은 문법).
    void StageRays(Sequence _seq, float _at)
    {
        if (this.rays == null || this.m_rayScales == null) return;

        float t_flare = Mathf.Max(0.05f, this.igniteDuration) * 0.35f;

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            Graphic t_ray = this.rays[t_i];
            if (t_ray == null) continue;

            RectTransform t_rt   = t_ray.rectTransform;
            Vector3       t_lit  = this.m_rayScales[t_i];
            Vector3       t_seed = Vector3.Scale(t_lit, new Vector3(1f, this.igniteSeedLength, 1f));
            float         t_a    = this.m_rayAlphas[t_i];

            _seq.InsertCallback(_at, () =>
            {
                t_rt.localScale = t_seed;
                SetAlpha(t_ray, 0f);
            });

            _seq.Insert(_at, t_rt.DOScale(t_lit, Mathf.Max(0.05f, this.igniteDuration)).SetEase(Ease.OutCubic));
            _seq.Insert(_at, t_ray.DOFade(this.igniteOverAlpha, t_flare).SetEase(Ease.OutQuad));
            _seq.Insert(_at + t_flare, t_ray.DOFade(t_a, Mathf.Max(0.05f, this.igniteSettle)).SetEase(Ease.InQuad));
        }
    }

    // 여운으로 넘어가는 자리. 호흡은 눌림이 끝난 뒤여야 같은 배율을 두 트윈이 밀지 않는다.
    void StageAfterglow(Sequence _seq, float _at)
    {
        _seq.InsertCallback(_at, StartSpin);
        _seq.InsertCallback(_at + Mathf.Max(0f, this.badgeSquashDuration), StartBreathe);
    }

    void StageHint(Sequence _seq, float _at)
    {
        if (this.hintGroup == null) return;

        _seq.Insert(_at, this.hintGroup.DOFade(this.m_hintAlpha, Mathf.Max(0.01f, this.hintFade)));
    }

    // 안무 직전 상태. 암전 말고는 아무것도 화면에 없어야 한다.
    void PrimeChoreography()
    {
        // 저작값을 손대기 전에 잡아 둔다 — 자세를 되돌리는 자리가 원본을 모르면 0으로 밀어버린다.
        Capture();
        KillLoops();

        if (this.contentGroup != null) this.contentGroup.alpha = 0f;

        if (this.badgeImage != null)
        {
            RectTransform t_badge = this.badgeImage.rectTransform;
            t_badge.DOKill();
            t_badge.localScale = Vector3.one * Mathf.Max(0.01f, this.slamFromScale);

            // 등급 승급은 옛 배지가 먼저 서느라 내용이 일찍 켜진다 — 새 배지는 알파로 숨겨 둔다.
            SetAlpha(this.badgeImage, 0f);
        }

        RestoreFromBadge();
        RestoreTierName();
        RestoreGradeName(0f);

        if (this.burstRing != null)
        {
            this.burstRing.DOKill();
            this.burstRing.rectTransform.localScale = this.m_ringScale;
            SetAlpha(this.burstRing, 0f);
        }

        if (this.rays != null && this.m_rayScales != null)
            for (int t_i = 0; t_i < this.rays.Length; t_i++)
            {
                if (this.rays[t_i] == null) continue;

                this.rays[t_i].DOKill();
                ApplyRayPose(t_i);
                SetAlpha(this.rays[t_i], 0f);
            }

        if (this.hintGroup != null)
        {
            this.hintGroup.DOKill();
            this.hintGroup.alpha = 0f;
        }

        RectTransform t_kick = ResolveKickRoot();
        if (t_kick == null) return;

        t_kick.DOKill();
        t_kick.localScale = Vector3.one;
    }

    // 최종 상태(= 안무가 끝난 자세)로 점프한다. 건너뛰기·닫기·비활성 어디로 나가도 중간값이 남지 않게.
    // 저작 상태와 같지 않은 축이 둘 있다 — 링은 이미 터져 사라진 뒤고, 옛 배지는 파열한 뒤다.
    void ResetChoreography()
    {
        // Show를 한 번도 거치지 않고 여기로 오는 길이 있다(부모 비활성). 원본을 모르는 채 자세를 되돌리면 0으로 민다.
        Capture();
        KillLoops();

        if (this.contentGroup != null) this.contentGroup.alpha = 1f;

        if (this.badgeImage != null)
        {
            RectTransform t_badge = this.badgeImage.rectTransform;
            t_badge.DOKill();
            t_badge.localScale = Vector3.one;
            SetAlpha(this.badgeImage, this.m_badgeAlpha);
        }

        // 옛 배지는 파열한 뒤가 최종 상태다.
        RestoreFromBadge();

        RestoreTierName();

        // 이름은 새 티어로 끝난다. 아직 한 번도 세운 적 없는 판(m_toName이 빈 값)이면 저작 문구를 그대로 둔다.
        if (this.tierNameText != null && !string.IsNullOrEmpty(this.m_toName)) this.tierNameText.text = this.m_toName;

        RestoreGradeName(this.m_gradeNameAlpha);

        // 링은 이미 터져 사라진 뒤가 최종 상태다 — 저작 알파로 되돌리면 정지한 링이 화면에 남는다.
        if (this.burstRing != null)
        {
            this.burstRing.DOKill();
            this.burstRing.rectTransform.localScale = this.m_ringScale;
            SetAlpha(this.burstRing, 0f);
        }

        if (this.rays != null && this.m_rayScales != null)
            for (int t_i = 0; t_i < this.rays.Length; t_i++)
            {
                if (this.rays[t_i] == null) continue;

                this.rays[t_i].DOKill();
                ApplyRayPose(t_i);
                SetAlpha(this.rays[t_i], this.m_rayAlphas[t_i]);
            }

        if (this.hintGroup != null)
        {
            this.hintGroup.DOKill();
            this.hintGroup.alpha = this.m_hintAlpha;
        }

        RectTransform t_kick = ResolveKickRoot();
        if (t_kick == null) return;

        t_kick.DOKill();
        t_kick.localScale = Vector3.one;
    }

    // 저작 자세·알파·색을 1회 캡처한다. 첫 Show가 값을 밀기 전이어야 원본이 잡힌다.
    // **색까지 잡는 이유**: 등급색이 섬광·링·광선의 RGB를 덮으므로, 저작색을 잃으면 단계 상승이 흰 섬광으로 못 돌아온다.
    void Capture()
    {
        if (this.m_captured) return;
        this.m_captured = true;

        int t_count = this.rays != null ? this.rays.Length : 0;

        this.m_rayScales = new Vector3[t_count];
        this.m_rayEulers = new Vector3[t_count];
        this.m_rayAlphas = new float[t_count];

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            if (this.rays[t_i] == null) continue;

            RectTransform t_rt = this.rays[t_i].rectTransform;

            this.m_rayScales[t_i] = t_rt.localScale;
            this.m_rayEulers[t_i] = t_rt.localEulerAngles;
            this.m_rayAlphas[t_i] = this.rays[t_i].color.a;
        }

        if (this.burstRing != null)
        {
            this.m_ringScale = this.burstRing.rectTransform.localScale;
            this.m_ringAlpha = this.burstRing.color.a;
        }

        this.m_flashColor = this.flash.color;

        if (this.hintGroup != null) this.m_hintAlpha = this.hintGroup.alpha;

        if (this.badgeImage != null) this.m_badgeAlpha = this.badgeImage.color.a;

        if (this.tierNameText != null) this.m_tierNameScale = this.tierNameText.rectTransform.localScale;

        if (this.fromBadgeImage != null)
        {
            RectTransform t_rt = this.fromBadgeImage.rectTransform;

            this.m_fromBadgePos   = t_rt.anchoredPosition;
            this.m_fromBadgeScale = t_rt.localScale;
            this.m_fromBadgeAlpha = this.fromBadgeImage.color.a;
        }

        if (this.gradeNameText != null)
        {
            this.m_gradeNamePos   = this.gradeNameText.rectTransform.anchoredPosition;
            this.m_gradeNameAlpha = this.gradeNameText.color.a;
        }
    }

    // 옛 배지를 저작 자리·배율로 되돌리고 감춘다(등장 전 = 파열 후).
    void RestoreFromBadge()
    {
        if (this.fromBadgeImage == null) return;

        RectTransform t_rt = this.fromBadgeImage.rectTransform;

        t_rt.DOKill();
        this.fromBadgeImage.DOKill();

        t_rt.anchoredPosition = this.m_fromBadgePos;
        t_rt.localScale       = this.m_fromBadgeScale;
        SetAlpha(this.fromBadgeImage, 0f);
    }

    // 티어명 배율을 저작값으로 되돌린다(문구는 부르는 쪽이 정한다 — 교체 전후가 갈린다).
    void RestoreTierName()
    {
        if (this.tierNameText == null) return;

        RectTransform t_rt = this.tierNameText.rectTransform;

        t_rt.DOKill();
        t_rt.localScale = this.m_tierNameScale;
    }

    // 등급명을 저작 자리로 되돌리고 알파만 지정받는다(단계 상승은 0, 등급 승급은 저작값).
    void RestoreGradeName(float _alpha)
    {
        if (this.gradeNameText == null) return;

        RectTransform t_rt = this.gradeNameText.rectTransform;

        t_rt.DOKill();
        this.gradeNameText.DOKill();

        t_rt.anchoredPosition = this.m_gradeNamePos;
        SetAlpha(this.gradeNameText, _alpha);
    }

    // 갈래별 문구. 비어 있으면 프리팹 저작 문구를 그대로 둔다 — 빈 문자열로 지우면 화면에서 제목이 사라진다.
    string TitleOf(EPromoteKind _kind)
    {
        string t_title = _kind == EPromoteKind.GradeUp ? this.titleGradeUp : this.titleFirstEntry;

        if (!string.IsNullOrEmpty(t_title)) return t_title;

        return this.titleText != null ? this.titleText.text : string.Empty;
    }

    // 등급색을 섬광·링·광선에 싣는다. RGB만 갈아끼우고 알파는 저작값 그대로 — 알파는 안무가 미는 축이다.
    void ApplyGradeTint(ERankGrade _grade)
    {
        Color t_rgb = GradeColorOf(_grade);

        this.flash.color = WithAlpha(t_rgb, this.m_flashColor.a);

        if (this.burstRing != null) this.burstRing.color = WithAlpha(t_rgb, this.m_ringAlpha);

        if (this.rays == null || this.m_rayAlphas == null) return;

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            if (this.rays[t_i] == null) continue;

            this.rays[t_i].color = WithAlpha(t_rgb, this.m_rayAlphas[t_i]);
        }
    }

    // 도달 등급의 색. 팔레트가 비었거나 짧으면 흰색으로 떨어진다 — 색이 없다고 연출을 멈추지 않는다.
    Color GradeColorOf(ERankGrade _grade)
    {
        int t_i = (int)_grade;

        if (this.gradeColors == null || t_i < 0 || t_i >= this.gradeColors.Length) return Color.white;

        return this.gradeColors[t_i];
    }

    // 광선 뭉치가 천천히 돈다. 저작 각도에서 절대값으로 목표를 잡는다 — 상대 회전은 반복할수록 밀린다.
    void StartSpin()
    {
        if (this.rays == null || this.m_rayEulers == null) return;

        KillSpins();
        this.m_spins = new Tween[this.rays.Length];

        float t_dur = Mathf.Max(0.1f, this.spinDuration);

        for (int t_i = 0; t_i < this.rays.Length; t_i++)
        {
            Graphic t_ray = this.rays[t_i];
            if (t_ray == null) continue;

            Vector3 t_to = this.m_rayEulers[t_i] + new Vector3(0f, 0f, this.spinDegrees);

            this.m_spins[t_i] = t_ray.rectTransform
                                     .DOLocalRotate(t_to, t_dur, RotateMode.FastBeyond360)
                                     .SetEase(Ease.Linear)
                                     .SetLoops(-1, LoopType.Restart)
                                     .SetLink(t_ray.gameObject);
        }
    }

    // 배지 호흡. 끝이 없으므로 참조를 들고 있다가 정리에서 걷는다.
    void StartBreathe()
    {
        if (this.badgeImage == null) return;

        KillBreathe();

        RectTransform t_badge = this.badgeImage.rectTransform;

        t_badge.localScale = Vector3.one;
        this.m_breathe = t_badge.DOScale(this.breatheScale, Mathf.Max(0.1f, this.breatheDuration))
                                .SetEase(Ease.InOutSine)
                                .SetLoops(-1, LoopType.Yoyo)
                                .SetLink(t_badge.gameObject);
    }

    void KillChoreo()
    {
        if (this.m_choreo != null && this.m_choreo.IsActive()) this.m_choreo.Kill();
        this.m_choreo = null;
    }

    void KillLoops()
    {
        KillSpins();
        KillBreathe();
    }

    void KillSpins()
    {
        if (this.m_spins == null) return;

        for (int t_i = 0; t_i < this.m_spins.Length; t_i++)
            if (this.m_spins[t_i] != null) this.m_spins[t_i].Kill();

        this.m_spins = null;
    }

    void KillBreathe()
    {
        if (this.m_breathe == null) return;

        this.m_breathe.Kill();
        this.m_breathe = null;
    }

    void ApplyRayPose(int _index)
    {
        RectTransform t_rt = this.rays[_index].rectTransform;

        t_rt.localScale       = this.m_rayScales[_index];
        t_rt.localEulerAngles = this.m_rayEulers[_index];
    }

    void SetInputEnabled(bool _enabled)
    {
        if (this.tapButton != null) this.tapButton.interactable = _enabled;
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : gameObject;

    RectTransform ResolveKickRoot()
        => this.kickRoot != null
            ? this.kickRoot
            : this.contentGroup != null ? this.contentGroup.transform as RectTransform : null;

    static void SetAlpha(Graphic _g, float _a)
    {
        if (_g == null) return;

        Color t_c = _g.color;
        t_c.a    = _a;
        _g.color = t_c;
    }

    static Color WithAlpha(Color _color, float _alpha)
    {
        _color.a = _alpha;
        return _color;
    }
}
