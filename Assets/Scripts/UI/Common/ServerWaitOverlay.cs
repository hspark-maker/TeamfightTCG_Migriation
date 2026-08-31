using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>서버 응답을 기다리는 동안 화면을 덮는 전역 대기 표시. 입력 차단은 요청 즉시 걸고,
/// 딤·스피너 그림은 showDelay가 지난 뒤에도 요청이 남아 있을 때만 켠다 —
/// 빠른 왕복에서 한 프레임 깜빡이고 사라지면 결함으로 읽힌다.
///
/// 창구는 <see cref="Hold"/> / <see cref="Release"/> 둘뿐이다. 풀 UI라 <see cref="Show"/> · <see cref="Hide"/>는
/// <see cref="PooledUIBase"/>가 요구하는 매개변수 없는 계약이고, owner를 아는 것은 static 창구 쪽이다.
///
/// ⚠ 계층 전제(프리팹 저작이 이 모양을 지켜야 한다):
///   ServerWaitOverlay(RectTransform, 이 스크립트) > Contents > [Blocker, Visual > (Dim, Spinner)]
///   Contents가 <see cref="PooledUIBase.contents"/>다 — Show/Hide가 켜고 끄는 유일한 대상.
///   Blocker는 알파 0 · raycastTarget 켠 Image로, Contents가 켜지는 즉시 입력을 먹는다(임계와 무관).
///   Visual은 CanvasGroup이고 임계(showDelay)를 넘긴 뒤에만 알파가 올라간다.
///   루트에 Canvas·CanvasScaler·GraphicRaycaster는 <b>없다</b> — 풀 컨테이너(<see cref="UiSortingOrder.Pool"/>)의 것을 쓴다.
///
/// ⚠ 이 대기 화면과 실패 안내(<see cref="SimpleYNPopup"/> 등)는 이제 같은 풀 컨테이너에 담겨
/// <b>형제 순서로만</b> 위아래가 갈린다(AddOrUpdateUI가 열 때마다 SetAsLastSibling을 건다).
/// "안내가 대기에 묻히지 않는다"를 보장하는 것은 정렬 층이 아니라 <b>호출부가 Release를 먼저 하고 팝업을 띄우는 순서</b>
/// 하나뿐이다(<see cref="PackPurchaseFlow"/> 참조).</summary>
public class ServerWaitOverlay : PooledUIBase
{
    // UIPoolManager.GetUI는 없을 때 로그를 남긴다 — 대기가 이미 걷힌 뒤의 Release는 정상 갈래라
    // 조용히 넘어갈 수 있게 자기 인스턴스를 직접 들고 있는다.
    static ServerWaitOverlay s_instance;

    [SerializeField] CanvasGroup visualGroup;
    [SerializeField] GameObject blocker;
    [SerializeField] RectTransform spinner;

    [Min(0f)]
    [Tooltip("이 시간이 지나도 대기가 끝나지 않았을 때만 딤·스피너를 켠다. 입력 차단은 이 값과 무관하게 즉시 걸린다.")]
    [SerializeField] float showDelay = 0.3f;

    [Tooltip("임계를 넘겨 그림을 켤 때의 페이드 인 길이. 0이면 즉시 나타난다.")]
    [SerializeField] float fadeDuration = 0.15f;

    [Tooltip("스피너가 한 바퀴 도는 데 걸리는 시간(초).")]
    [SerializeField] float spinPeriod = 1f;

    [Min(0f)]
    [Tooltip("차단이 이 시간을 넘기면 오버레이가 스스로 걷히고 남은 owner를 에러로 알린다(0이면 상한 없음). " +
             "정상 대기가 여기 닿는 일은 없어야 한다 — 실제 왕복 상한(callable 15초 × 재시도 1회 + 재인증, " +
             "앞선 서버 명령의 직렬화 대기까지)보다 넉넉하게 둘 것.")]
    [SerializeField] float safetyTimeout = 90f;

    readonly List<object> m_owners = new List<object>();
    Tween m_spin;
    bool m_visualsShown;

    // 임계 대기를 무효화하는 축. 요청이 모두 걷히면 올려서, 뒤늦게 깨어난 대기가 빈 화면을 덮지 못하게 한다.
    int m_generation;

    /// <summary>_owner의 서버 대기를 시작한다. 같은 owner가 다시 불러도 중복으로 쌓지 않는다.
    ///
    /// 같은 owner로 대기를 중첩하지 말 것 — Release는 참조 카운트를 세지 않아, 겹친 두 대기 중 첫 Release가
    /// 차단막을 통째로 걷는다. 카운트로 바꾸지 않는 이유는 한 owner의 대기가 겹치는 것 자체가 배선 결함이고,
    /// 카운트는 그 결함을 "화면이 안 걷힌다"는 증상 없이 조용히 삼키기 때문이다.</summary>
    public static void Hold(object _owner)
    {
        if (_owner == null) return;

        UIPoolManager.Instance?.AddOrUpdateUI<ServerWaitOverlay>(new ServerWaitData { owner = _owner });
    }

    /// <summary>_owner의 대기를 끝낸다. 남은 요청이 없으면 전부 끈다. 아직 오버레이가 없으면 아무 일도 하지 않는다.</summary>
    public static void Release(object _owner)
    {
        if (_owner == null || s_instance == null) return;

        s_instance.Remove(_owner);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_instance = null;
    }

    protected override void Awake()
    {
        base.Awake();   // RegisterUI — 이걸 빠뜨리면 풀이 이 인스턴스를 영영 못 찾는다
        s_instance = this;

        BindFallbacks();
        ResetToIdle();

        if (this.contents != null) this.contents.SetActive(false);
    }

    /// <summary>owner 하나를 대기 목록에 <b>더한다</b>. 왕복이 겹치면 두 번째 Hold가 여기로 다시 들어오므로 덮어쓰지 않는다.</summary>
    public override void Initialization(UIData _data)
    {
        this.data = _data;

        if (_data is ServerWaitData t_waitData) Push(t_waitData.owner);
    }

    /// <summary>차단막을 세운다. Contents가 켜지는 것이 곧 입력 차단이라, 임계 전에도 이 호출은 먼저 일어나야 한다.</summary>
    public override void Show()
    {
        if (this.contents != null) this.contents.SetActive(true);
        this.isShow = true;
        this.data?.showCustomMethod?.Invoke();
    }

    /// <summary>차단막과 그림을 모두 걷고 대기 상태를 초기값으로 되돌린다.</summary>
    public override void Hide()
    {
        ResetToIdle();

        if (this.contents != null) this.contents.SetActive(false);
        this.isShow = false;
        this.data?.onHide?.Invoke();   // data는 풀이 인스턴스를 만든 직후 Hide가 오면 null일 수 있다
    }

    void BindFallbacks()
    {
        if (this.contents == null)
            Debug.LogWarning("[ServerWaitOverlay] contents가 배선되지 않아 대기 화면을 켤 수 없습니다.", this);

        if (this.blocker == null)
        {
            Debug.LogWarning("[ServerWaitOverlay] blocker가 배선되지 않아 런타임 대체 차단막을 만듭니다.", this);
            this.blocker = CreateFallbackBlocker();
        }

        if (this.visualGroup == null)
            Debug.LogWarning("[ServerWaitOverlay] visualGroup이 배선되지 않아 딤·스피너 표시를 건너뜁니다.", this);

        if (this.spinner == null)
            Debug.LogWarning("[ServerWaitOverlay] spinner가 배선되지 않아 회전 연출을 건너뜁니다.", this);
    }

    // 차단막은 contents 아래여야 한다 — Show/Hide가 켜고 끄는 것이 contents뿐이라, 루트에 붙이면 걷히지 않는다.
    GameObject CreateFallbackBlocker()
    {
        Transform t_parent = this.contents != null ? this.contents.transform : this.transform;

        var t_object = new GameObject("BlockerFallback", typeof(RectTransform), typeof(Image));
        var t_rect = (RectTransform)t_object.transform;
        t_rect.SetParent(t_parent, false);
        t_rect.anchorMin = Vector2.zero;
        t_rect.anchorMax = Vector2.one;
        t_rect.offsetMin = Vector2.zero;
        t_rect.offsetMax = Vector2.zero;
        t_rect.SetAsFirstSibling();

        var t_image = t_object.GetComponent<Image>();
        t_image.color = new Color(0f, 0f, 0f, 0f);   // 알파 0이어도 raycastTarget이면 입력은 먹는다
        t_image.raycastTarget = true;

        return t_object;
    }

    void Push(object _owner)
    {
        if (_owner == null) return;

        PruneDestroyedOwners();

        for (int i = 0; i < this.m_owners.Count; i++)
            if (ReferenceEquals(this.m_owners[i], _owner)) return;

        bool t_wasEmpty = this.m_owners.Count == 0;
        this.m_owners.Add(_owner);

        if (this.blocker != null) this.blocker.SetActive(true);

        // 두 번째 owner가 들어와도 임계 타이머를 다시 걸지 않는다 — 이미 뜬 그림이 다시 페이드되거나
        // 첫 요청의 임계가 뒤로 밀리면 안 된다.
        if (!t_wasEmpty) return;

        RevealAfterDelayAsync(this.m_generation).Forget();
        ForceHideAfterTimeoutAsync(this.m_generation).Forget();
    }

    void Remove(object _owner)
    {
        for (int i = this.m_owners.Count - 1; i >= 0; i--)
            if (ReferenceEquals(this.m_owners[i], _owner)) this.m_owners.RemoveAt(i);

        PruneDestroyedOwners();
        if (this.m_owners.Count == 0) Hide();
    }

    void PruneDestroyedOwners()
    {
        for (int i = this.m_owners.Count - 1; i >= 0; i--)
            if (this.m_owners[i] is UnityEngine.Object t_owner && t_owner == null) this.m_owners.RemoveAt(i);
    }

    async UniTaskVoid RevealAfterDelayAsync(int _generation)
    {
        try
        {
            if (this.showDelay > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(this.showDelay), ignoreTimeScale: true,
                    cancellationToken: this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { return; }   // 오버레이가 먼저 내려갔다

        if (_generation != this.m_generation) return;

        PruneDestroyedOwners();
        if (this.m_owners.Count == 0)
        {
            Hide();
            return;
        }

        ShowVisuals();
    }

    // 배선 실수를 소리내어 잡는 마지막 방어. Hold와 Release가 어긋나 owner가 남으면 화면이 영구히 잠기고
    // 유저에게는 되돌릴 방법이 없다 — 지금 호출부는 전부 try/finally라 실현되지 않지만,
    // 이 부품은 전역 재사용을 전제로 만든 것이라 그렇지 않은 호출부가 생기는 순간 성립한다.
    async UniTaskVoid ForceHideAfterTimeoutAsync(int _generation)
    {
        if (this.safetyTimeout <= 0f) return;

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(this.safetyTimeout), ignoreTimeScale: true,
                cancellationToken: this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { return; }   // 오버레이가 먼저 내려갔다

        if (_generation != this.m_generation) return;

        PruneDestroyedOwners();
        if (this.m_owners.Count > 0)
            Debug.LogError($"[ServerWaitOverlay] 대기가 {this.safetyTimeout:0}초 동안 걷히지 않아 강제로 내립니다 " +
                           $"— Release를 부르지 않은 owner: {DescribeOwners()}", this);

        Hide();
    }

    string DescribeOwners()
    {
        var t_text = new StringBuilder();

        for (int i = 0; i < this.m_owners.Count; i++)
        {
            if (t_text.Length > 0) t_text.Append(", ");
            t_text.Append(OwnerLabel(this.m_owners[i]));
        }

        return t_text.Length > 0 ? t_text.ToString() : "(없음)";
    }

    // static 경로는 타입 자체를 키로 넘긴다 — 그 갈래에서 GetType()을 물으면 owner가 누구인지 사라진다.
    static string OwnerLabel(object _owner)
    {
        if (_owner is Type t_type) return t_type.Name;
        if (_owner is UnityEngine.Object t_object) return $"{t_object.GetType().Name}({t_object.name})";

        return _owner != null ? _owner.GetType().Name : "null";
    }

    void ShowVisuals()
    {
        if (this.m_visualsShown) return;
        this.m_visualsShown = true;

        if (this.visualGroup != null)
        {
            this.visualGroup.DOKill();
            this.visualGroup.gameObject.SetActive(true);
            this.visualGroup.interactable = false;
            this.visualGroup.blocksRaycasts = true;

            if (this.fadeDuration > 0f)
            {
                this.visualGroup.alpha = 0f;
                this.visualGroup.DOFade(1f, this.fadeDuration)
                    .SetUpdate(true)
                    .SetLink(this.gameObject);
            }
            else this.visualGroup.alpha = 1f;
        }

        StartSpin();
    }

    // contents는 건드리지 않는다 — 켜고 끄는 것은 풀 관용구를 따르는 Show/Hide의 몫이고,
    // 여기는 그 안에서 다음 대기를 처음부터 시작할 수 있게 상태만 되돌린다.
    void ResetToIdle()
    {
        this.m_generation++;
        this.m_visualsShown = false;
        this.m_owners.Clear();

        KillSpin();

        if (this.visualGroup != null)
        {
            this.visualGroup.DOKill();
            this.visualGroup.alpha = 0f;
            this.visualGroup.interactable = false;
            this.visualGroup.blocksRaycasts = false;
        }

        if (this.blocker != null) this.blocker.SetActive(false);
    }

    void StartSpin()
    {
        if (this.m_spin != null && this.m_spin.IsActive()) return;
        if (this.spinner == null || this.spinPeriod <= 0f) return;

        this.spinner.localRotation = Quaternion.identity;

        // SetUpdate(true) — 서버를 기다리는 동안 Time.timeScale이 0일 수 있다.
        this.m_spin = this.spinner
            .DOLocalRotate(new Vector3(0f, 0f, -360f), this.spinPeriod, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Restart)
            .SetUpdate(true)
            .SetLink(this.gameObject);
    }

    // 상시 회전은 오브젝트를 꺼도 멈추지 않는다 — 손으로 죽이고 각도까지 세워야 다음에 켤 때 기울어 있지 않다.
    void KillSpin()
    {
        if (this.m_spin != null && this.m_spin.IsActive()) this.m_spin.Kill();
        this.m_spin = null;

        if (this.spinner != null) this.spinner.localRotation = Quaternion.identity;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (s_instance == this) s_instance = null;
        this.m_owners.Clear();
        this.m_spin = null;
    }
}


/// <summary>대기를 요청한 주체 한 명. Initialization이 이 owner를 목록에 더한다.</summary>
public class ServerWaitData : UIData
{
    public object owner;
}
