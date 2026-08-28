using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class CurrencyHud : MonoBehaviour
{
    // 재화가 갈릴 때 주는 한 박. 화면 전환과 같은 프레임에 일어나므로 획득 펄스보다 작게 잡는다.
    const float SWAP_PUNCH = 0.18f;

    // 활성 HUD를 재화별로 찾는 창구. 같은 GameObject에 종류가 다른 HUD가 여러 장 붙어 있어
    // 타입 탐색(FindFirstObjectByType)으로는 어느 쪽이 잡힐지 보장되지 않는다.
    static readonly Dictionary<ECurrencyType, CurrencyHud> s_huds = new Dictionary<ECurrencyType, CurrencyHud>();

    [FormerlySerializedAs("goldText")]
    [SerializeField] TMP_Text valueText;

    [Tooltip("이 칸이 맡을 재화. 이 값이 곧 그 칸의 재화이며 런타임에 갈리지 않는다.")]
    [SerializeField] ECurrencyType type = ECurrencyType.Gold;

    [Tooltip("재화 아이콘(옵션). CurrencyLook 표에 그림이 있을 때만 갈아낀다 — 비워두면 프리팹 그림 그대로다.")]
    [SerializeField] Image iconImage;

    [Tooltip("이 HUD를 그 재화의 대표(코인이 날아와 꽂히는 곳)로 등록할지.\n\n" +
             "화면당 대표는 재화별로 딱 한 장이고, 겹치면 마지막에 켜진 쪽이 이긴다. " +
             "그래서 로비 위에 잠깐 겹쳐 뜨는 화면(개봉 오버레이 등)에 잔액을 하나 더 두려면 " +
             "반드시 이 값을 꺼야 한다 — 켜 두면 그 화면이 닫히는 순간 대표 자리가 통째로 비어(본인 등록만 지운다) " +
             "로비 획득 연출의 코인이 날아갈 곳을 잃는다.\n\n" +
             "끈다고 잃는 것은 도착 지점 자격뿐이다. 잔액 표시·소모 롤다운·연출은 그대로 돈다.")]
    [SerializeField] bool registerAsPrimary = true;

    [Tooltip("배율 연출(획득 펄스·소비 눌림)이 물릴 노드. 미배선이면 이 컴포넌트가 붙은 노드(아이콘+숫자 묶음).\n" +
             "숫자 텍스트를 직접 물리지 말 것 — 그 rect는 LayoutGroup·ContentSizeFitter가 잡는 자식이라 " +
             "피벗이 묶음 한쪽으로 치우쳐 있고, 배율 축이 그 피벗이라 숫자가 옆으로 밀리듯 보인다.")]
    [SerializeField] RectTransform punchTarget;

    // 획득 연출 중에는 실제 잔액 대신 연출이 지정한 표시값을 보여준다(코인이 도착하며 숫자가 오르는 구간).
    bool m_held;
    long m_displayedValue;
    int m_displayRevision;

    /// <summary>수치 텍스트의 RectTransform. 코인이 날아와 꽂히는 **도착 지점**이다.</summary>
    public RectTransform TextRect => this.valueText != null ? (RectTransform)this.valueText.transform : null;

    /// <summary>이 HUD가 맡은 재화. 결제 재화에 맞는 잔액만 띄우려는 화면이 본다.</summary>
    public ECurrencyType Type => this.type;

    /// <summary>펄스로 튀길 노드. 도착 지점과 갈라 둔다 — 코인은 숫자에 꽂혀야 하지만,
    /// 튀는 것은 아이콘까지 묶은 덩어리여야 축이 그 한가운데에 선다.</summary>
    RectTransform PunchRect => this.punchTarget != null ? this.punchTarget
                             : transform is RectTransform t_self ? t_self
                             : this.TextRect;

    /// <summary>해당 재화의 활성 HUD를 얻는다. 꺼져 있거나 없으면 false(그 재화 연출만 건너뛰면 된다).</summary>
    public static bool TryGet(ECurrencyType _type, out CurrencyHud _hud)
    {
        if (!s_huds.TryGetValue(_type, out _hud)) return false;

        // 파괴됐는데 OnDisable이 오지 않은 잔재를 여기서 걷는다.
        if (_hud == null)
        {
            s_huds.Remove(_type);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 획득 연출용 숫자 롤업을 시작하고, 코인 도착 콜백(도착 장수, 전체 장수)에 물릴 진행 핸들러를 돌려준다.
    /// 잔액이 이미 최종값이라는 전제 — 지급·저장이 끝난 뒤에 부른다(획득분만큼 되돌렸다가 도착에 맞춰 다시 올린다).
    /// 연출이 끊겨도 고정이 풀리도록 호출부는 반환된 해제 콜백을 시퀀스 OnKill에 걸어둘 것.
    /// </summary>
    public Action<int, int> BeginGainRollUp(long _gain, out Action _release,
                                            float _punch = UiPunch.DEFAULT_SCALE)
    {
        long t_target = CurrencyManager.GetBalance(this.type);
        long t_start = m_held ? m_displayedValue : t_target - _gain;

        int t_revision = ++m_displayRevision;
        this.HoldDisplay(t_start);

        _release = () => this.ReleaseDisplay(t_revision);

        return (_arrived, _total) =>
        {
            if (t_revision != m_displayRevision) return;

            if (_total <= 0 || _arrived >= _total) this.ReleaseDisplay(t_revision);
            else this.HoldDisplay(t_start + (long)((t_target - t_start) * (_arrived / (float)_total)));

            UiPunch.Play(this.PunchRect, _punch);
        };
    }

    /// <summary>표시값을 연출용으로 고정한다. 실제 잔액 변경은 ReleaseDisplay까지 화면에 반영되지 않는다.</summary>
    void HoldDisplay(long _value)
    {
        m_held = true;
        this.Render(_value);
    }

    /// <summary>고정을 풀고 실제 잔액으로 되돌린다.</summary>
    void ReleaseDisplay(int _revision)
    {
        if (_revision != m_displayRevision) return;

        m_displayRevision++;
        m_held = false;
        this.Render(CurrencyManager.GetBalance(this.type));
    }

    void Awake()
    {
        if (this.valueText == null) this.valueText = GetComponent<TMP_Text>();

        this.ApplyIcon();
    }

    // 프리팹을 복제해 type만 바꾼 HUD(조각 등)가 그림까지 따라오게 한다.
    void ApplyIcon()
    {
        if (this.iconImage == null) return;

        Sprite t_icon = CurrencyLook.IconOf(this.type);
        if (t_icon != null) this.iconImage.sprite = t_icon;
    }

    /// <summary>이 칸이 맡을 재화를 갈아끼운다. **변동 칸 전용** — <see cref="ContextCurrencySlot"/>만 부른다.
    /// 고정 칸에 대고 부르면 그 재화가 화면에서 사라지므로 다른 곳에서 쓰지 말 것.
    /// 대표 등록·아이콘·표시값이 함께 따라간다.</summary>
    public void SetType(ECurrencyType _type)
    {
        if (this.type == _type) return;

        // 돌고 있던 연출은 옛 재화의 것이다 — 걷지 않으면 새 재화 숫자 위에서 롤다운이 계속된다.
        m_displayRevision++;
        m_held = false;
        this.ResetPunchScale();

        // 대표 자리도 함께 옮긴다. 안 옮기면 옛 재화의 코인이 이제 다른 재화를 띄우는 칸으로 날아온다.
        if (s_huds.TryGetValue(this.type, out var t_cur) && t_cur == this) s_huds.Remove(this.type);

        this.type = _type;

        if (this.registerAsPrimary && this.isActiveAndEnabled) s_huds[this.type] = this;

        this.ApplyIcon();
        this.Render(CurrencyManager.GetBalance(this.type));

        // 값이 아니라 **재화가** 갈린 것이라 한 박을 준다 — 없으면 숫자가 몰래 바뀐 것처럼 읽힌다.
        if (this.isActiveAndEnabled) UiPunch.Play(this.PunchRect, SWAP_PUNCH);
    }

    void OnEnable()
    {
        // 종류별 1장. 같은 종류가 겹치면 마지막이 이긴다(예외 없이).
        // 종속 표시(registerAsPrimary=false)는 이 자리를 넘보지 않는다 — 자세한 이유는 그 필드 툴팁.
        if (this.registerAsPrimary) s_huds[this.type] = this;

        // 활성화 시점의 실제 잔액으로 먼저 맞춘 뒤 이후 변경을 구독.
        CurrencyManager.OnCurrencyChanged += this.HandleCurrencyChanged;
        this.Render(CurrencyManager.GetBalance(this.type));
    }

    void OnDisable()
    {
        // 씬 전환은 새 HUD의 OnEnable이 먼저 도는 순서가 가능하다 — 본인일 때만 지워야 새 등록을 밟지 않는다.
        if (s_huds.TryGetValue(this.type, out var t_cur) && t_cur == this) s_huds.Remove(this.type);

        CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;

        m_displayRevision++;
        // 리셋이 DOComplete보다 뒤여야 한다 — 완료는 트윈이 시작할 때 잡아 둔 배율로 되돌리는데,
        // 펄스가 겹친 채 꺼졌으면 그 값이 기준 배율이 아니다.
        if (this.PunchRect != null) this.PunchRect.DOComplete();
        this.ResetPunchScale();
        // 연출 도중 꺼지면 해제 호출이 오지 않는다 — 고정을 여기서 풀어 다음 활성화가 잔액을 못 따라가는 상태를 막는다.
        m_held = false;
    }

    // 이 HUD가 맡은 재화의 변경만 반영. 다른 종류는 무시.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (_type != this.type) return;
        if (m_held) return;
        this.Render(_balance);
    }

    // 배율을 기준으로 되돌린다 — 확대·눌림이 겹친 채 끊겨도 크기가 그 상태로 굳지 않게.
    void ResetPunchScale()
    {
        RectTransform t_rect = this.PunchRect;
        if (t_rect != null) t_rect.localScale = Vector3.one;
    }

    // 천단위 콤마 포맷
    void Render(long _amount)
    {
        m_displayedValue = _amount;
        if (this.valueText == null) return;
        this.valueText.text = _amount.ToString("N0");
    }
}
