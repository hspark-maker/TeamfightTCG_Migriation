using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>상단바 재화 칸 하나. 잔액을 띄우고 획득·소비 연출을 돌리며, 대표 칸이면 다른 재화에 잠시 빌려주기도 한다.</summary>
public class CurrencyHud : MonoBehaviour
{
    // 타입 탐색(FindFirstObjectByType)으로는 한 GameObject에 여러 장 붙은 HUD 중 어느 쪽이 잡힐지 보장되지 않는다.
    static readonly Dictionary<ECurrencyType, CurrencyHud> s_huds = new Dictionary<ECurrencyType, CurrencyHud>();

    [FormerlySerializedAs("goldText")]
    [SerializeField] TMP_Text valueText;

    [Tooltip("이 칸의 **고향 재화**(저작 시점의 값). 런타임에 갈린다 — 대표 HUD가 없는 재화(에너지 등)의 " +
             "획득 연출이 도착지를 찾지 못하면 CurrencySlotBoard가 이 칸을 잠시 빌려 그 재화로 갈아입히고, " +
             "로비 탭을 넘어갈 때 고향 재화로 되돌린다.\n\n" +
             "아이콘은 CurrencyLook 표의 barIcon이 진실원이다 — 칸이 갈릴 때 아래 Image가 그 그림으로 갈린다. " +
             "같은 표의 icon(보상 슬롯용)과는 일부러 갈라 둔 별개 칸이다.\n\n" +
             "Type을 캐싱하지 말 것. 이 칸의 재화는 프레임마다 달라질 수 있으니 쓸 때마다 다시 물어라.")]
    [SerializeField] ECurrencyType type = ECurrencyType.Gold;

    [Tooltip("이 칸의 재화 아이콘. 칸이 재화를 갈아입을 때 CurrencyLook 표의 barIcon으로 갈린다.\n" +
             "미배선이면 이 칸은 빌려줄 수 없다(그림과 숫자가 어긋나느니 그 재화 연출을 건너뛴다).")]
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
             "피벗이 묶음 한쪽으로 치우쳐 있고, 배율 축이 그 피벗이라 숫자가 옆으로 밀리듯 보인다.\n\n" +
             "칸 전환 롤의 창 안쪽 노드로도 물리지 말 것 — UiPunch가 대상의 트윈을 먼저 끝내므로 롤이 강제 완료된다.")]
    [SerializeField] RectTransform punchTarget;

    [Tooltip("칸이 다른 재화로 갈릴 때 도는 세로 롤의 길이(초). 옛 내용이 한쪽으로 빠지고 새 내용이 반대쪽에서 올라온다.\n" +
             "대여는 밑에서 위로, 반납은 위에서 아래로 — 방향이 곧 '빌렸다/돌아왔다'를 말한다.\n\n" +
             "0으로 두면 롤 없이 한 프레임에 교체된다(연출을 끄는 스위치).")]
    [SerializeField, Min(0f)] float rollDuration = 0.22f;

    [Tooltip("롤이 멎는 감. OutBack이 살짝 지나갔다 돌아오며 릴이 '탁' 걸리는 소리를 낸다.")]
    [SerializeField] Ease rollEase = Ease.OutBack;

    [Tooltip("빌린 재화를 마저 보여 주는 시간(초). 획득 연출이 끝나는 순간부터 잰다 — 코인이 다 꽂히고 " +
             "숫자가 멎은 뒤에도 이만큼은 그 재화가 이 칸에 남아 있다가 스스로 고향 재화로 돌아온다.\n\n" +
             "짧으면 유저가 늘어난 잔액을 읽기 전에 칸이 갈리고, 길면 엉뚱한 재화가 상단바를 오래 차지한다.")]
    [SerializeField, Min(0.1f)] float lendHoldDuration = 2f;

    [Header("소모 연출")]
    [SerializeField, Min(0.01f)] float spendRollDuration = 0.55f;

    [Tooltip("소비 첫 박에 눌리는 깊이. 확대로 곧장 올라가지 않고 한 번 눌렀다 튀어야 '빠져나갔다'가 읽힌다.\n" +
             "1이면 눌림을 건너뛰고 바로 확대 배율로 올라간다.")]
    [SerializeField, Range(0.5f, 1f)] float spendPressScale = 0.92f;

    [Tooltip("롤다운이 도는 내내 유지할 확대 배율. 어느 재화가 빠지는 중인지 눈이 놓치지 않게 " +
             "이 HUD만 커진 채로 숫자가 굴러간다 — 롤이 끝나야 원래 크기로 내려온다.\n" +
             "옆 HUD와 겹치므로 크게 잡지 말 것 — localScale은 LayoutGroup 계산에 안 잡힌다.")]
    [SerializeField, Range(1f, 1.6f)] float spendHoldScale = 1.08f;

    [Tooltip("눌렸다 확대 배율까지 올라가는 데 걸리는 시간. 롤다운보다 훨씬 짧아야 '툭' 하는 한 박으로 들린다.")]
    [SerializeField, Min(0.01f)] float spendPressDuration = 0.14f;

    [Tooltip("롤이 끝나고 원래 크기로 내려오는 데 걸리는 시간. 1 아래로 살짝 지나갔다 돌아오며 '탁' 하고 멎는다.")]
    [SerializeField, Min(0.01f)] float spendReturnDuration = 0.2f;

    [Tooltip("롤다운이 도는 내내 숫자를 물들일 색. 롤이 끝나는 순간 원래 색으로 돌아온다.\n" +
             "반복 펄스를 대신하는 자리다 — 흔들지 않고도 '지금 줄고 있다'를 계속 말해 준다.")]
    [SerializeField] Color spendTint = new Color(1f, 0.42f, 0.35f);

    // 연출이 도는 동안에는 실제 잔액 대신 연출이 지정한 표시값을 보여준다.
    bool m_held;
    long m_displayedValue;
    int m_displayRevision;
    Tweener m_spendTween;
    long m_spendTarget;
    Tween m_spendMotion;
    Color m_baseTextColor = Color.white;
    bool m_tinted;
    ECurrencyType m_defaultType;
    int m_lendSerial;
    Tween m_lendRelease;
    readonly CurrencySlotRoll m_roll = new CurrencySlotRoll();

    /// <summary>코인이 날아와 꽂히는 도착 지점.</summary>
    // 롤이 붙은 칸은 수치가 아니라 그 창을 준다 — 도착 좌표는 조립 시점에 굳으므로 움직이는 rect를 내주면 궤적이 어긋난다.
    public RectTransform TextRect => m_roll.TextWindow != null ? m_roll.TextWindow
                                   : this.valueText != null ? (RectTransform)this.valueText.transform
                                   : null;

    /// <summary>이 HUD가 <b>지금</b> 맡은 재화. 빌려간 동안 갈리므로 캐싱하지 말 것.</summary>
    public ECurrencyType Type => this.type;

    /// <summary>저작 시점의 고향 재화. 반납하면 여기로 돌아온다.</summary>
    public ECurrencyType DefaultType => m_defaultType;

    /// <summary>다른 재화에 내줄 수 있는 칸인지.</summary>
    // 고향 그림이 표에 없으면 내주지 않는다 — 반납해도 대여 재화 그림이 그대로 굳는다.
    public bool IsLendable => this.registerAsPrimary
                           && this.iconImage != null
                           && CurrencyLook.BarIconOf(m_defaultType) != null;

    /// <summary>지금 고향이 아닌 재화를 맡고 있는지.</summary>
    public bool IsLent => this.type != m_defaultType;

    /// <summary>빌려간 순번. 회수는 가장 오래된 대여부터.</summary>
    public int LendSerial => m_lendSerial;

    /// <summary>수치 연출이 도는 중인지.</summary>
    public bool IsBusy => m_held || (m_spendTween != null && m_spendTween.IsActive());

    /// <summary>칸을 갈아입는 롤이 도는 중인지.</summary>
    // IsBusy에 섞지 말 것 — 고향 칸 회수 경로가 IsBusy면 건너뛰므로 그 재화의 획득 연출이 통째로 사라진다.
    public bool IsRolling => m_roll.IsRolling;

    // 코인은 숫자에 꽂혀야 하지만 튀는 것은 아이콘까지 묶은 덩어리여야 축이 한가운데에 선다.
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
    /// 잔액이 이미 최종값이라는 전제 — 지급·저장이 끝난 뒤에 부른다.
    /// 호출부는 반환된 해제 콜백을 시퀀스 OnKill에 걸어 둘 것(연출이 끊겨도 고정이 풀리도록).
    /// </summary>
    public Action<int, int> BeginGainRollUp(long _gain, out Action _release,
                                            float _punch = UiPunch.DEFAULT_SCALE)
    {
        long t_target = CurrencyManager.GetBalance(this.type);
        long t_start = m_held ? m_displayedValue : t_target - _gain;

        this.KillSpendTween();
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

    /// <summary>이 칸을 다른 재화에 빌려준다. 배차는 CurrencySlotBoard가 정하고 여기서는 갈아입기만 한다.</summary>
    public void Lend(ECurrencyType _type, int _serial)
    {
        m_lendSerial = _serial;
        this.Rebind(_type);
        this.ArmLendRelease();
    }

    /// <summary>고향 재화로 즉시 되돌린다.</summary>
    public void Return()
    {
        this.KillLendRelease();
        m_lendSerial = 0;
        this.Rebind(m_defaultType);
    }

    // 연출이 멎을 때마다(ReleaseDisplay) 다시 감기므로, 실제로 재는 것은 "마지막 연출이 끝난 뒤부터"다.
    void ArmLendRelease()
    {
        this.KillLendRelease();
        if (!this.IsLent) return;

        m_lendRelease = DOVirtual.DelayedCall(Mathf.Max(0.1f, this.lendHoldDuration), this.ReleaseLendIfIdle)
                                 .SetLink(gameObject);
    }

    void ReleaseLendIfIdle()
    {
        m_lendRelease = null;
        if (!this.IsLent) return;

        // 코인이 오는 중이면 도착지를 치우지 않고, 대여 롤이 도는 중이면 반납 롤이 겹치지 않게 한 박 더 기다린다.
        if (this.IsBusy || this.IsRolling) { this.ArmLendRelease(); return; }

        this.Return();
    }

    void KillLendRelease()
    {
        Tween t_tween = m_lendRelease;
        m_lendRelease = null;
        if (t_tween != null && t_tween.IsActive()) t_tween.Kill();
    }

    // 아래 순서가 곧 s_huds 규약이다 — 옛 연출을 먼저 무효화하고, 대표 등록을 옮긴 뒤, 그림과 숫자를 새 재화로 맞춘다.
    void Rebind(ECurrencyType _type)
    {
        if (this.type == _type) return;

        Sprite t_oldIcon = this.iconImage != null ? this.iconImage.sprite : null;
        string t_oldText = this.valueText != null ? this.valueText.text : null;

        m_displayRevision++;
        m_held = false;

        this.KillSpendTween();
        this.KillSpendMotion();
        this.ClearTint(false);

        // OnDisable과 같은 "본인일 때만" 문장 — 남의 등록을 밟지 않는다.
        if (this.registerAsPrimary && s_huds.TryGetValue(this.type, out var t_cur) && t_cur == this) s_huds.Remove(this.type);

        this.type = _type;
        if (this.registerAsPrimary) s_huds[this.type] = this;

        this.ApplyIcon();
        this.Render(CurrencyManager.GetBalance(this.type));

        // 고향으로 되돌아가는 길은 대여를 되감는 그림이라 방향이 반대다.
        m_roll.Play(_upward: _type != m_defaultType, t_oldIcon, t_oldText, this.rollDuration, this.rollEase);
    }

    // 갈리지 않는 종속 표시(개봉 오버레이 칩 등)는 그 화면 아트가 진실원이라 건드리지 않는다.
    void ApplyIcon()
    {
        if (!this.IsLendable) return;

        var t_icon = CurrencyLook.BarIconOf(this.type);
        if (t_icon != null) this.iconImage.sprite = t_icon;
    }

    void HoldDisplay(long _value)
    {
        m_held = true;
        this.Render(_value);
    }

    void ReleaseDisplay(int _revision)
    {
        if (_revision != m_displayRevision) return;

        m_displayRevision++;
        m_held = false;
        this.Render(CurrencyManager.GetBalance(this.type));

        // 연출이 멎은 지금이 "충분히 보여줄" 시간의 시작점이다. 여기서 안 감으면 코인이 날아온 만큼 대여가 짧아진다.
        this.ArmLendRelease();
    }

    void Awake()
    {
        m_defaultType = this.type;

        if (this.valueText == null) this.valueText = GetComponent<TMP_Text>();

        // 연출 도중에 잡으면 소비 색이 기준색으로 굳는다.
        if (this.valueText != null) m_baseTextColor = this.valueText.color;

        // 게이트에 IsLendable을 쓰지 않는다 — 그 판정이 CurrencyLook을 읽어 부트 주입 순서에 매인다.
        if (this.registerAsPrimary && this.iconImage != null && this.valueText != null)
            m_roll.Bind(transform as RectTransform, this.iconImage, this.valueText);
    }

    void OnEnable()
    {
        // 종류별 1장. 같은 종류가 겹치면 마지막이 이긴다(종속 표시는 이 자리를 넘보지 않는다 — registerAsPrimary 툴팁).
        if (this.registerAsPrimary)
        {
            s_huds[this.type] = this;
            CurrencySlotBoard.Register(this);
        }

        this.ApplyIcon();

        CurrencyManager.OnCurrencyChanged += this.HandleCurrencyChanged;
        CurrencyManager.OnCurrencySpent += this.HandleCurrencySpent;
        this.Render(CurrencyManager.GetBalance(this.type));
    }

    void OnDisable()
    {
        // 씬 전환은 새 HUD의 OnEnable이 먼저 도는 순서가 가능하다 — 본인일 때만 지워야 새 등록을 밟지 않는다.
        if (s_huds.TryGetValue(this.type, out var t_cur) && t_cur == this) s_huds.Remove(this.type);

        CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;
        CurrencyManager.OnCurrencySpent -= this.HandleCurrencySpent;

        m_displayRevision++;
        this.KillSpendTween();
        this.KillSpendMotion();
        this.ClearTint(false);
        if (this.PunchRect != null) this.PunchRect.DOComplete();
        // 연출 도중 꺼지면 해제 호출이 오지 않는다.
        m_held = false;

        CurrencySlotBoard.Unregister(this);
        this.KillLendRelease();
        // 위 DOComplete는 칸 자신만 잡는다 — 롤은 자식 anchoredPosition이라 여기서 따로 앉혀야 한다.
        m_roll.Snap();
        m_lendSerial = 0;
        // 고향으로 되돌려 두면 씬 전환 반납이 따로 필요 없다(등록은 하지 않는다 — 다음 OnEnable이 다시 잡는다).
        this.type = m_defaultType;
    }

    void HandleCurrencySpent(ECurrencyType _type, long _cost, long _balance)
    {
        if (_type != this.type) return;

        this.BeginSpendRollDown(_cost, _balance);
    }

    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (_type != this.type) return;

        if (m_spendTween != null && m_spendTween.IsActive())
        {
            if (_balance != m_spendTarget)
            {
                m_spendTarget = _balance;
                m_spendTween.ChangeEndValue(_balance, snapStartValue: true);
            }
            return;
        }

        if (m_held) return;
        this.Render(_balance);
    }

    void BeginSpendRollDown(long _cost, long _balance)
    {
        long t_start = m_displayedValue;
        this.KillSpendTween();

        int t_revision = ++m_displayRevision;
        long t_value = Math.Max(t_start, _balance + _cost);
        this.HoldDisplay(t_value);
        m_spendTarget = _balance;

        float t_duration = Mathf.Max(0.01f, this.spendRollDuration);

        // 소비는 두 박이다 — 시작에 '툭', 끝에 '탁'. 그 사이에 배율을 더 흔들면 정보가 아니라 노이즈가 된다.
        this.PlayLift();
        this.ApplyTint();

        Tweener t_tween = DOTween.To(() => t_value,
                                    _value =>
                                    {
                                        t_value = _value;
                                        if (t_revision == m_displayRevision) this.Render(_value);
                                    },
                                    _balance, t_duration)
                                .SetEase(Ease.OutCubic)
                                .SetLink(gameObject);

        t_tween.OnComplete(() => this.FinishSpendRollDown(t_revision, _settle: true));
        // 위 OnComplete가 리비전을 올려 두므로 이 경로가 잡는 것은 '중간에 끊긴 롤' 뿐이다.
        t_tween.OnKill(() => this.FinishSpendRollDown(t_revision, _settle: false));
        m_spendTween = t_tween;
    }

    void FinishSpendRollDown(int _revision, bool _settle)
    {
        if (_revision != m_displayRevision) return;

        m_spendTween = null;
        this.ClearTint(_settle);
        // 끊긴 롤은 내려오는 연출 없이 즉시 되돌린다 — 커진 채로 굳는 것만은 막아야 한다.
        if (_settle) this.PlaySettle();
        else this.KillSpendMotion();
        this.ReleaseDisplay(_revision);
    }

    // 확대 배율까지 튀어 올라 **그대로 머문다** — 롤이 도는 내내 커져 있어야 어느 재화가 빠지는지 눈이 놓치지 않는다.
    void PlayLift()
    {
        RectTransform t_rect = this.PunchRect;
        if (t_rect == null) return;

        this.KillSpendMotion();

        float t_duration = Mathf.Max(0.01f, this.spendPressDuration);
        float t_hold = Mathf.Max(1f, this.spendHoldScale);
        Sequence t_motion = DOTween.Sequence().SetLink(t_rect.gameObject);

        if (this.spendPressScale < 1f)
            t_motion.Append(t_rect.DOScale(this.spendPressScale, t_duration * 0.35f).SetEase(Ease.OutQuad));

        t_motion.Append(t_rect.DOScale(t_hold, t_duration * 0.65f).SetEase(Ease.OutBack));
        m_spendMotion = t_motion;
    }

    // OutBack이 1 아래를 살짝 지나갔다 돌아와서, 따로 tick을 쏘지 않아도 '탁' 하고 멎는 소리가 난다.
    void PlaySettle()
    {
        RectTransform t_rect = this.PunchRect;
        if (t_rect == null) return;

        // 배율을 되돌리지 않고 끈다 — 커져 있는 지금 크기가 이 트윈의 출발점이다.
        this.KillSpendMotion(_resetScale: false);

        m_spendMotion = t_rect.DOScale(1f, Mathf.Max(0.01f, this.spendReturnDuration))
                              .SetEase(Ease.OutBack)
                              .SetLink(t_rect.gameObject);
    }

    void ApplyTint()
    {
        if (this.valueText == null) return;

        this.valueText.DOKill();
        this.valueText.color = this.spendTint;
        m_tinted = true;
    }

    void ClearTint(bool _fade)
    {
        if (!m_tinted) return;

        m_tinted = false;
        if (this.valueText == null) return;

        this.valueText.DOKill();
        if (_fade && this.isActiveAndEnabled)
            this.valueText.DOColor(m_baseTextColor, 0.18f).SetLink(this.valueText.gameObject);
        else
            this.valueText.color = m_baseTextColor;
    }

    void KillSpendTween()
    {
        Tweener t_tween = m_spendTween;
        m_spendTween = null;
        if (t_tween != null && t_tween.IsActive()) t_tween.Kill();
    }

    // 이어서 지금 크기부터 트윈할 때만 _resetScale을 꺼서 출발점을 남긴다.
    void KillSpendMotion(bool _resetScale = true)
    {
        Tween t_motion = m_spendMotion;
        m_spendMotion = null;
        if (t_motion != null && t_motion.IsActive()) t_motion.Kill();

        if (!_resetScale) return;

        RectTransform t_rect = this.PunchRect;
        if (t_rect != null) t_rect.localScale = Vector3.one;
    }

    void Render(long _amount)
    {
        m_displayedValue = _amount;
        if (this.valueText == null) return;
        this.valueText.text = _amount.ToString("N0");
    }
}
