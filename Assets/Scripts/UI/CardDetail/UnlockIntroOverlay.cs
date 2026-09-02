using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 해금된 개념(키워드·시너지)을 전면에서 한 장으로 가르치고 [확인]을 기다리는 오버레이.
// 딤을 눌러서는 닫히지 않고, 행의 등장은 자리 대신 배율로 준다(레이아웃 그룹이 자리를 되돌린다).
public class UnlockIntroOverlay : SingletonOverlay<UnlockIntroOverlay>
{
    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] Button confirmButton;

    [Tooltip("행이 깔리는 노드. 자식은 UnlockIntroRow를 단 노드여야 하고, 런타임 Instantiate는 없다.")]
    [SerializeField] Transform rowRoot;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("첫 행이 들어오기까지의 뜸. 딤이 깔리는 동안은 아직 읽을 것이 없다.")]
    [SerializeField] float rowDelay = 0.12f;
    [Tooltip("행 하나가 들어오는 시간.")]
    [SerializeField] float rowDuration = 0.2f;
    [Tooltip("행끼리 밀리는 간격. 0이면 전부 한 덩어리로 떠서 개수가 안 읽힌다.")]
    [SerializeField] float rowInterval = 0.1f;
    [Tooltip("행이 이 배율에서 출발해 제 크기로 앉는다. 1이면 페이드만 남는다.")]
    [SerializeField] float rowFromScale = 0.92f;

    [Tooltip("행이 다 뜬 뒤 [확인]이 열리기까지의 뜸. 이 구간이 읽는 시간이다.")]
    [SerializeField] float confirmDelay = 0.5f;
    [SerializeField] float confirmFadeDuration = 0.16f;

    Sequence m_intro;

    // 한 번 쓰면 비워 연타를 막는다.
    Action m_onClose;

    CanvasGroup m_confirmGroup;

    int m_shownRows;

    // 화면이 걷힐 때 함께 걷지 않으면 안 보이는 자리에서 카메라가 계속 RenderTexture를 그린다.
    UnlockDemoStage m_demo;

    // 매 표시마다 경고하면 로그가 묻힌다.
    static bool s_rowShortageWarned;

    /// <summary>안내 오버레이를 얻는다(평소 꺼져 있는 노드라 비활성까지 뒤진다).</summary>
    public static bool TryGet(out UnlockIntroOverlay _overlay)
        => TryGetOrCreate(RuntimeOverlayPrefabs.Get<UnlockIntroOverlay>, out _overlay);

    /// <summary>_intros를 세우고 [확인]을 기다린다(_onClose는 걷힌 뒤 한 번, 빈 목록이면 곧바로 온다).</summary>
    public void Show(IReadOnlyList<UnlockIntro> _intros, int _card, Action _onClose)
    {
        // 시퀀스에 중첩된 트윈은 대상의 DOKill이 잡지 못해 새 안무와 같은 노드를 함께 민다.
        KillIntro();
        EndDemo();

        this.m_shownRows = BuildRows(_intros);

        if (this.m_shownRows == 0)
        {
            this.m_onClose = null;
            _onClose?.Invoke();
            return;
        }

        this.m_onClose = _onClose;

        if (this.confirmButton != null)
        {
            this.confirmButton.onClick.RemoveAllListeners();   // 재표시마다 중복 등록 방지
            this.confirmButton.onClick.AddListener(OnConfirmClicked);
        }

        IsOpen = true;
        SetVisible(true);

        // 다 서기 전에 눌러 닫히면 무엇이 열렸는지 못 본다.
        SetInputEnabled(false);

        BeginDemo(_intros, _card);

        this.m_intro = BuildIntro();
        this.m_intro.Play();
    }

    /// <summary>밖에서 걷는다(화면이 통째로 넘어가는 경로). 콜백은 흘리지 않는다.</summary>
    public void Hide()
    {
        this.m_onClose = null;
        KillIntro();
        EndDemo();

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        SetVisible(false);
        ResetChoreography();

        if (t_wasOpen) RaiseClosed();
    }

    // 이 화면이 서는 층은 프리팹 저작값이 아니라 UiSortingOrder 표가 쥔다.
    void Awake()
    {
        UiSortingOrder.Stamp(GetComponent<Canvas>(), UiSortingOrder.Intro);
    }

    // 잠금을 푸는 곳이 등장 안무뿐이라, Show를 거치지 않고 뜨면 [확인]이 잠긴 모달로 남는다.
    void OnEnable()
    {
        SetInputEnabled(true);
    }

    void OnDisable()
    {
        this.transition.HandleDisabled(ResolveTarget());
        KillIntro();
        EndDemo();
        ResetChoreography();

        // Hide를 거치지 않고 꺼지는 경로(부모 비활성·씬 언로드)에서 이 플래그가 남으면 영영 열린 것으로 읽힌다.
        IsOpen = false;
    }

    void OnConfirmClicked()
    {
        // 콜백 유무로 연타를 막으면 뒤처리가 없는 호출부에서 [확인]이 아무 일도 안 하는 모달이 된다.
        if (!IsOpen) return;

        // 정리 도중 다시 들어와도 두 번 흐르지 않게 먼저 비운다.
        var t_callback = this.m_onClose;
        this.m_onClose = null;

        SetInputEnabled(false);

        IsOpen = false;

        KillIntro();
        EndDemo();
        SetVisible(false);
        ResetChoreography();

        RaiseClosed();

        // 받는 쪽이 이 화면의 상태를 다시 물어볼 수 있어야 해서 정리가 끝난 뒤에 넘긴다.
        t_callback?.Invoke();
    }

    // 프리팹에 미리 깔린 행을 꺼내 쓰고 남는 것은 끈다. 돌려주는 값은 실제로 세운 수.
    int BuildRows(IReadOnlyList<UnlockIntro> _intros)
    {
        if (this.rowRoot == null)
        {
            Debug.LogWarning("[UnlockIntroOverlay] rowRoot 미배선 — 세울 자리가 없습니다.");
            return 0;
        }

        int t_used  = 0;
        int t_count = _intros != null ? _intros.Count : 0;

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            if (t_used >= this.rowRoot.childCount)
            {
                if (!s_rowShortageWarned)
                {
                    s_rowShortageWarned = true;
                    Debug.LogWarning($"[UnlockIntroOverlay] 깔린 행이 {this.rowRoot.childCount}개뿐이라 "
                                   + $"{t_count}개를 다 세우지 못했습니다(프리팹에 행을 더 깔 것).");
                }
                break;
            }

            Transform t_row  = this.rowRoot.GetChild(t_used);
            var       t_view = t_row.GetComponent<UnlockIntroRow>();
            if (t_view == null) continue;   // 행이 아닌 장식 노드가 섞일 수 있다

            t_view.Bind(_intros[t_i]);
            t_view.SetDemo(null);           // 띠는 BeginDemo가 딱 한 줄에만 켠다
            t_row.gameObject.SetActive(true);
            t_used++;
        }

        for (int t_i = t_used; t_i < this.rowRoot.childCount; t_i++)
            this.rowRoot.GetChild(t_i).gameObject.SetActive(false);

        return t_used;
    }

    /// <summary>데모를 맨 윗줄 하나에만 세운다 — 무대·카메라가 하나뿐이라 동시에 둘을 돌릴 수 없다.</summary>
    void BeginDemo(IReadOnlyList<UnlockIntro> _intros, int _card)
    {
        if (_card <= 0 || this.rowRoot == null) return;

        for (int t_i = 0; t_i < this.m_shownRows && t_i < _intros.Count; t_i++)
        {
            UnlockIntro t_intro = _intros[t_i];

            // 시너지를 못 받은 줄은 대본을 고를 수 없어 배지로 남긴다.
            if (t_intro.IsSynergy && t_intro.Synergy == null) continue;

            var t_view = this.rowRoot.GetChild(t_i).GetComponent<UnlockIntroRow>();
            if (t_view == null) continue;

            if (!UnlockDemoStage.TryGet(out UnlockDemoStage t_stage)) return;

            Texture t_tex = t_intro.IsSynergy ? t_stage.Begin(_card, t_intro.Synergy)
                                              : t_stage.Begin(_card, t_intro.Keyword);
            if (t_tex == null) { t_stage.End(); return; }   // 저작이 덜 됐다 — 띠 없이 글자만 남긴다

            this.m_demo = t_stage;
            t_view.SetDemo(t_tex);
            return;
        }
    }

    // 텍스처가 해제되므로 띠에서 먼저 떼어야 한다 — 뒤집으면 죽은 RenderTexture가 한 프레임 남는다.
    void EndDemo()
    {
        UnlockDemoStage t_demo = this.m_demo;
        this.m_demo = null;
        if (t_demo == null) return;

        if (this.rowRoot != null)
            for (int t_i = 0; t_i < this.rowRoot.childCount; t_i++)
                this.rowRoot.GetChild(t_i).GetComponent<UnlockIntroRow>()?.SetDemo(null);

        t_demo.End();
    }

    // 등장 안무. 행이 하나씩 앉은 뒤에야 [확인]이 열린다.
    Sequence BuildIntro()
    {
        PrimeIntro();

        var   t_seq    = DOTween.Sequence().SetLink(gameObject);
        float t_rowDur = Mathf.Max(0.01f, this.rowDuration);
        float t_last   = this.rowDelay;

        for (int t_i = 0; t_i < this.m_shownRows; t_i++)
        {
            Transform t_row = this.rowRoot.GetChild(t_i);
            float     t_at  = this.rowDelay + Mathf.Max(0f, this.rowInterval) * t_i;

            t_seq.Insert(t_at, t_row.DOScale(1f, t_rowDur).SetEase(Ease.OutBack));
            t_seq.Insert(t_at, GroupOf(t_row.gameObject).DOFade(1f, t_rowDur));

            t_last = t_at + t_rowDur;
        }

        float t_fade      = Mathf.Max(0.01f, this.confirmFadeDuration);
        float t_confirmAt = t_last + Mathf.Max(0f, this.confirmDelay);

        if (this.m_confirmGroup != null)
            t_seq.Insert(t_confirmAt, this.m_confirmGroup.DOFade(1f, t_fade));

        // 잠금을 푸는 곳이 여기뿐이라, 빠지면 [확인]이 영영 잠긴 모달이 된다.
        t_seq.InsertCallback(t_confirmAt + t_fade, () => SetInputEnabled(true));

        t_seq.OnComplete(() => this.m_intro = null);
        return t_seq;
    }

    // 등장 직전 상태. 딤 말고는 아무것도 화면에 없어야 한다.
    void PrimeIntro()
    {
        for (int t_i = 0; t_i < this.m_shownRows; t_i++)
        {
            Transform t_row = this.rowRoot.GetChild(t_i);

            t_row.DOKill();
            t_row.localScale = Vector3.one * Mathf.Max(0.01f, this.rowFromScale);
            GroupOf(t_row.gameObject).alpha = 0f;
        }

        if (this.confirmButton != null)
        {
            this.m_confirmGroup = GroupOf(this.confirmButton.gameObject);
            this.m_confirmGroup.DOKill();
            this.m_confirmGroup.alpha = 0f;
        }
    }

    // 안무가 잘린 자리에서 꺼진 행도 그 상태로 다시 켜지므로 꺼진 행까지 훑어 원복한다.
    void ResetChoreography()
    {
        if (this.rowRoot != null)
            for (int t_i = 0; t_i < this.rowRoot.childCount; t_i++)
            {
                Transform t_row = this.rowRoot.GetChild(t_i);

                t_row.DOKill();
                t_row.localScale = Vector3.one;
                GroupOf(t_row.gameObject).alpha = 1f;
            }

        if (this.m_confirmGroup != null)
        {
            this.m_confirmGroup.DOKill();
            this.m_confirmGroup.alpha = 1f;
        }
    }

    void KillIntro()
    {
        if (this.m_intro != null && this.m_intro.IsActive()) this.m_intro.Kill();
        this.m_intro = null;
    }

    void SetInputEnabled(bool _enabled)
    {
        if (this.confirmButton != null) this.confirmButton.interactable = _enabled;
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : gameObject;

    static CanvasGroup GroupOf(GameObject _go)
    {
        var t_group = _go.GetComponent<CanvasGroup>();
        return t_group != null ? t_group : _go.AddComponent<CanvasGroup>();
    }
}
