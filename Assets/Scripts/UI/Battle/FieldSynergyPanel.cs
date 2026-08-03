using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>필드 한쪽의 **활성 시너지 목록**을 화면 모서리에 세로로 세우는 UI 패널.
///
/// 자리는 진영으로 갈린다 — 내 쪽은 **아래 가운데**, 상대 쪽은 **위 가운데**(각자 자기 필드 바깥쪽).
/// 위/아래로 갈라져 있어야 "누구 시너지인가"를 색·글자 없이 위치만으로 읽는다. 앵커·피벗·정렬은
/// <see cref="localSide"/> 하나로 코드가 정한다 — 씬에서 손으로 맞추면 두 패널이 서로 다르게 어긋난다.
///
/// **월드가 아니라 캔버스**에 있다: 필드 옆(월드)에 두면 카드·연출과 겹치고 화면 비율마다 자리가 흔들린다.
/// 화면 고정 정보는 화면 좌표에 둔다.
///
/// **상대 시너지도 그대로 보여준다**(2026-08-03 사용자 확정). 카드 배지가 뒷면 적의 소속을 가리는 것과
/// 다른 판단이다 — 필드 시너지는 판을 읽는 정보라 양쪽 다 보이는 쪽이 낫다. 정보 누출로 보고 가리지 마라.
/// **개수·티어는 표시하지 않는다** — 아이콘만. 티어는 쓰지 않는 개념이라 여기에 되살리지 마라.
///
/// 배선은 없다 — 켜질 때 스스로 등록하고, <see cref="Show"/>가 진영으로 찾아 그린다
/// (씬마다 참조를 다시 꽂지 않게. 같은 진영 패널이 둘이면 먼저 켜진 쪽이 쓰인다).
///
/// 순수 표시 — 규칙/상태 무접촉. 시너지 스냅샷(SynergyState)은 덱 확정 1회 산출이라 재계산도 없다.</summary>
[RequireComponent(typeof(RectTransform))]
public class FieldSynergyPanel : MonoBehaviour
{
    [Tooltip("체크=내 필드(아래 가운데), 해제=상대 필드(위 가운데)")]
    [SerializeField] bool localSide = true;

    [Header("겉모습 (px)")]
    [SerializeField] Vector2 iconSize = new Vector2(72f, 72f);
    [Tooltip("아이콘 사이 가로 간격")]
    [SerializeField] float   spacing  = 10f;
    [Tooltip("x=가운데에서 좌우 미세 조정(보통 0), y=화면 위/아래 변에서 띄우는 거리")]
    [SerializeField] Vector2 margin = new Vector2(0f, 24f);
    [Tooltip("이 수를 넘는 시너지는 표시하지 않는다(줄이 화면을 넘지 않게)")]
    [SerializeField] int maxBadges = 5;

    [Header("배경판")]
    [Tooltip("둥근 사각 스프라이트(9-slice 권장). 비우면 배경 없이 아이콘만 뜬다")]
    [SerializeField] Sprite backgroundSprite;
    [Tooltip("아이콘 줄 바깥으로 두는 여백(가로, 세로)")]
    [SerializeField] Vector2 backgroundPadding = new Vector2(24f, 12f);
    [SerializeField] Color backgroundColor = new Color(0f, 0f, 0f, 0.45f);
    [Tooltip("9-slice 테두리 축소 배율. 원본 border가 판 높이보다 두꺼우면 모서리가 뭉개진다 — 그때 키운다")]
    [Min(0.01f)] [SerializeField] float backgroundBorderShrink = 2f;

    static readonly List<FieldSynergyPanel> panels = new List<FieldSynergyPanel>();

    readonly List<Image> spawned = new List<Image>();
    Image background;
    SynergyState lastState;

    void OnEnable()  { panels.Add(this);    ApplyCorner(); }
    void OnDisable() => panels.Remove(this);

    /// <summary>그 진영 패널에 시너지를 그린다. 패널이 없는 씬(테스트 씬 등)에선 조용히 무동작.</summary>
    public static void Show(bool _localSide, SynergyState _state)
    {
        foreach (FieldSynergyPanel t_panel in panels)
        {
            if (t_panel == null || t_panel.localSide != _localSide) continue;
            t_panel.Refresh(_state);
            return;
        }
    }

    /// <summary>같은 스냅샷이면 아무것도 하지 않는다 — 매번 지웠다 만들면 그때마다 아이콘이 새로 뜬다.</summary>
    public void Refresh(SynergyState _state)
    {
        if (ReferenceEquals(this.lastState, _state)) return;
        this.lastState = _state;
        Rebuild(_state);
    }

    /// <summary>앵커·피벗을 진영에 맞춘다. 내 쪽은 **아래 가운데**, 상대 쪽은 **위 가운데**.
    /// 가로 중앙 정렬이라 시너지 수가 늘어도 줄이 좌우로 균등하게 자란다(모서리 기준이면 한쪽으로만 자란다).</summary>
    void ApplyCorner()
    {
        var t_rt = (RectTransform)transform;
        Vector2 t_anchor = this.localSide ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);

        t_rt.anchorMin = t_anchor;
        t_rt.anchorMax = t_anchor;
        t_rt.pivot     = t_anchor;
        // 세로 여백만 진영으로 부호가 갈린다. 가로는 중앙 기준의 미세 조정값이다(보통 0).
        t_rt.anchoredPosition = new Vector2(
            this.margin.x,
            this.localSide ? this.margin.y : -this.margin.y);
    }

    /// <summary>아이콘 줄을 덮는 배경판. 아이콘 수에 맞춰 폭이 정해지므로 줄을 만들기 직전에 잡는다
    /// (고정 크기로 두면 시너지가 하나일 때 텅 빈 판이, 넷일 때 모자란 판이 된다).
    /// 그림은 인스펙터에서 받는다 — 스프라이트를 안 꽂으면 배경 없이 아이콘만 뜬다.</summary>
    void BuildBackground(int _count, float _step)
    {
        if (this.backgroundSprite == null) return;

        if (this.background == null)
        {
            var t_go = new GameObject("Background", typeof(RectTransform));
            t_go.transform.SetParent(transform, false);
            t_go.transform.SetAsFirstSibling();   // 아이콘보다 뒤에(아래에) 그려지게

            this.background = t_go.AddComponent<Image>();
            this.background.type          = Image.Type.Sliced;   // 9-slice — 늘려도 모서리 반경이 안 늘어난다
            this.background.raycastTarget = false;
        }

        this.background.gameObject.SetActive(true);
        this.background.sprite                 = this.backgroundSprite;
        this.background.color                  = this.backgroundColor;
        this.background.pixelsPerUnitMultiplier = this.backgroundBorderShrink;

        var t_rt = (RectTransform)this.background.transform;
        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot =
            this.localSide ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);
        t_rt.sizeDelta = new Vector2(
            _step * (_count - 1) + this.iconSize.x + this.backgroundPadding.x * 2f,
            this.iconSize.y + this.backgroundPadding.y * 2f);
        // 아이콘은 자기 변(아래/위)에 붙어 있으므로 배경판도 같은 변 기준으로 여백만큼 밀어낸다.
        t_rt.anchoredPosition = new Vector2(
            0f, this.localSide ? -this.backgroundPadding.y : this.backgroundPadding.y);
    }

    void Rebuild(SynergyState _state)
    {
        foreach (Image t_old in this.spawned)
            if (t_old != null) Destroy(t_old.gameObject);
        this.spawned.Clear();

        // 시너지가 없으면 배경판도 숨긴다 — 빈 판만 떠 있으면 "무언가 로딩 중"으로 읽힌다.
        if (this.background != null) this.background.gameObject.SetActive(false);

        if (_state == null) return;

        // 몇 개가 뜨는지 먼저 세야 가로 중앙 정렬을 할 수 있다(줄 전체 폭이 정해져야 시작점이 나온다).
        var t_list = new List<SynergyData>();
        foreach (ActiveSynergy t_active in _state.Active)
        {
            if (t_active?.Synergy == null) continue;
            if (t_list.Count >= this.maxBadges) break;
            t_list.Add(t_active.Synergy);
        }
        if (t_list.Count == 0) return;

        float t_step  = this.iconSize.x + this.spacing;
        float t_start = -t_step * (t_list.Count - 1) * 0.5f;   // 줄의 가운데가 패널 원점에 오게

        // 배경판이 먼저다 — 나중에 만든 아이콘이 자식 순서상 뒤에 와서 위에 그려진다(UI는 형제 순서가 곧 깊이).
        BuildBackground(t_list.Count, t_step);

        for (int i = 0; i < t_list.Count; i++)
        {
            var t_go = new GameObject("SynergyIcon_" + t_list[i].name, typeof(RectTransform));
            var t_rt = (RectTransform)t_go.transform;
            t_rt.SetParent(transform, false);
            // 부모가 이미 가운데(아래/위)에 붙어 있으므로 아이콘은 부모 원점 기준으로만 늘어선다.
            // 피벗은 가로 가운데 · 세로는 부모와 같은 변(아래쪽이면 아래, 위쪽이면 위)에 맞춘다.
            t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot =
                this.localSide ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);
            t_rt.sizeDelta        = this.iconSize;
            t_rt.anchoredPosition = new Vector2(t_start + t_step * i, 0f);

            var t_img = t_go.AddComponent<Image>();
            t_img.sprite         = t_list[i].activeIcon;   // 필드에 열린 시너지라 항상 활성 아이콘
            t_img.preserveAspect = true;
            t_img.raycastTarget  = false;   // 카드 입력(드래그/탭)을 가로채지 않게

            this.spawned.Add(t_img);
        }
    }
}
