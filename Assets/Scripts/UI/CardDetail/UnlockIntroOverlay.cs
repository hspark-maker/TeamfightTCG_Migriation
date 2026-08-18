using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 방금 열린 개념(키워드·시너지)을 전면에서 한 번 가르치는 오버레이.
// 무엇을 보여줄지는 호출부가 정하고(UnlockIntro 목록), 여기는 세우고 [확인]을 기다린다 —
// 그래서 "처음 보는 것인가"라는 판정을 알 필요가 없다(CardRewardOverlay가 지급을 모르는 것과 같은 규약).
//
// 씬에 저작하지 않고 Resources에서 세운다(CardRewardOverlay와 같은 규약) — 로비 캔버스에 중첩하면
// 그 프리팹을 저장할 때마다 다른 탭의 저작이 함께 흔들린다.
//
// ⚠ 딤을 눌러 닫히지 않는다. 읽어야 넘어가는 자리라 나가는 문은 [확인] 하나뿐이다.
//
// 여러 개가 한 방에 열려도 화면은 한 장이다 — 한 장씩 넘기게 하면 확인 탭이 개수만큼 늘어나는데
// 정작 읽는 시간은 늘지 않는다. 대신 행이 하나씩 들어와 "몇 개가 열렸는지"가 박자로 읽힌다.
//
// 행의 등장은 **자리가 아니라 배율**로 준다. 행은 레이아웃 그룹에 매달릴 물건이라
// anchoredPosition을 밀면 리빌드가 매 프레임 되돌려 안무가 통째로 안 보인다(진화 연출의 stageFitter와 같은 함정).
public class UnlockIntroOverlay : MonoBehaviour
{
    static UnlockIntroOverlay s_instance;

    /// <summary>안내가 떠 있는가. 로비 쪽 안내가 이 위에 겹치지 않게 볼 때 쓴다.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>닫힌 직후. 이 시점엔 IsOpen이 이미 false다.</summary>
    public static event Action OnAnyClosed;

    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] Button confirmButton;

    [Tooltip("행이 깔리는 노드. 행은 프리팹에 미리 깔아 둔다 — 런타임 Instantiate 없음(상세창 칩과 같은 규약).\n" +
             "자식은 UnlockIntroRow를 단 노드여야 한다.")]
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

    [Tooltip("행이 다 뜬 뒤 [확인]이 열리기까지의 뜸. 이 구간이 읽는 시간이다 — " +
             "0이면 손이 글보다 빨라져 읽지 않고 넘어간다.")]
    [SerializeField] float confirmDelay = 0.5f;
    [SerializeField] float confirmFadeDuration = 0.16f;

    // 진행 중 등장 안무. 확인·닫기가 도중에 와도 저작 상태로 되돌린 뒤 이어가야 한다.
    Sequence m_intro;

    // 확인 콜백. 한 번 쓰면 비워 연타를 막는다.
    Action m_onClose;

    CanvasGroup m_confirmGroup;

    // 이번에 세운 행 수. 등장 안무와 정리가 같은 수를 봐야 한다.
    int m_shownRows;

    // 지금 돌고 있는 데모 무대. 화면이 걷힐 때 반드시 함께 걷어야 한다 —
    // 남으면 안 보이는 자리에서 카메라가 계속 RenderTexture를 그린다.
    KeywordDemoStage m_demo;

    // 프리팹에 깔린 행이 모자란 것은 저작 문제다 — 매 표시마다 경고하면 로그가 묻힌다.
    static bool s_rowShortageWarned;

    /// <summary>안내 오버레이를 얻는다. 평소 꺼져 있는 노드라 이미 선 것을 찾을 때는 비활성까지 뒤진다
    /// (CardRewardOverlay와 같은 규약).</summary>
    public static bool TryGet(out UnlockIntroOverlay _overlay)
    {
        if (s_instance == null)
            s_instance = FindFirstObjectByType<UnlockIntroOverlay>(FindObjectsInactive.Include);

        if (s_instance == null)
        {
            var t_prefab = RuntimeUiPrefabs.Get(ERuntimeUiPrefab.UnlockIntroOverlay);
            if (t_prefab == null)
            {
                Debug.LogWarning("[UnlockIntroOverlay] Boot 카탈로그에서 해금 안내 프리팹을 찾지 못했습니다.");
            }
            else
            {
                var t_go = Instantiate(t_prefab);
                s_instance = t_go.GetComponent<UnlockIntroOverlay>();

                // 컴포넌트가 없으면 세운 것이 화면을 덮은 채 남는다 — 부를 때마다 한 장씩 쌓이므로 즉시 걷는다.
                if (s_instance == null)
                {
                    Debug.LogWarning("[UnlockIntroOverlay] 카탈로그 프리팹에 UnlockIntroOverlay가 없습니다(프리팹 배선 확인).");
                    Destroy(t_go);
                }
            }
        }

        _overlay = s_instance;
        return _overlay != null;
    }

    /// <summary>_intros를 세우고 [확인]을 기다린다. _onClose는 걷힌 뒤 정확히 한 번 온다.
    /// 세울 것이 하나도 없으면 뜨지 않고 _onClose를 곧바로 흘린다 — 호출부가 빈 목록을 걸러야 할 이유가 없다.
    ///
    /// _card는 데모 무대의 공격자로 선다(<see cref="KeywordDemoStage"/>). null이면 데모 없이 글자만 —
    /// 배선·저작이 덜 된 상태에서도 안내 자체는 성립해야 한다.
    ///
    /// <returns>실제로 세운 줄 수(앞에서부터). 프리팹에 깔린 줄보다 많이 넘기면 뒤쪽은 뜨지 않으므로,
    /// **본 것으로 낙인찍는 쪽은 이 수만큼만** 찍어야 한다 — 안 뜬 안내가 본 것이 되면 영영 못 본다.</returns></summary>
    public int Show(IReadOnlyList<UnlockIntro> _intros, CardData _card, Action _onClose)
    {
        // 직전 표시의 안무를 걷는다 — 시퀀스에 중첩된 트윈은 대상의 DOKill이 잡지 못해 새 안무와 같은 노드를 함께 민다.
        KillIntro();
        EndDemo();

        this.m_shownRows = BuildRows(_intros);

        // 세울 것이 없거나(빈 목록) 하나도 못 세웠으면(배선 실패) 닫을 수단만 남은 빈 판이 된다 — 그냥 흘려보낸다.
        if (this.m_shownRows == 0)
        {
            this.m_onClose = null;
            _onClose?.Invoke();
            return 0;
        }

        this.m_onClose = _onClose;

        if (this.confirmButton != null)
        {
            this.confirmButton.onClick.RemoveAllListeners();   // 재표시마다 중복 등록 방지
            this.confirmButton.onClick.AddListener(OnConfirmClicked);
        }

        IsOpen = true;
        SetVisible(true);

        // 등장이 도는 동안은 손을 막는다 — 다 서기 전에 눌러 닫히면 무엇이 열렸는지 못 본다.
        SetInputEnabled(false);

        BeginDemo(_intros, _card);

        this.m_intro = BuildIntro();
        this.m_intro.Play();

        return this.m_shownRows;
    }

    /// <summary>밖에서 걷는다(화면이 통째로 넘어가는 경로). 콜백은 흘리지 않는다 —
    /// 이 길로 닫는 쪽은 이미 자기 흐름을 쥐고 있다.</summary>
    public void Hide()
    {
        this.m_onClose = null;
        KillIntro();
        EndDemo();

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        SetVisible(false);
        ResetChoreography();

        if (t_wasOpen) OnAnyClosed?.Invoke();
    }

    // 잠금은 등장 안무가 푼다. Show를 거치지 않고 뜨는 경로(부모가 다시 켜짐)에서는 그 안무가 없어
    // [확인]이 잠긴 모달로 남으므로, 켜질 때 일단 열어 둔다(Show는 이 뒤에 다시 잠근다).
    void OnEnable()
    {
        SetInputEnabled(true);
    }

    // 오버레이는 자기 자신이 토글 대상이라 OnDisable이 정상 동작한다 — 잘린 퇴장 마무리를 여기서 위임한다.
    void OnDisable()
    {
        this.transition.HandleDisabled(ResolveTarget());
        KillIntro();
        EndDemo();
        ResetChoreography();

        // 꺼진 화면은 떠 있는 것이 아니다. Hide를 거치지 않고 꺼지는 경로(부모 비활성·씬 언로드)에서
        // 이 플래그가 남으면 "로비 표면이 보이는가" 판정이 영영 false가 된다.
        IsOpen = false;
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;

        // 열린 채 씬이 바뀌면 플래그가 남아 다음 씬의 안내가 영영 억제된다.
        IsOpen = false;
    }

    void OnConfirmClicked()
    {
        // 콜백을 먼저 비워 연타로 두 번 흐르는 경로를 막는다.
        var t_callback = this.m_onClose;
        this.m_onClose = null;
        if (t_callback == null) return;

        SetInputEnabled(false);

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        KillIntro();
        EndDemo();
        SetVisible(false);
        ResetChoreography();

        if (t_wasOpen) OnAnyClosed?.Invoke();

        // 넘겨주기는 정리가 다 끝난 뒤다 — 받는 쪽이 이 화면의 상태를 다시 물어볼 수 있어야 한다.
        t_callback.Invoke();
    }

    // 행을 채운다. 프리팹에 미리 깔린 것을 꺼내 쓰고 남는 것은 끈다(상세창 칩 TryShowChip과 같은 규약).
    // 돌려주는 값은 실제로 세운 수 — 모자라면 거기서 멈춘다(빈 칸을 세우느니 덜 보여주는 편이 낫다).
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
            if (t_view == null) continue;   // 행이 아닌 장식 노드가 섞여 있어도 안무가 성립해야 한다

            t_view.Bind(_intros[t_i]);
            t_view.SetDemo(null);           // 띠는 BeginDemo가 딱 한 줄에만 켠다
            t_row.gameObject.SetActive(true);
            t_used++;
        }

        for (int t_i = t_used; t_i < this.rowRoot.childCount; t_i++)
            this.rowRoot.GetChild(t_i).gameObject.SetActive(false);

        return t_used;
    }

    /// <summary>데모를 딱 한 줄에 세운다.
    ///
    /// ⚠ <b>무대·카메라가 하나뿐이라 동시에 두 개를 돌릴 수 없다.</b> 키워드가 둘 이상 열린 판에서는
    /// 맨 위 키워드 행에만 띠가 뜨고 나머지는 글자로 남는다 — 무대를 행 수만큼 복제하면
    /// 카메라와 RenderTexture가 그만큼 늘어나는데, 정작 눈은 한 번에 하나만 본다.
    /// 시너지는 Keyword가 None이라 자연히 건너뛴다(덱 편성 규칙이라 보여줄 대본이 없다).</summary>
    void BeginDemo(IReadOnlyList<UnlockIntro> _intros, CardData _card)
    {
        if (_card == null || this.rowRoot == null) return;

        for (int t_i = 0; t_i < this.m_shownRows && t_i < _intros.Count; t_i++)
        {
            if (_intros[t_i].Keyword == CardKeyword.None) continue;

            var t_view = this.rowRoot.GetChild(t_i).GetComponent<UnlockIntroRow>();
            if (t_view == null) continue;

            if (!KeywordDemoStage.TryGet(out KeywordDemoStage t_stage)) return;

            Texture t_tex = t_stage.Begin(_card, _intros[t_i].Keyword);
            if (t_tex == null) { t_stage.End(); return; }   // 저작이 덜 됐다 — 띠 없이 글자만 남긴다

            this.m_demo = t_stage;
            t_view.SetDemo(t_tex);
            return;
        }
    }

    // 무대를 걷는다. 텍스처가 해제되므로 **띠에서 먼저 떼어야** 한다 — 순서가 뒤집히면
    // 죽은 RenderTexture를 물고 있는 RawImage가 한 프레임 남는다.
    void EndDemo()
    {
        KeywordDemoStage t_demo = this.m_demo;
        this.m_demo = null;
        if (t_demo == null) return;

        if (this.rowRoot != null)
            for (int t_i = 0; t_i < this.rowRoot.childCount; t_i++)
                this.rowRoot.GetChild(t_i).GetComponent<UnlockIntroRow>()?.SetDemo(null);

        t_demo.End();
    }

    // 등장 안무. 딤만 먼저 깔리고 행이 하나씩 앉은 뒤에야 [확인]이 열린다.
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

        // 손은 버튼이 다 뜬 뒤에 돌려준다. 잠금을 푸는 곳이 여기뿐이라, 빠지면 [확인]이 영영 잠긴 모달이 된다.
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

    // 다음 표시가 중간값(줄어든 배율·반투명)에서 시작하지 않게 원복. 꺼진 행까지 훑는다 —
    // 안무가 잘린 자리에서 꺼진 행은 그 상태 그대로 다음 표시에 다시 켜진다.
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
