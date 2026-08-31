using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;   // 분출 시퀀스의 OnComplete·Play 확장 메서드
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 보상 수령 팝업. 표시와 확인 콜백만 담당하고 지급은 호출자가 자기 매니저에 위임한다 —
// 그래서 랭크 티어든 앨범 완성이든 출처를 알 필요가 없다(제목 + RewardLine 목록이면 뜬다).
// 씬에 직접 저작되므로 PooledUIBase가 아니라 SetActive 토글로 동작한다.
//
// ⚠ 어느 탭에도 속하지 않는 공용 1개다. 랭크 오버레이 밑에 두면 그 오버레이를 켜야만 뜨므로,
//   앨범 수령에서 랭크 보상 목록이 뒤에 같이 켜진다 — 반드시 두 오버레이의 형제로 선다.
public class RewardClaimPopup : SingletonOverlay<RewardClaimPopup>
{
    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] TMP_Text titleText;  // 티어 표시명(선택)
    [SerializeField] Button claimButton;  // [획득]

    [Tooltip("보상 칸(아이콘 + 수량). 저작한 보상이 칸 수보다 적으면 남는 칸은 꺼진다.")]
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;

    [Tooltip("딤(팝업 배경). 등장 연출이 도는 동안 이 버튼을 잠가 오조작으로 닫히지 않게 한다.")]
    [SerializeField] Button dimButton;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("등장·퇴장 안무. 아무것도 배선하지 않으면 이 축을 통째로 건너뛰고 예전처럼 페이드만 남는다.")]
    [SerializeField] RewardRevealFx reveal = new RewardRevealFx();

    [Tooltip("획득 순간 화면이 반응하는 축. dim에 딤 이미지를 물린다(알파는 그대로, 색만 밀린다).")]
    [SerializeField] ScreenDimTint dimTint = new ScreenDimTint();

    [Tooltip("빛이 피어날 자리의 폴백(선택). 평소엔 그 재화의 보상 아이콘 자리에서 피므로 쓰이지 않는다 — " +
             "아이콘을 배선하지 않은 칸에서만 여기로 내려온다.")]
    [SerializeField] RectTransform burstOrigin;

    static bool s_overflowWarned;

    // 등장 안무. 수령·닫기가 등장 도중에 와도 저작 상태로 되돌린 뒤 이어가야 한다.
    Sequence m_intro;

    // 확인 콜백. 던지고 기다리지 않으므로 반환값은 즉시 완료된 경우(로컬 가드 거절)에만 본다.
    // 중복 클릭 방지를 위해 한 번 쓰면 비운다.
    Func<UniTask<RewardClaimOutcome>> m_onConfirm;

    // 닫힘 콜백. 연 쪽이 팝업 뒤에 연출을 이을 때만 쓴다(공용 팝업이라 static 이벤트로 두면 다른 소비처에 샌다).
    Action m_onClosed;

    // 분출량(표시 목록의 예고로 Show가 채운다 — 응답을 기다리지 않아 실지급을 알 수 없다).
    // 재화가 갈리면 각자의 HUD로 각자의 빛이 흘러가므로 종류별로 담아 둔다.
    readonly CurrencyGainBucket m_rewards = new CurrencyGainBucket();

    // 재화별로 빛이 피어날 자리(그 재화가 걸린 보상 아이콘). 아이콘 자리에서 피어야 "그것이 빛이 됐다"로 읽힌다.
    readonly RectTransform[] m_origins = new RectTransform[(int)ECurrencyType.Count];

    // 이 팝업이 세운 분출. 딤을 눌러 닫고 다음 행을 바로 수령할 수 있어 두 연출이 겹친다.
    Sequence m_burst;

    /// <summary>
    /// 씬의 공용 팝업을 얻는다. 평소 꺼져 있는 노드라 비활성까지 뒤진다 —
    /// 자가 설치는 하지 않는다(저작된 빛·리본·버튼이 있어 코드로 세울 수 있는 물건이 아니다).
    /// </summary>
    public static bool TryGet(out RewardClaimPopup _popup)
        => TryGetExisting(out _popup);

    /// <summary>
    /// 보상을 띄운다. _onConfirm은 던지고 기다리지 않는다 — 수령을 누르면 서버 왕복이 뒤에서 도는 동안
    /// 화면은 곧장 획득 연출로 넘어간다(그래서 반환값도 보지 않는다).
    /// <para>_rewards는 <b>수령 전 예고</b>다(클라 스펙). 분출·롤업이 이 목록으로 서므로 실지급과 갈릴 수 있고,
    /// 그때는 연출이 끝나 고정이 풀릴 때 HUD가 서버 잔액으로 맞춰진다.</para>
    /// </summary>
    public void Show(string _title, IReadOnlyList<RewardLine> _rewards, Func<UniTask<RewardClaimOutcome>> _onConfirm,
                     bool _claimOnDim = false, Action _onClosed = null)
    {
        this.m_onConfirm = _onConfirm;
        this.m_onClosed = _onClosed;

        // 직전 표시의 안무를 걷는다 — 시퀀스에 중첩된 트윈은 대상의 DOKill이 잡지 못해 새 안무와 같은 노드를 함께 민다.
        this.KillIntro();

        // 직전 분출의 소유권도 뗀다. 안 떼면 옛 분출의 종료 콜백이 방금 연 이 팝업을 닫는다(수령 경로와 같은 이유).
        this.m_burst = null;

        var t_rewards = _rewards ?? Array.Empty<RewardLine>();

        // 분출량은 표시 목록(예고)으로 선다 — 응답을 기다리지 않으므로 서버 실지급을 알 방법이 없다.
        // 슬롯 수를 넘긴 보상도 담는다(표시만 앞칸으로 잘릴 뿐, 지급은 전부 이뤄진다).
        this.m_rewards.Clear();
        for (int t_i = 0; t_i < t_rewards.Count; t_i++) this.m_rewards.Add(t_rewards[t_i].Gain);

        if (this.titleText != null) this.titleText.text = _title;
        this.BindRewardSlots(t_rewards);

        if (this.claimButton != null)
        {
            this.claimButton.gameObject.SetActive(!_claimOnDim);
            this.claimButton.onClick.RemoveAllListeners(); // 재표시마다 중복 등록 방지
            this.claimButton.onClick.AddListener(this.ClaimClicked);
        }

        if (this.dimButton != null)
        {
            this.dimButton.onClick.RemoveAllListeners();
            if (_claimOnDim) this.dimButton.onClick.AddListener(this.ClaimClicked);
            else this.dimButton.onClick.AddListener(this.Hide);
        }

        this.SetVisible(true);

        this.dimTint.Capture();
        this.reveal.ApplyCount(t_rewards.Count);

        // 등장이 도는 동안은 손을 막는다 — 보상이 다 서기 전에 눌러 닫히면 무엇을 받았는지 못 본다.
        this.SetInputEnabled(false);

        this.m_intro = this.reveal.BuildIntro(this.rewardSlots, this.dimTint);
        this.m_intro.InsertCallback(this.reveal.IntroDuration, () => this.SetInputEnabled(true));
        this.m_intro.SetLink(this.gameObject).Play();
    }

    public void Hide()
    {
        this.m_onConfirm = null;
        this.KillIntro();
        this.SetVisible(false);

        // 먼저 비우고 부른다 — 콜백이 이 팝업을 다시 열어도(Show) 방금 심은 콜백을 덮지 않게.
        var t_closed = this.m_onClosed;
        this.m_onClosed = null;
        t_closed?.Invoke();
    }

    // 잠금은 등장 안무가 푼다. Show를 거치지 않고 뜨는 경로(부모가 다시 켜짐)에서는 그 안무가 없어
    // [획득]도 딤도 잠긴 모달로 남으므로, 켜질 때 일단 열어 둔다(Show는 이 뒤에 다시 잠근다).
    void OnEnable()
    {
        this.SetInputEnabled(true);
    }

    // 팝업은 자기 자신이 토글 대상이라 OnDisable이 정상 동작한다 — 잘린 퇴장 마무리와 표시 원복을 여기서 위임한다.
    // 분출 시퀀스는 죽이지 않는다(팝업 밖 노드에서 도는 연출이라 끊으면 코인이 허공에 굳는다).
    void OnDisable()
    {
        this.transition.HandleDisabled(this.ResolveTarget());
        this.RestoreReveal();
    }

    void ClaimClicked()
    {
        // 콜백을 먼저 비워 연타로 두 번 지급되는 경로를 막는다(매니저 가드와 이중 방어).
        var t_callback = this.m_onConfirm;
        this.m_onConfirm = null;

        this.SetInputEnabled(false);

        // 왕복을 기다리지 않는다 — 지급·영속은 뒤에서 마저 끝나고, 화면은 이 프레임에 획득으로 넘어간다.
        var t_claim = t_callback != null ? t_callback.Invoke() : UniTask.FromResult(default(RewardClaimOutcome));

        // 다만 로컬 가드에 걸린 거절(상태가 어긋남·이미 날아간 같은 보상)은 왕복 없이 이 자리에서 판정된다 —
        // 그때는 줄 것이 없으니 소리도 연출도 없다. 서버까지 간 거절은 알 길이 없어 그대로 연출이 돈다.
        if (t_claim.Status.IsCompleted())
        {
            if (!t_claim.GetAwaiter().GetResult().Succeeded)
            {
                this.Hide();
                return;
            }
        }
        else
        {
            t_claim.Forget();
        }

        SoundManager.Instance?.PlayCue(EOutgameSound.RewardClaim);

        if (!CurrencyGainEffectPlayer.TryGet(this, out var t_player))
        {
            this.Hide();
            return;
        }

        // 직전 연출을 새 롤업 고정(BuildLightGain 안의 BeginGainRollUp)보다 먼저 마무리한다 — 순서가 뒤집히면
        // 옛 도착 콜백이 새 고정을 덮어 숫자가 뒤로 튄다(로비 획득 연출과 같은 이유).
        // 소유권을 먼저 떼야 옛 시퀀스의 종료 콜백이 이번에 다시 연 팝업을 닫지 않는다.
        var t_prev = this.m_burst;
        this.m_burst = null;
        if (t_prev != null && t_prev.IsActive()) t_prev.Complete(true);

        // 등장이 아직 돌고 있으면 걷어 저작 상태로 되돌린다 — 중간값에서 퇴장을 이어 받으면 아이콘이 튄다.
        this.RestoreReveal();

        // 재화가 갈리면 줄기도 갈려야 한다 — 공용 재생기가 종류별 시퀀스를 조립하고 수치 고정 해제 안전망까지 붙여 온다.
        // 잔액은 아직 오르지 않았으므로 롤업 목표를 예고량으로 세운다(_optimistic) — 안 그러면 숫자가 내려갔다 제자리로 온다.
        var t_gain = t_player.BuildLightGain(this.m_rewards, this.m_origins, this.reveal.LightSprite,
                                             _optimistic: true);
        if (t_gain == null)
        {
            this.Hide();
            return;
        }

        // 연출 레이어가 랭크 오버레이보다 아래면 빛이 딤에 가린다(로비 획득 연출과 같은 처리).
        // 빛이 아이콘 '위'에 떠야 아이콘이 그 아래서 사라진 것으로 읽히므로, 이 한 줄이 연출의 전제다.
        t_player.transform.SetAsLastSibling();

        // 퇴장 안무와 빛 줄기를 한 시간축에 놓는다 — 아이콘이 사그라드는 구간에 빛이 피어야 그것이 변한 것으로 읽힌다.
        // 마스터에 SetLink를 걸지 않는 이유는 기존과 같다: 팝업이 꺼질 때 죽으면 빛이 허공에 굳는다.
        var t_burst = DOTween.Sequence();
        this.reveal.BuildOutro(t_burst, this.dimTint);
        t_burst.Insert(this.reveal.LaunchAt, t_gain);

        this.m_burst = t_burst;

        // 정상 완료와 강제 종료를 한 경로로 덮는다 — OnComplete만 두면 시퀀스가 끊겼을 때
        // 팝업이 [획득] 비활성 상태로 열린 채 남는다. autoKill은 전역 기본값에 기대지 않고 명시한다.
        t_burst.SetAutoKill(true).OnKill(this.OnBurstEnded);

        // BuildLightGain은 재생을 호출자에게 맡긴다 — 전역 autoPlay 설정에 기대지 않고 여기서 명시적으로 돌린다.
        t_burst.Play();
    }

    void KillIntro()
    {
        if (this.m_intro != null && this.m_intro.IsActive()) this.m_intro.Kill();
        this.m_intro = null;
    }

    // 모든 축을 저작 상태로 되돌린다. 화면에 남아 있는 동안 부르면 안 된다 —
    // 빨려들어 사라진 아이콘이 퇴장 페이드 동안 되살아난다.
    void RestoreReveal()
    {
        this.KillIntro();

        this.reveal.Reset();
        this.dimTint.Reset();
    }

    void SetInputEnabled(bool _enabled)
    {
        if (this.claimButton != null) this.claimButton.interactable = _enabled;
        if (this.dimButton != null) this.dimButton.interactable = _enabled;
    }

    // 이 팝업이 소유한 분출이 끝났을 때만 닫는다(소유권을 넘긴 뒤 끝난 옛 연출은 무시).
    void OnBurstEnded()
    {
        if (this.m_burst == null) return;

        this.m_burst = null;
        this.Hide();
    }

    void BindRewardSlots(IReadOnlyList<RewardLine> _rewards)
    {
        Array.Clear(this.m_origins, 0, this.m_origins.Length);

        if (this.rewardSlots == null) return;

        for (int t_i = 0; t_i < this.rewardSlots.Length; t_i++)
        {
            if (this.rewardSlots[t_i] == null) continue;

            if (t_i >= _rewards.Count)
            {
                this.rewardSlots[t_i].Hide();
                continue;
            }

            this.rewardSlots[t_i].Bind(_rewards[t_i].Icon, _rewards[t_i].Gain.Amount);
            this.RecordOrigin(_rewards[t_i].Gain.Type, this.rewardSlots[t_i].Icon);
        }

        // 저작 문제라 표시할 때마다 찍으면 소음이다 — 세션에 한 번이면 족하다.
        if (_rewards.Count > this.rewardSlots.Length && !s_overflowWarned)
        {
            s_overflowWarned = true;
            Debug.LogWarning($"[RewardClaimPopup] 보상 {_rewards.Count}건이 슬롯 {this.rewardSlots.Length}칸을 초과 — 앞칸만 표시한다.", this);
        }
    }

    // 같은 재화가 두 칸에 나뉘어 있어도 빛은 한 줄기다(수치도 한 번에 오른다) — 먼저 선 칸에서 피운다.
    void RecordOrigin(ECurrencyType _type, Graphic _icon)
    {
        int t_slot = (int)_type;
        if (this.m_origins[t_slot] != null) return;

        this.m_origins[t_slot] = _icon != null ? (RectTransform)_icon.transform : this.burstOrigin;
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
