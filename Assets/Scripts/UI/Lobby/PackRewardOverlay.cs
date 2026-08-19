using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 카드팩 한 개를 세워 "이 팩이 도착했다"만 알리는 예고 오버레이.
// 표시와 닫힘 콜백만 담당하고 지급·구매는 하지 않는다 — 실제 획득은 이 뒤의 상점 흐름 몫이다.
// 씬에 저작하지 않고 Addressables 타입 색인에서 독립 Canvas로 세운다(두 카드 보상 오버레이와 같은 규약).
//
// ⚠ 딤을 눌러 닫히지 않는다. 나가는 문은 [확인] 하나뿐이다 — 닫는 순간이 팩이 탭으로 날아가는 순간이라
//   유저가 모르게 새어 나가면 그 비행이 어디서 출발한 것인지 읽히지 않는다.
//   비행 자체는 이 화면 밖(LobbyGainEffectDirector)이 맡는다 — 카드 보상이 도감 탭으로 넘기는 것과 같은 규약이다.
//
// 팩은 가만히 서 있지 않는다. 축이 둘이고 서로 만지는 노드가 다르다 —
//   등장(내려꽂힘·착지 펀치)은 여기서 packRoot를, 부유·펄스는 PackIdleMotion이 그 자식을 만진다.
//   같은 노드를 두 축이 함께 쥐면 매 프레임 서로를 덮어쓴다(PackIdleMotion 주석의 packRoot/visual 분리와 같은 규약).
//
// 카드 보상 오버레이(CardRewardOverlay)를 확장하지 않고 따로 세운 이유는 그쪽 안무가 전부 카드 1장 전제이기 때문이다 —
// 림라이트·NEW는 카드를 사건으로 만드는 장치라 팩 그림에는 걸리지 않는다.
public class PackRewardOverlay : SingletonOverlay<PackRewardOverlay>
{
    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] TMP_Text titleText;
    [SerializeField] Button confirmButton;

    [Tooltip("팩 그림. CardPackData.PackArt를 그대로 얹는다.")]
    [SerializeField] Image packImage;

    [Tooltip("팩 이름(옵션). 미배선이면 제목만 뜬다.")]
    [SerializeField] TMP_Text packNameText;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("등장 안무가 미는 노드. 팩 그림 자신이 아니라 그 부모여야 한다 — "
           + "그림 쪽은 아이들 부유(PackIdleMotion)가 쥐고 있다. 미배선이면 등장 안무 없이 그냥 뜬다.")]
    [SerializeField] RectTransform packRoot;

    [Tooltip("팝업이 뜬 뒤 팩이 출발하기까지의 뜸. 이 사이엔 제목만 있고 팩은 화면에 없다.")]
    [SerializeField] float packDropDelay = 0.06f;

    [Tooltip("팩이 이만큼 위에서 출발한다(px).")]
    [SerializeField] float packDropDistance = 380f;

    [Tooltip("출발 배율(제자리 대비). 크게 다가와 있어야 내려앉는 순간이 사건이 된다.")]
    [SerializeField] float packDropScale = 1.35f;

    [Tooltip("내려앉는 시간. 길면 '떨어진다'가 되고 짧아야 '꽂힌다'가 된다.")]
    [SerializeField] float packDropDuration = 0.13f;

    [Tooltip("착지 순간 팩이 부푸는 양(0.08이면 8% 부풀었다 돌아온다. 0이면 펀치 없음).")]
    [SerializeField] float packLandPunch = 0.08f;

    [SerializeField] float packLandPunchDuration = 0.22f;

    [Tooltip("착지 뒤 [확인]이 열리기까지의 뜸. 이 구간이 무엇을 받았는지 읽는 시간이다 — "
           + "0이면 손이 팩보다 빨라져 보지도 못하고 넘어간다.")]
    [SerializeField] float confirmDelay = 0.45f;

    // 진행 중 등장 안무. 확인·닫기가 도중에 와도 저작 상태로 되돌린 뒤 이어가야 한다.
    Sequence m_intro;

    // 닫힘 콜백. 한 번 쓰면 비워 연타를 막는다.
    Action m_onClosed;

    // 팩의 제자리. 프리팹 저작값이 곧 제자리라 최초 1회만 캡처한다.
    Vector2 m_packHome;
    Vector3 m_packHomeScale = Vector3.one;
    bool m_homeCaptured;

    CanvasGroup m_packGroup;

    /// <summary>예고 오버레이를 얻는다. 씬에 저작해 두지 않고 Addressables 타입 색인에서 세운다.
    /// 평소 꺼져 있는 노드라 이미 선 것을 찾을 때는 비활성까지 뒤진다(카드 보상 오버레이와 같은 규약).</summary>
    public static bool TryGet(out PackRewardOverlay _overlay)
        => TryGetOrCreate(RuntimeOverlayPrefabs.Get<PackRewardOverlay>, out _overlay);

    /// <summary>팩이 서 있는 자리. 닫은 뒤 이어지는 비행이 여기서 출발해야
    /// "방금 본 그 팩이 팩 탭으로 갔다"가 한 줄로 이어진다(CardRewardOverlay.CardAnchor와 같은 규약).</summary>
    public RectTransform PackAnchor => this.packRoot;

    /// <summary>팩 하나를 띄운다. 나가는 문은 [확인] 하나뿐이다.
    /// <paramref name="_onClosed"/>는 화면이 걷힌 <b>뒤</b>에 불린다 — 그쪽이 이어지는 비행을 튼다
    /// (화면은 그 연출에 자리를 넘기고 걷힌다).</summary>
    public void Show(string _title, CardPackData _pack, Action _onClosed)
    {
        this.m_onClosed = _onClosed;
        this.KillIntro();

        if (this.titleText != null) this.titleText.text = _title;

        if (this.packImage != null)
        {
            Sprite t_art = _pack != null ? _pack.PackArt : null;
            this.packImage.sprite  = t_art;
            this.packImage.enabled = t_art != null;
        }

        if (this.packNameText != null) this.packNameText.text = _pack != null ? _pack.DisplayName : string.Empty;

        if (this.confirmButton != null)
        {
            this.confirmButton.onClick.RemoveListener(this.OnConfirmClicked);   // 재표시마다 중복 등록 방지
            this.confirmButton.onClick.AddListener(this.OnConfirmClicked);
        }

        IsOpen = true;
        this.SetVisible(true);
        this.CaptureHome();

        // 등장이 도는 동안은 손을 막는다 — 팩이 다 서기 전에 눌러 닫히면 무엇을 받았는지 못 본다.
        this.SetInputEnabled(false);

        this.m_intro = this.BuildIntro();
        this.m_intro.Play();
    }

    public void Hide()
    {
        this.KillIntro();

        bool t_wasOpen = IsOpen;
        IsOpen = false;

        this.SetVisible(false);
        this.ResetChoreography();

        if (t_wasOpen) RaiseClosed();
    }

    // 잠금은 등장 안무가 푼다. Show를 거치지 않고 뜨는 경로(부모가 다시 켜짐)에서는 그 안무가 없어
    // [확인]이 잠긴 모달로 남으므로, 켜질 때 일단 열어 둔다(Show는 이 뒤에 다시 잠근다).
    void OnEnable()
    {
        this.SetInputEnabled(true);
    }

    // 오버레이는 자기 자신이 토글 대상이라 OnDisable이 정상 동작한다 — 잘린 퇴장 마무리를 여기서 위임한다.
    void OnDisable()
    {
        this.transition.HandleDisabled(this.ResolveTarget());
        this.KillIntro();
        this.ResetChoreography();

        // 꺼진 화면은 떠 있는 것이 아니다. Hide를 거치지 않고 꺼지는 경로(부모 비활성·씬 언로드)에서
        // 이 플래그가 남으면 "로비 표면이 보이는가" 판정이 영영 false가 되어 뒤의 안내가 서지 못한다.
        IsOpen = false;
    }

    void OnConfirmClicked()
    {
        // 콜백을 먼저 비워 연타로 두 번 도는 경로를 막는다.
        var t_callback = this.m_onClosed;
        this.m_onClosed = null;
        if (t_callback == null) return;

        this.SetInputEnabled(false);

        // 화면을 먼저 걷고 콜백을 부른다. 그 콜백이 트는 비행의 종료 신호를 기다리는 쪽이
        // "팝업이 떠 있는 동안 온 신호"로 오인해 흘려보내지 않도록(OutgameTutorialBridge.OnCardGainFinished).
        this.Hide();

        t_callback.Invoke();
    }

    // 등장 안무. 팩만 위에서 내려앉고, 착지 한 프레임에 펀치를 몰아넣는다.
    Sequence BuildIntro()
    {
        this.PrimeIntro();

        var t_seq = DOTween.Sequence().SetLink(this.gameObject);
        float t_land = this.packDropDelay + this.packDropDuration;

        if (this.packRoot != null)
        {
            t_seq.InsertCallback(this.packDropDelay, () => { if (this.m_packGroup != null) this.m_packGroup.alpha = 1f; });
            t_seq.Insert(this.packDropDelay,
                         this.packRoot.DOAnchorPosY(this.m_packHome.y, this.packDropDuration).SetEase(Ease.InQuad));
            t_seq.Insert(this.packDropDelay,
                         this.packRoot.DOScale(this.m_packHomeScale, this.packDropDuration).SetEase(Ease.InQuad));

            if (this.packLandPunch > 0f)
                t_seq.InsertCallback(t_land, () => UiPunch.Play(this.packRoot, this.packLandPunch, this.packLandPunchDuration));
        }

        // 손은 팩이 다 선 뒤에 돌려준다. 잠금을 푸는 곳이 여기뿐이라, 빠지면 [확인]이 영영 잠긴 모달이 된다.
        t_seq.InsertCallback(t_land + this.confirmDelay, () => this.SetInputEnabled(true));

        t_seq.OnComplete(() => this.m_intro = null);
        return t_seq;
    }

    // 등장 직전 상태. 팩은 아직 화면에 없다.
    void PrimeIntro()
    {
        if (this.packRoot == null) return;

        this.packRoot.DOKill();
        this.packRoot.anchoredPosition = this.m_packHome + new Vector2(0f, this.packDropDistance);
        this.packRoot.localScale = this.m_packHomeScale * this.packDropScale;
        if (this.m_packGroup != null) this.m_packGroup.alpha = 0f;
    }

    // 팩의 제자리와 알파 손잡이를 확보한다. 프리팹 저작값이 곧 제자리라 최초 1회만.
    void CaptureHome()
    {
        if (this.m_homeCaptured || this.packRoot == null) return;
        this.m_homeCaptured = true;

        this.m_packHome      = this.packRoot.anchoredPosition;
        this.m_packHomeScale = this.packRoot.localScale;

        // 프리팹에 없으면 붙여 준다 — 배선 여부와 무관하게 안무가 성립해야 한다.
        this.m_packGroup = GroupOf(this.packRoot.gameObject);
    }

    // 다음 표시가 중간값(어긋난 자리·줄어든 배율·반투명)에서 시작하지 않게 원복.
    void ResetChoreography()
    {
        if (!this.m_homeCaptured || this.packRoot == null) return;

        this.packRoot.DOKill();
        this.packRoot.anchoredPosition = this.m_packHome;
        this.packRoot.localScale = this.m_packHomeScale;

        if (this.m_packGroup != null)
        {
            this.m_packGroup.DOKill();
            this.m_packGroup.alpha = 1f;
        }
    }

    static CanvasGroup GroupOf(GameObject _go)
    {
        var t_group = _go.GetComponent<CanvasGroup>();
        return t_group != null ? t_group : _go.AddComponent<CanvasGroup>();
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
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
