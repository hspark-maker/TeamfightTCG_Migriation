using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 전투 이펙트 확인용 디버그 창(테스트 씬 전용). AttackTestScene에서 F1로 열고 버튼으로 연출만 재생한다.
///
/// 왜 IMGUI(OnGUI)인가: 테스트 도구가 Canvas·프리팹 배선을 늘리면 프로덕션 씬과 배선이 갈라지고
/// 손볼 곳이 두 배가 된다. 여기선 코드 하나만 붙이면 끝나고, 빌드에 남아도 이 컴포넌트를
/// 씬에 두지 않으면 아무 비용이 없다.
///
/// 규약: **게임 상태를 바꾸지 않는다** — 재생하는 건 전부 연출(BattleVfx/HealVfx/CardView 연출 메서드)이고
/// 체력·시너지·RNG를 건드리지 않는다. 그래서 반복 재생해도 테스트 씬 상태가 망가지지 않는다.
/// 예외는 GameTiming.Speed(전역 배속)뿐 — 연출을 느리게 돌려 보기 위한 것이고 씬을 나가면 끝난다.
/// </summary>
public class VfxDebugWindow : MonoBehaviour
{
    [Header("Field Views (비우면 씬에서 자동 탐색)")]
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;

    [Header("라이브러리 (비우면 이미 주입된 것 사용)")]
    // 테스트 씬은 GameInitializer/DataLibrary 초기화이 없을 수 있다 → 그때 여기 값으로 직접 주입.
    // 이미 주입돼 있으면 건드리지 않는다.
    [SerializeField] BattleVfxLibrary library;

    [Header("Window")]
    [SerializeField] KeyCode toggleKey = KeyCode.F1;
    [SerializeField] bool    openOnStart = true;

    // IMGUI는 픽셀 단위라 고해상도에서 글자·버튼이 그대로 작아진다 → GUI.matrix를 화면 높이 비율로 스케일.
    // 창 좌표/크기는 아래 REF_HEIGHT 기준의 "논리 픽셀"로 다루고, 실제 픽셀 변환은 OnGUI가 한 곳에서 처리.
    const float REF_HEIGHT = 1080f;
    [Tooltip("해상도 비례 크기에 추가로 곱하는 배율. 화면이 1080p일 때 1 = 논리 픽셀 그대로.")]
    [SerializeField] float uiScale = 1f;

    Rect   windowRect = new Rect(16f, 16f, 420f, 700f);
    bool   open;
    int    sourceSlot;      // 발사/발동 주체(플레이어 필드 슬롯)
    int    targetSlot;      // 대상(적 필드 슬롯)
    int    healAmount = 1;
    Vector2 scroll;

    void Start()
    {
        this.open = this.openOnStart;

        if (this.playerFieldView == null || this.enemyFieldView == null) AutoFindFields();
        if (!BattleVfx.HasLibrary && this.library != null) BattleVfx.SetLibrary(this.library);
    }

    /// <summary>필드 뷰 미배선 시 씬에서 찾는다. owner 0=플레이어 기준으로 갈라 담고,
    /// 판정 불가(카드 미바인딩)면 찾은 순서대로 채운다 — 테스트 도구라 최선 추정으로 충분하다.</summary>
    void AutoFindFields()
    {
        var t_found = FindObjectsByType<BattleFieldView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t_fv in t_found)
        {
            bool t_isPlayer = t_fv.Field != null
                ? t_fv.Field.OwnerIndex == TurnState.LocalOwnerIndex
                : this.playerFieldView == null;

            if (t_isPlayer && this.playerFieldView == null) this.playerFieldView = t_fv;
            else if (this.enemyFieldView == null)           this.enemyFieldView  = t_fv;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(this.toggleKey)) this.open = !this.open;
    }

    /// <summary>화면 높이 비례 스케일. 세로 1080 기준으로 커지고 작아진다(4K에서 약 2배).
    /// 입력 좌표도 GUI.matrix가 같이 변환하므로 클릭 판정이 그림과 어긋나지 않는다.</summary>
    float Scale => Mathf.Max(0.4f, Screen.height / REF_HEIGHT * Mathf.Max(0.2f, this.uiScale));

    void OnGUI()
    {
        if (!this.open) return;

        Matrix4x4 t_prev = GUI.matrix;
        float t_scale = Scale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(t_scale, t_scale, 1f));

        // 논리 화면(스케일 적용 후 좌표계) 안에 창을 가둔다 — 스케일이 커지면 창이 화면 밖으로 밀려 못 잡는다.
        float t_logicalW = Screen.width  / t_scale;
        float t_logicalH = Screen.height / t_scale;
        this.windowRect.width  = Mathf.Min(this.windowRect.width,  t_logicalW - 8f);
        this.windowRect.height = Mathf.Min(this.windowRect.height, t_logicalH - 8f);
        this.windowRect.x = Mathf.Clamp(this.windowRect.x, 0f, Mathf.Max(0f, t_logicalW - this.windowRect.width));
        this.windowRect.y = Mathf.Clamp(this.windowRect.y, 0f, Mathf.Max(0f, t_logicalH - this.windowRect.height));

        this.windowRect = GUI.Window(GetInstanceID(), this.windowRect, DrawWindow, "VFX Debug  (" + this.toggleKey + ")");

        GUI.matrix = t_prev;
    }

    void DrawWindow(int _id)
    {
        this.scroll = GUILayout.BeginScrollView(this.scroll);

        DrawStatus();
        GUILayout.Space(6f);
        DrawUiScale();
        GUILayout.Space(6f);
        DrawSlotPickers();
        GUILayout.Space(6f);
        DrawSpeed();
        GUILayout.Space(6f);
        DrawLibrarySection();
        GUILayout.Space(6f);
        DrawHealSection();
        GUILayout.Space(6f);
        DrawCardFxSection();

        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
    }

    // ── 상태 ─────────────────────────────────────────────────────────────

    void DrawStatus()
    {
        GUILayout.Label("Library: " + (BattleVfx.Library != null ? BattleVfx.Library.name : "<없음 — 연출 전부 무동작>"));
        GUILayout.Label("Fields: P=" + Name(this.playerFieldView) + " / E=" + Name(this.enemyFieldView));
        if (Source() == null || Target() == null)
            GUILayout.Label("슬롯 뷰를 못 찾음 — 필드 뷰 배선/슬롯 번호 확인");
    }

    static string Name(BattleFieldView _fv) => _fv != null ? _fv.name : "null";

    /// <summary>해상도 비례 스케일 위에 손으로 더 키우고 줄이는 배율. 창 크기도 같이 조절(내용이 잘리면 늘린다).</summary>
    void DrawUiScale()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("UI x" + this.uiScale.ToString("0.0") + " (실제 x" + Scale.ToString("0.00") + ")");
        if (GUILayout.Button("-", GUILayout.Width(28f))) this.uiScale = Mathf.Max(0.4f, this.uiScale - 0.2f);
        if (GUILayout.Button("+", GUILayout.Width(28f))) this.uiScale = Mathf.Min(4f,   this.uiScale + 0.2f);
        if (GUILayout.Button("1x", GUILayout.Width(34f))) this.uiScale = 1f;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("창 크기", GUILayout.Width(60f));
        if (GUILayout.Button("좁게")) this.windowRect.width  = 360f;
        if (GUILayout.Button("넓게")) this.windowRect.width  = 560f;
        if (GUILayout.Button("길게")) this.windowRect.height = 900f;
        GUILayout.EndHorizontal();
    }

    CardView Source() => Slot(this.playerFieldView, this.sourceSlot);
    CardView Target() => Slot(this.enemyFieldView,  this.targetSlot);

    static CardView Slot(BattleFieldView _fv, int _index)
    {
        if (_fv == null || _index < 0 || _index >= BattleField.SLOT_COUNT) return null;
        return _fv.GetSlotView(_index);
    }

    // ── 슬롯 선택 ────────────────────────────────────────────────────────

    void DrawSlotPickers()
    {
        this.sourceSlot = SlotRow("Source (내 필드)", this.sourceSlot);
        this.targetSlot = SlotRow("Target (적 필드)", this.targetSlot);
    }

    static int SlotRow(string _label, int _value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(_label, GUILayout.Width(120f));
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
            if (GUILayout.Toggle(_value == i, i.ToString(), GUI.skin.button)) _value = i;
        GUILayout.EndHorizontal();
        return _value;
    }

    // ── 배속 ─────────────────────────────────────────────────────────────

    void DrawSpeed()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Speed " + GameTiming.Speed.ToString("0.00"), GUILayout.Width(120f));
        // 배속은 GameTiming 단일 진실원을 그대로 쓴다(여기서 자체 배율을 만들면 인게임과 어긋난다).
        GameTiming.Speed = GUILayout.HorizontalSlider(GameTiming.Speed, 0.2f, 3f);
        if (GUILayout.Button("1x", GUILayout.Width(34f))) GameTiming.Speed = 1f;
        GUILayout.EndHorizontal();
    }

    // ── 라이브러리 연출 ──────────────────────────────────────────────────

    void DrawLibrarySection()
    {
        GUILayout.Label("── BattleVfxLibrary ──");

        foreach (BattleVfxId t_id in System.Enum.GetValues(typeof(BattleVfxId)))
        {
            if (t_id == BattleVfxId.None) continue;

            bool t_has = BattleVfx.TryGetEntry(t_id, out _);

            GUILayout.BeginHorizontal();
            GUILayout.Label((t_has ? "" : "(미배선) ") + t_id, GUILayout.Width(150f));

            // World = 좌표에 1회 스폰(수명은 라이브러리 값), Attach = 카드 자식으로 붙여 재생(카드가 움직이면 따라감).
            if (GUILayout.Button("World@S", GUILayout.Width(70f)) && Source() != null)
                BattleVfx.Play(t_id, Source().BottomCenter, Source().VfxSortingLayerId);

            if (GUILayout.Button("Attach@T", GUILayout.Width(75f)) && Target() != null)
                BattleVfx.PlayAttached(t_id, Target().transform, Target().IsEnemySide, Target().VfxSortingLayerId);

            GUILayout.EndHorizontal();
        }
    }

    // ── 힐러 투사체 ──────────────────────────────────────────────────────

    void DrawHealSection()
    {
        GUILayout.Label("── 힐 버스트 (HealVfx) ──");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Amount " + this.healAmount, GUILayout.Width(120f));
        this.healAmount = Mathf.RoundToInt(GUILayout.HorizontalSlider(this.healAmount, 1f, 9f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("→ 대상 1개")) PlayHealBurst(_single: true);
        if (GUILayout.Button("→ 내 필드 전체")) PlayHealBurst(_single: false);
        GUILayout.EndHorizontal();

        // 비행 커브 형태값은 라이브러리 소유 — 여기서 바꾸면 인게임과 같은 값을 만지는 것이다
        // (에디터에서 조정하면 에셋에 남는다. 그게 목적: 테스트 씬에서 맞춘 값이 그대로 인게임에 간다).
        BattleVfxLibrary t_lib = BattleVfx.Library;
        if (t_lib == null) return;

        GUILayout.BeginHorizontal();
        GUILayout.Label("Curve " + t_lib.healCurveHeight.ToString("0.00"), GUILayout.Width(120f));
        t_lib.healCurveHeight = GUILayout.HorizontalSlider(t_lib.healCurveHeight, 0f, 3f);
        GUILayout.EndHorizontal();

        t_lib.healAlternateCurve = GUILayout.Toggle(t_lib.healAlternateCurve, "커브 방향 교차(부채꼴)");
    }

    /// <summary>힐 투사체 버스트. 대상은 **내 필드** 카드들(힐러 = 아군 회복이라 실제 경로와 같은 구도).
    /// _single이면 Target 슬롯 번호에 해당하는 내 필드 카드 하나만.</summary>
    void PlayHealBurst(bool _single)
    {
        CardView t_src = Source();
        if (t_src == null) return;

        var t_targets = new List<(CardView view, CardInstance card, int amount)>();
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            if (_single && i != this.targetSlot) continue;
            if (i == this.sourceSlot && !_single) continue;   // 힐러 자신은 대상 아님(인게임 규칙과 동일)

            CardView t_view = Slot(this.playerFieldView, i);
            if (t_view != null && t_view.BoundCard != null)
                t_targets.Add((t_view, t_view.BoundCard, this.healAmount));
        }

        HealVfx.PlayHealBurst(t_src, t_targets);
    }

    // ── 카드 자체 연출 ───────────────────────────────────────────────────

    void DrawCardFxSection()
    {
        GUILayout.Label("── 카드 연출 ──");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Heal VFX @T") && Target() != null) Target().PlayHealEffect(this.healAmount);
        if (GUILayout.Button("Hit @T") && Target() != null)     Target().PlayHitAnim(0.15f, this.healAmount, Source()).Forget();
        GUILayout.EndHorizontal();
    }
}
