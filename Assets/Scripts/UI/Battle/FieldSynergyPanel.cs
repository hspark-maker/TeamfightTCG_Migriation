using System.Collections.Generic;
using UnityEngine;

/// <summary>필드 한쪽의 **활성 시너지 아이콘 줄**. 내 쪽은 화면 아래 가운데, 상대 쪽은 위 가운데에 둔다
/// — 위/아래로 갈라져 있어야 "누구 시너지인가"를 색·글자 없이 위치만으로 읽는다.
///
/// **고정 UI는 씬에 있고, 개수가 변하는 것만 만든다.** 배경판·앵커·간격은 씬 저작이고(런타임 생성 금지),
/// 아이콘은 시너지 수만큼 늘었다 줄었다 하므로 프리팹에서 찍어낸다. 한 번 만든 아이콘은 버리지 않고
/// 껐다 켜 재사용한다 — 갱신마다 파괴/생성하면 그때마다 레이아웃이 다시 돈다.
///
/// **배경판 크기는 코드가 계산하지 않는다.** 아이콘 줄에 HorizontalLayoutGroup + ContentSizeFitter가
/// 붙어 있어 켜진 아이콘 수만큼 저절로 늘고 준다. 폭을 코드로 재면 여백·간격 값이 씬과 코드 두 곳에 생긴다.
///
/// **상대 시너지도 그대로 보여준다**(2026-08-03 사용자 확정). 카드 배지가 뒷면 적의 소속을 가리는 것과
/// 다른 판단이다 — 필드 시너지는 판을 읽는 정보라 양쪽 다 보이는 쪽이 낫다. 정보 누출로 보고 가리지 마라.
/// **개수·티어는 표시하지 않는다** — 아이콘만. 티어는 쓰지 않는 개념이라 여기에 되살리지 마라.
///
/// 배선은 없다 — 켜질 때 스스로 등록하고, <see cref="Show"/>가 진영으로 찾아 그린다
/// (씬마다 참조를 다시 꽂지 않게. 같은 진영 패널이 둘이면 먼저 켜진 쪽이 쓰인다).
///
/// 순수 표시 — 규칙/상태 무접촉. 시너지 스냅샷(SynergyState)은 덱 확정 1회 산출이라 재계산도 없다.</summary>
public class FieldSynergyPanel : MonoBehaviour
{
    [Tooltip("체크=내 필드(아래 가운데), 해제=상대 필드(위 가운데). 어느 진영 것인지만 정한다")]
    [SerializeField] bool localSide = true;

    [Tooltip("배경판 겸 아이콘 줄. HorizontalLayoutGroup + ContentSizeFitter가 붙어 있어야 " +
             "아이콘 수에 맞춰 스스로 늘고 준다. 시너지가 없으면 통째로 꺼진다")]
    [SerializeField] GameObject iconRow;

    [Tooltip("아이콘 한 칸 프리팹(그림+pop+글로우를 스스로 소유한다). 수가 변하는 부분이라 여기만 런타임 생성한다")]
    [SerializeField] SynergyIconView iconPrefab;

    [Tooltip("한 줄에 최대 몇 개까지. 0이면 제한 없음")]
    [SerializeField] int maxIcons = 6;

    static readonly List<FieldSynergyPanel> panels = new List<FieldSynergyPanel>();

    readonly List<SynergyIconView> icons = new List<SynergyIconView>();   // 재사용 풀(파괴하지 않는다)
    SynergyState lastState;
    bool         drawnOnce;        // lastState가 null인 것과 "아직 한 번도 안 그렸다"를 가르는 값
    BattleField  field;            // 확대할 카드를 찾는 출처(그 진영 필드)
    SynergyData  selected;         // 지금 설명이 열려 있는 시너지. null = 닫힘

    void OnEnable()  => panels.Add(this);
    void OnDisable() { panels.Remove(this); Deselect(); }

    /// <summary>그 진영 패널에 시너지를 그린다. 패널이 없는 씬(테스트 씬 등)에선 조용히 무동작.
    /// 필드를 같이 받는 이유는 아이콘을 눌렀을 때 **그 시너지를 가진 카드**를 찾아야 하기 때문이다.</summary>
    public static void Show(bool _localSide, SynergyState _state, BattleField _field = null)
    {
        foreach (FieldSynergyPanel t_panel in panels)
        {
            if (t_panel == null || t_panel.localSide != _localSide) continue;
            t_panel.field = _field;
            t_panel.Refresh(_state);
            return;
        }
    }

    /// <summary>[Triggered] 그 시너지가 실제로 일한 순간, 해당 아이콘 하나만 튄다. 배선이 없거나
    /// 그 시너지가 줄에 없으면 무동작 — 상대 진영 발동이 내 줄을 흔들지 않게 진영으로 먼저 가른다.
    /// 순수 표시(상태·RNG 무접촉).</summary>
    public static void Pop(bool _localSide, SynergyData _synergy)
    {
        if (_synergy == null) return;
        foreach (FieldSynergyPanel t_panel in panels)
        {
            if (t_panel == null || t_panel.localSide != _localSide) continue;
            t_panel.PopIcon(_synergy);
            return;
        }
    }

    /// <summary>그 시너지를 맡은 칸을 찾아 터뜨린다. 켜져 있는 칸만(꺼진 칸은 이번 판에 없는 시너지다).</summary>
    void PopIcon(SynergyData _synergy)
    {
        foreach (SynergyIconView t_icon in this.icons)
        {
            if (t_icon == null || !t_icon.gameObject.activeInHierarchy) continue;
            if (t_icon.Synergy != _synergy) continue;
            t_icon.Pop();
            return;
        }
    }

    /// <summary>같은 스냅샷이면 아무것도 하지 않는다 — 시너지는 덱 확정 1회 산출이라 대부분의 호출이 같은 값이다.
    ///
    /// 단 **첫 호출은 값이 같아도 반드시 그린다**(drawnOnce). 시너지가 없는 판은 _state가 null인데
    /// lastState 초기값도 null이라 여기서 걸러버리면, 씬에서 켜둔 배경판을 끌 기회가 영영 오지 않는다.</summary>
    public void Refresh(SynergyState _state)
    {
        if (this.drawnOnce && ReferenceEquals(this.lastState, _state)) return;
        this.drawnOnce = true;
        this.lastState = _state;
        Deselect();   // 칸이 다른 시너지로 바뀌면 열려 있던 설명·확대가 거짓이 된다

        int t_used = 0;
        if (_state != null && this.iconPrefab != null && this.iconRow != null)
        {
            foreach (ActiveSynergy t_active in _state.Active)
            {
                if (t_active?.Synergy == null) continue;
                if (this.maxIcons > 0 && t_used >= this.maxIcons) break;

                SynergyIconView t_slot = SlotAt(t_used);
                if (t_slot == null) continue;

                t_slot.Bind(t_active.Synergy, t_active.Count);   // 그림 대입 + 이전 판 트윈 정리는 칸이 스스로 한다
                t_slot.gameObject.SetActive(true);
                t_used++;
            }
        }

        // 남는 아이콘은 끈다(지우지 않는다). 레이아웃 그룹이 켜진 것만 세어 가운데로 모으고,
        // 배경판 크기도 그 결과를 따라간다 — 여기서 폭을 계산할 일이 없다.
        for (int i = t_used; i < this.icons.Count; i++)
            if (this.icons[i] != null) this.icons[i].gameObject.SetActive(false);

        if (this.iconRow != null) this.iconRow.SetActive(t_used > 0);
    }


    #region 누르는 동안 = 설명 + 소속 카드 확대
    /// <summary>아이콘을 **누르고 있는 동안만** 그 시너지의 설명 팝업이 뜨고,
    /// **그 시너지를 가진 이 필드 카드들이 확대**된다. 손을 떼면 둘 다 원복 —
    /// 토글이면 닫는 걸 잊은 채 판이 진행돼 확대된 카드가 계속 남는다.
    ///
    /// 팝업·확대 둘 다 기존 기능을 그대로 부른다: 카드 배지 롱프레스가 쓰는 <see cref="ExplainPopupUI"/>와
    /// 드래그 조준이 쓰는 <see cref="CardView.SetTargetFocus"/>. 여기서 새 연출을 만들지 마라 —
    /// 같은 정보가 경로마다 다르게 보이면 어느 쪽이 맞는지 알 수 없다.</summary>
    void OnIconPressed(SynergyIconView _icon)
    {
        if (_icon == null || _icon.Synergy == null) return;

        Deselect();
        this.selected = _icon.Synergy;

        ExplainPopupData t_data = ExplainPopupData.ForSynergy(_icon.Synergy, _icon.OwnedCount);
        if (t_data != null)
        {
            t_data.iconRect = (RectTransform)_icon.transform;   // uGUI 아이콘이라 월드 앵커가 아니라 이쪽
            UIPoolManager.Instance?.AddOrUpdateUI<ExplainPopupUI>(t_data);
        }

        SetFocus(this.selected, true);
    }

    /// <summary>손을 뗐다. 누르는 중이던 칸이 아니어도 닫는다 — 열려 있는 설명은 하나뿐이다.</summary>
    void OnIconReleased(SynergyIconView _icon) => Deselect();

    /// <summary>설명을 닫고 확대를 되돌린다. 열려 있지 않으면 무동작.</summary>
    void Deselect()
    {
        if (this.selected == null) return;

        SetFocus(this.selected, false);
        this.selected = null;
        UIPoolManager.Instance?.HideUI<ExplainPopupUI>();
    }

    /// <summary>이 필드에서 그 시너지 소속인 라이브 카드의 뷰를 켜고 끈다.
    /// 소속 판정은 <see cref="SynergyApplier.BelongsTo"/> 단독 — 여기서 카드 데이터를 다시 훑지 마라.</summary>
    void SetFocus(SynergyData _synergy, bool _on)
    {
        if (this.field == null || _synergy == null) return;

        foreach (CardInstance t_card in this.field.GetActiveCards())
        {
            if (t_card == null || !t_card.IsAlive) continue;
            if (!SynergyApplier.BelongsTo(t_card, _synergy)) continue;
            CardView.GetView(t_card)?.SetTargetFocus(_on);
        }
    }
    #endregion

    /// <summary>i번째 아이콘. 모자라면 그때 하나 더 찍어낸다(시너지 수는 판마다 달라 미리 정해둘 수 없다).</summary>
    SynergyIconView SlotAt(int _index)
    {
        while (this.icons.Count <= _index)
        {
            SynergyIconView t_new = Instantiate(this.iconPrefab, this.iconRow.transform);
            t_new.name = "SynergyIcon_" + this.icons.Count;
            t_new.Pressed  += OnIconPressed;    // 칸은 "눌렸다/뗐다"만 알리고, 무엇을 할지는 줄이 정한다
            t_new.Released += OnIconReleased;
            this.icons.Add(t_new);
        }
        return this.icons[_index];
    }
}
