using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>필드 한쪽의 **활성 시너지 목록**을 화면 모서리에 세로로 세우는 UI 패널.
///
/// 자리는 진영으로 갈린다 — 내 쪽은 **왼쪽 아래**, 상대 쪽은 **오른쪽 위**. 두 줄이 화면 대각선으로
/// 갈라져 있어야 "누구 시너지인가"를 색·글자 없이 위치만으로 읽는다. 앵커·피벗·쌓는 방향은
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
    [Tooltip("체크=내 필드(왼쪽 아래), 해제=상대 필드(오른쪽 위)")]
    [SerializeField] bool localSide = true;

    [Header("겉모습 (px)")]
    [SerializeField] Vector2 iconSize = new Vector2(72f, 72f);
    [SerializeField] float   spacing  = 10f;
    [Tooltip("화면 모서리에서 띄우는 여백(가로, 세로)")]
    [SerializeField] Vector2 margin = new Vector2(24f, 24f);
    [Tooltip("이 수를 넘는 시너지는 표시하지 않는다(줄이 화면을 넘지 않게)")]
    [SerializeField] int maxBadges = 5;

    static readonly List<FieldSynergyPanel> panels = new List<FieldSynergyPanel>();

    readonly List<Image> spawned = new List<Image>();
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

    /// <summary>앵커·피벗을 진영에 맞춘다. 세로로 쌓는 방향은 항상 **화면 안쪽**이다 —
    /// 바깥으로 쌓으면 시너지가 늘수록 화면 밖으로 밀려 마지막 줄이 잘린다.</summary>
    void ApplyCorner()
    {
        var t_rt = (RectTransform)transform;
        Vector2 t_anchor = this.localSide ? new Vector2(0f, 0f) : new Vector2(1f, 1f);

        t_rt.anchorMin = t_anchor;
        t_rt.anchorMax = t_anchor;
        t_rt.pivot     = t_anchor;
        t_rt.anchoredPosition = new Vector2(
            this.localSide ?  this.margin.x : -this.margin.x,
            this.localSide ?  this.margin.y : -this.margin.y);
    }

    void Rebuild(SynergyState _state)
    {
        foreach (Image t_old in this.spawned)
            if (t_old != null) Destroy(t_old.gameObject);
        this.spawned.Clear();

        if (_state == null) return;

        float t_step = (this.iconSize.y + this.spacing) * (this.localSide ? 1f : -1f);

        foreach (ActiveSynergy t_active in _state.Active)
        {
            if (t_active?.Synergy == null) continue;
            if (this.spawned.Count >= this.maxBadges) break;

            var t_go = new GameObject("SynergyIcon_" + t_active.Synergy.name, typeof(RectTransform));
            var t_rt = (RectTransform)t_go.transform;
            t_rt.SetParent(transform, false);
            // 부모가 이미 모서리에 붙어 있으므로 아이콘은 부모 원점 기준으로만 쌓는다.
            t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = this.localSide ? Vector2.zero : Vector2.one;
            t_rt.sizeDelta = this.iconSize;
            t_rt.anchoredPosition = new Vector2(0f, t_step * this.spawned.Count);

            var t_img = t_go.AddComponent<Image>();
            t_img.sprite         = t_active.Synergy.activeIcon;   // 필드에 열린 시너지라 항상 활성 아이콘
            t_img.preserveAspect = true;
            t_img.raycastTarget  = false;   // 카드 입력(드래그/탭)을 가로채지 않게

            this.spawned.Add(t_img);
        }
    }
}
