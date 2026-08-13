using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 카드 한 장을 크게 세워 보여주고 [획득]으로 받게 하는 보상 오버레이.
// 표시와 확인 콜백만 담당하고 지급은 호출자가 한다 — 그래서 출처(튜토리얼 보너스든 그 밖이든)를 알 필요가 없다.
// 씬에 저작하지 않고 Resources에서 세운다(LoadingCover와 같은 규약) — 로비 캔버스에 중첩하면
// 그 프리팹을 저장할 때마다 다른 탭의 저작이 함께 흔들린다.
//
// ⚠ 딤을 눌러 닫히지 않는다. 받아야 넘어가는 자리에 쓰는 물건이라 나가는 문은 [획득] 하나뿐이다.
public class CardRewardOverlay : MonoBehaviour
{
    const string ResourcePath = "UI/CardRewardOverlay";

    static CardRewardOverlay s_instance;

    /// <summary>보상 화면이 떠 있는가. 로비 쪽 안내가 이 위에 겹치지 않게 볼 때 쓴다.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>닫힌 직후. 이 시점엔 IsOpen이 이미 false다.</summary>
    public static event Action OnAnyClosed;

    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] TMP_Text titleText;
    [SerializeField] Button acquireButton;

    [Tooltip("보여줄 카드. 팩 개봉 낱장과 같은 물건이라 슬램·섬광·림라이트·NEW가 이미 배선돼 있다.")]
    [SerializeField] PackCardView cardView;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("카드가 서는 순간 화면이 반응하는 축. dim에 딤 이미지를 물린다(알파는 그대로, 색만 밀린다).")]
    [SerializeField] ScreenDimTint dimTint = new ScreenDimTint();

    [Tooltip("카드가 선 뒤 딤이 밝아졌다 돌아오는 시간.")]
    [SerializeField] float dimPulseDuration = 0.25f;

    // 등장 안무. 획득·닫기가 등장 도중에 와도 저작 상태로 되돌린 뒤 이어가야 한다.
    Sequence m_intro;

    // 획득 콜백. 한 번 쓰면 비워 연타를 막는다. 지급이 실패하든 말든 화면은 닫힌다 —
    // 받아야 넘어가는 자리라 여기서 가두면 탈출로가 없다.
    Action m_onAcquire;

    /// <summary>보상 오버레이를 얻는다. 씬에 저작해 두지 않고 Resources에서 세운다 —
    /// 로비 캔버스에 중첩하면 그 프리팹을 저장할 때마다 다른 탭의 저작이 함께 흔들린다(LoadingCover와 같은 이유).
    /// 평소 꺼져 있는 노드라 이미 선 것을 찾을 때는 비활성까지 뒤진다.</summary>
    public static bool TryGet(out CardRewardOverlay _overlay)
    {
        if (s_instance == null)
            s_instance = FindFirstObjectByType<CardRewardOverlay>(FindObjectsInactive.Include);

        if (s_instance == null)
        {
            var t_prefab = Resources.Load<GameObject>(ResourcePath);
            if (t_prefab == null)
            {
                Debug.LogWarning($"[CardRewardOverlay] Resources/{ResourcePath} 를 찾지 못해 보상 화면을 세울 수 없습니다.");
            }
            else
            {
                var t_go = Instantiate(t_prefab);
                s_instance = t_go.GetComponent<CardRewardOverlay>();

                // 컴포넌트가 없으면 세운 것이 화면을 덮은 채 남는다 — 부를 때마다 한 장씩 쌓이므로 즉시 걷는다.
                if (s_instance == null)
                {
                    Debug.LogWarning($"[CardRewardOverlay] Resources/{ResourcePath} 에 CardRewardOverlay가 없습니다(프리팹 배선 확인).");
                    Destroy(t_go);
                }
            }
        }

        _overlay = s_instance;
        return _overlay != null;
    }

    /// <summary>카드 한 장을 띄운다. _onAcquire는 [획득]에서 <b>화면이 닫힌 뒤</b> 불린다 —
    /// 그때 지급하고, 이어지는 획득 연출도 그쪽이 튼다.</summary>
    public void Show(string _title, CardData _card, Action _onAcquire)
    {
        this.m_onAcquire = _onAcquire;

        // 직전 표시의 안무를 걷는다 — 시퀀스에 중첩된 트윈은 대상의 DOKill이 잡지 못해 새 안무와 같은 노드를 함께 민다.
        this.KillIntro();

        if (this.titleText != null) this.titleText.text = _title;

        // 보상으로 주는 카드는 언제나 새 카드로 세운다 — 중복 표식(탈채도·환급 칩)이 설 자리가 아니다.
        if (this.cardView != null) this.cardView.Bind(new DrawnCard(_card, true, 0L));

        if (this.acquireButton != null)
        {
            this.acquireButton.onClick.RemoveAllListeners();   // 재표시마다 중복 등록 방지
            this.acquireButton.onClick.AddListener(this.OnAcquireClicked);
        }

        IsOpen = true;
        this.SetVisible(true);
        this.dimTint.Capture();

        // 등장이 도는 동안은 손을 막는다 — 카드가 다 서기 전에 눌러 닫히면 무엇을 받았는지 못 본다.
        this.SetInputEnabled(false);

        // 카드 안무는 패널이 다 선 뒤에 터져야 "이 카드가 등장했다"로 읽힌다.
        this.m_intro = DOTween.Sequence().SetLink(this.gameObject);
        this.m_intro.AppendInterval(this.transition.OpenDuration);
        this.m_intro.AppendCallback(this.PlayCardReveal);

        // 손을 여기서 돌려준다 — 잠금을 푸는 곳이 등장 안무뿐이라, 빠지면 [획득]이 영영 잠긴 모달이 된다.
        this.m_intro.AppendCallback(() => this.SetInputEnabled(true));

        this.m_intro.Append(this.dimTint.TweenLevel(1f, this.dimPulseDuration * 0.4f));
        this.m_intro.Append(this.dimTint.TweenLevel(0f, this.dimPulseDuration));
        this.m_intro.OnComplete(() => this.m_intro = null);
        this.m_intro.Play();
    }

    public void Hide()
    {
        this.m_onAcquire = null;
        this.KillIntro();
        this.dimTint.Reset();

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        this.SetVisible(false);

        if (t_wasOpen) OnAnyClosed?.Invoke();
    }

    // 잠금은 등장 안무가 푼다. Show를 거치지 않고 뜨는 경로(부모가 다시 켜짐)에서는 그 안무가 없어
    // [획득]이 잠긴 모달로 남으므로, 켜질 때 일단 열어 둔다(Show는 이 뒤에 다시 잠근다).
    void OnEnable()
    {
        this.SetInputEnabled(true);
    }

    // 오버레이는 자기 자신이 토글 대상이라 OnDisable이 정상 동작한다 — 잘린 퇴장 마무리를 여기서 위임한다.
    void OnDisable()
    {
        this.transition.HandleDisabled(this.ResolveTarget());
        this.KillIntro();
        this.dimTint.Reset();

        // 꺼진 화면은 떠 있는 것이 아니다. Hide를 거치지 않고 꺼지는 경로(부모 비활성·씬 언로드)에서
        // 이 플래그가 남으면 "로비 표면이 보이는가" 판정이 영영 false가 되어 뒤의 안내가 서지 못한다.
        IsOpen = false;
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;

        // 열린 채 씬이 바뀌면 플래그가 남아 다음 씬의 안내가 영영 억제된다.
        IsOpen = false;
    }

    void OnAcquireClicked()
    {
        // 콜백을 먼저 비워 연타로 두 번 지급되는 경로를 막는다(호출자 가드와 이중 방어).
        var t_callback = this.m_onAcquire;
        this.m_onAcquire = null;
        if (t_callback == null) return;

        this.SetInputEnabled(false);

        // 닫는 것이 먼저다. 이 화면이 떠 있는 동안의 연출 종료는 "보상을 받아서 난 것"이 아니라서
        // 기다리는 쪽이 흘려보내는데(OutgameTutorialBridge.OnCardGainFinished), 지급이 그 안에서 트는 연출은
        // 흘려보내면 안 된다 — IsOpen을 먼저 내려 두 경우가 갈리게 한다.
        this.Hide();

        // 지급·영속은 이 호출에서 끝난다. 이어지는 획득 연출도 그쪽이 튼다.
        t_callback.Invoke();
    }

    // 한 장이 드러나는 순간의 강조. 결과 격자용 ApplyResultContrast는 부르지 않는다 —
    // 그쪽은 "놓여 있는 상태"라 이 자리의 등장 안무를 덮는다(PackRevealView와 같은 호출).
    void PlayCardReveal()
    {
        if (this.cardView != null) this.cardView.PlayRevealAccent();
    }

    void KillIntro()
    {
        if (this.m_intro != null && this.m_intro.IsActive()) this.m_intro.Kill();
        this.m_intro = null;
    }

    void SetInputEnabled(bool _enabled)
    {
        if (this.acquireButton != null) this.acquireButton.interactable = _enabled;
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
