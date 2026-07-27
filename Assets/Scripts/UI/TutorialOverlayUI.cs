using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 튜토리얼 인게임 오버레이 — 스크립트 스텝 순서를 따라 안내 문구를 순차로 띄운다.
/// **연출/입력 게이트 전용**: 전투 규칙/RNG/결정 지점 무접촉. 스텝 소비의 단일 진실원은
/// <see cref="TutorialConfig"/> 큐 — 이 UI는 표시와 탭 대기만 한다.
/// 싱글 튜토리얼(<see cref="TutorialConfig.IsActive"/>)에서만 <see cref="Ensure"/>로 생성.
///
/// 기능:
/// - 순차 배너: 스텝마다 문구 교체(fade+pop).
/// - 탭 진행: 마스크 활성 구간에서 화면 탭으로 다음 진행(<see cref="WaitForTapAsync"/>).
/// - 입력 마스크: 마스크는 uGUI raycast를 흡수한다. **주의: 카드 입력은 Physics2D
///   `OnMouseDown`이라 uGUI 마스크로 차단되지 않는다** — 실제 카드 입력 차단은 호출측이
///   `TurnState.InputAllowed=false`로 하며, 마스크는 (1)탭 감지 (2)배경 어둡게(darken) 담당.
///   darken 여부(<see cref="ScriptedAttack.dimBackground"/>)와 무관하게 마스크는 탭을 받는다.
/// - 포인터: 공격 스텝에서 공격자 카드에 hintArrow(드래그 방향) 표시 + 양측 슬롯 하이라이트.
/// 캔버스는 코드 빌드(프리팹 불필요, TutorialSetupUI 선례).
/// </summary>
public class TutorialOverlayUI : MonoBehaviour
{
    public static TutorialOverlayUI Instance { get; private set; }

    TextMeshProUGUI banner;
    CanvasGroup     bannerGroup;
    Image           dimMask;       // 풀스크린 어둡게 + 입력 차단 + 탭 감지
    CanvasGroup     dimGroup;

    bool tapped;      // dim 활성 중 탭(release) 발생 플래그(WaitForTapAsync가 소비)
    bool tapArmed;    // 탭 대기 활성 구간(이 구간에서 시작된 press만 인정)
    bool pressActive; // 이번 대기 구간 안에서 pointer down이 발생했는가
    bool inspected; // Inspect 대기 중 적 카드 롱프레스 발생 플래그(WaitForInspectAsync가 소비)

    // 직전 스텝에서 켠 하이라이트/포인터(교체/정리 시 되돌리기 위해 추적).
    CardView highlightedAttacker;
    CardView highlightedTarget;
    CardView pointerCard;

    /// <summary>튜토리얼 활성 시 오버레이를 1회 생성. 이미 있으면 재사용.</summary>
    public static TutorialOverlayUI Ensure()
    {
        if (Instance != null) return Instance;
        var t_go = new GameObject("TutorialOverlay");
        return Instance = t_go.AddComponent<TutorialOverlayUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── 표시 API ────────────────────────────────────────────────────────────

    /// <summary>
    /// 설명/게이트 스텝. 배너 + 탭 대기용 마스크 활성(탭 감지). darken이면 배경 어둡게,
    /// 아니면 투명하되 탭은 여전히 받는다. 하이라이트/포인터 없음.
    /// </summary>
    public void ShowMessage(string _message, bool _darken)
    {
        ClearHighlight();
        ClearPointer();
        ActivateMask(_darken);
        SetBanner(_message);
    }

    /// <summary>
    /// 공격 스텝 안내. 배너 + 공격자/타깃 슬롯 하이라이트 + (showPointer면) 공격자에 드래그 포인터.
    /// 마스크는 끈다(카드 드래그를 받아야 하므로). attacker/target 슬롯 뷰는 null 허용.
    /// </summary>
    public void ShowAttack(string _message, CardView _attacker, CardView _target, bool _showPointer)
    {
        ClearHighlight();
        ClearPointer();

        this.highlightedAttacker = _attacker;
        this.highlightedTarget   = _target;
        _attacker?.SetHighlight(true);
        _target?.SetHighlight(true);

        if (_showPointer && _attacker != null)
        {
            this.pointerCard = _attacker;
            _attacker.SetTutorialPointer(true);
        }

        DeactivateMask();
        SetBanner(_message);
    }

    /// <summary>dim 마스크가 활성인 동안 화면 탭을 대기. **손을 뗄 때(release)** 완료된다.
    /// 단, 이 대기 구간 안에서 새로 시작된 press의 release만 인정한다 — 직전 스텝/롱프레스에서부터
    /// 이어져 눌려 있던 손가락은 무시해 다이얼로그 즉시 스킵을 막는다. ct로 씬 파괴 취소.</summary>
    public async UniTask WaitForTapAsync(CancellationToken _ct)
    {
        this.tapped      = false;
        this.pressActive = false;
        this.tapArmed    = true;
        try
        {
            await UniTask.WaitUntil(() => this.tapped, cancellationToken: _ct);
        }
        finally
        {
            this.tapArmed    = false;
            this.tapped      = false;
            this.pressActive = false;
        }
    }

    /// <summary>
    /// Inspect 안내. 배너만 띄우고 마스크는 끈다(적 카드 롱프레스를 받아야 하므로).
    /// 카드 입력은 Physics2D라 마스크와 무관 — 마스크를 켜면 어두워지기만 하므로 여기선 끈다.
    /// </summary>
    public void ShowInspect(string _message)
    {
        ClearHighlight();
        ClearPointer();
        DeactivateMask();
        SetBanner(_message);
    }

    /// <summary>Inspect 대기 중 적 카드 롱프레스 발생을 통지. 대기 중일 때만 의미 있다.</summary>
    public void NotifyInspected() => this.inspected = true;

    /// <summary>적 카드 롱프레스(정보확인)를 대기. 발생하면 완료. ct로 씬 파괴 취소.</summary>
    public async UniTask WaitForInspectAsync(CancellationToken _ct)
    {
        this.inspected = false;
        await UniTask.WaitUntil(() => this.inspected, cancellationToken: _ct);
        this.inspected = false;
    }

    /// <summary>배너 숨기고 마스크 끄고 하이라이트/포인터 되돌림(턴 종료/정리).</summary>
    public void Clear()
    {
        ClearHighlight();
        ClearPointer();
        DeactivateMask();
        HideBanner();
    }

    // ── 내부 ────────────────────────────────────────────────────────────────

    void SetBanner(string _message)
    {
        if (string.IsNullOrEmpty(_message)) { HideBanner(); return; }

        this.banner.text = _message;
        this.bannerGroup.DOKill();
        this.banner.transform.DOKill();
        this.bannerGroup.alpha = 0f;
        this.banner.transform.localScale = Vector3.one * 0.92f;
        this.bannerGroup.DOFade(1f, 0.2f).SetLink(this.bannerGroup.gameObject);
        this.banner.transform.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetLink(this.banner.gameObject);
    }

    void HideBanner()
    {
        if (this.bannerGroup == null) return;
        this.bannerGroup.DOKill();
        this.bannerGroup.DOFade(0f, 0.15f).SetLink(this.bannerGroup.gameObject);
    }

    // 마스크 활성: 탭 감지(raycast on) + darken이면 어둡게, 아니면 투명(그래도 탭 받음).
    void ActivateMask(bool _darken)
    {
        if (this.dimMask == null) return;
        this.dimMask.raycastTarget   = true;
        this.dimGroup.blocksRaycasts = true;
        this.dimGroup.DOKill();
        this.dimGroup.DOFade(_darken ? 1f : 0f, 0.15f).SetLink(this.dimGroup.gameObject);
    }

    void DeactivateMask()
    {
        if (this.dimMask == null) return;
        this.dimMask.raycastTarget   = false;
        this.dimGroup.blocksRaycasts = false;
        this.dimGroup.DOKill();
        this.dimGroup.DOFade(0f, 0.15f).SetLink(this.dimGroup.gameObject);
    }

    // pointer down: 대기 중이면 "이번 구간에서 시작된 press"로 표시.
    void OnMaskDown()
    {
        if (this.tapArmed) this.pressActive = true;
    }

    // pointer up: 이번 구간에서 시작된 press의 release만 진행으로 인정.
    void OnMaskUp()
    {
        if (this.tapArmed && this.pressActive) this.tapped = true;
        this.pressActive = false;
    }

    void ClearHighlight()
    {
        this.highlightedAttacker?.SetHighlight(false);
        this.highlightedTarget?.SetHighlight(false);
        this.highlightedAttacker = null;
        this.highlightedTarget   = null;
    }

    void ClearPointer()
    {
        this.pointerCard?.SetTutorialPointer(false);
        this.pointerCard = null;
    }

    // ── 코드 빌드 캔버스 ──────────────────────────────────────────────────────
    void BuildUI()
    {
        var t_canvasGo = new GameObject("TutorialOverlayCanvas");
        t_canvasGo.transform.SetParent(transform, false);
        var t_canvas = t_canvasGo.AddComponent<Canvas>();
        t_canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        t_canvas.sortingOrder = 200;   // 전투 UI 위
        var t_scaler = t_canvasGo.AddComponent<CanvasScaler>();
        t_scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        t_scaler.referenceResolution = new Vector2(1080f, 1920f);
        t_canvasGo.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        // dim 마스크(풀스크린, 탭 감지 버튼) — 기본 비활성(raycast off).
        var t_dimGo = new GameObject("DimMask");
        t_dimGo.transform.SetParent(t_canvasGo.transform, false);
        this.dimGroup = t_dimGo.AddComponent<CanvasGroup>();
        this.dimGroup.alpha = 0f;
        this.dimGroup.blocksRaycasts = false;
        this.dimMask = t_dimGo.AddComponent<Image>();
        this.dimMask.color = new Color(0f, 0f, 0f, 0.6f);
        this.dimMask.raycastTarget = false;
        var t_dimRect = this.dimMask.GetComponent<RectTransform>();
        t_dimRect.anchorMin = Vector2.zero;
        t_dimRect.anchorMax = Vector2.one;
        t_dimRect.offsetMin = t_dimRect.offsetMax = Vector2.zero;
        var t_tapCatcher = t_dimGo.AddComponent<TutorialTapCatcher>();
        t_tapCatcher.OnDown = OnMaskDown;
        t_tapCatcher.OnUp   = OnMaskUp;

        // 배너(상단). dim보다 위에 그려지도록 나중에 생성.
        var t_bgGo = new GameObject("Banner");
        t_bgGo.transform.SetParent(t_canvasGo.transform, false);
        this.bannerGroup = t_bgGo.AddComponent<CanvasGroup>();
        this.bannerGroup.blocksRaycasts = false;   // 배너는 입력 무흡수(탭은 dim이 받음)
        this.bannerGroup.interactable   = false;
        var t_bg = t_bgGo.AddComponent<Image>();
        t_bg.color = new Color(0f, 0f, 0f, 0.72f);
        t_bg.raycastTarget = false;
        var t_bgRect = t_bg.GetComponent<RectTransform>();
        t_bgRect.anchorMin = new Vector2(0.5f, 1f);
        t_bgRect.anchorMax = new Vector2(0.5f, 1f);
        t_bgRect.pivot     = new Vector2(0.5f, 1f);
        t_bgRect.anchoredPosition = new Vector2(0f, -160f);
        t_bgRect.sizeDelta        = new Vector2(960f, 220f);

        var t_txtGo = new GameObject("Text");
        t_txtGo.transform.SetParent(t_bgGo.transform, false);
        this.banner = t_txtGo.AddComponent<TextMeshProUGUI>();
        TutorialUIStyle.ApplyFont(this.banner);
        this.banner.fontSize  = 46f;
        this.banner.color     = Color.white;
        this.banner.alignment = TextAlignmentOptions.Center;
        this.banner.enableWordWrapping = true;
        this.banner.raycastTarget = false;
        var t_txtRect = this.banner.GetComponent<RectTransform>();
        t_txtRect.anchorMin = Vector2.zero;
        t_txtRect.anchorMax = Vector2.one;
        t_txtRect.offsetMin = new Vector2(32f, 20f);
        t_txtRect.offsetMax = new Vector2(-32f, -20f);

        // "탭하여 계속" 힌트(배너 하단).
        var t_hintGo = new GameObject("TapHint");
        t_hintGo.transform.SetParent(t_bgGo.transform, false);
        var t_hint = t_hintGo.AddComponent<TextMeshProUGUI>();
        TutorialUIStyle.ApplyFont(t_hint);
        t_hint.text      = "<size=70%>화면을 탭하여 계속</size>";
        t_hint.color     = new Color(1f, 1f, 1f, 0.65f);
        t_hint.alignment = TextAlignmentOptions.Center;
        t_hint.raycastTarget = false;
        var t_hintRect = t_hint.GetComponent<RectTransform>();
        t_hintRect.anchorMin = new Vector2(0f, 0f);
        t_hintRect.anchorMax = new Vector2(1f, 0f);
        t_hintRect.pivot     = new Vector2(0.5f, 1f);
        t_hintRect.anchoredPosition = new Vector2(0f, -6f);
        t_hintRect.sizeDelta        = new Vector2(0f, 44f);

        this.bannerGroup.alpha = 0f;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var t_es = new GameObject("EventSystem");
        t_es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        t_es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }
}
