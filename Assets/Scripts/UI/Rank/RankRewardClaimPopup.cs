using System;
using System.Collections.Generic;
using DG.Tweening;   // 분출 시퀀스의 OnComplete·Play 확장 메서드
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 랭크 보상 수령 팝업(ClaimPopup 노드에 부착). 표시와 확인 콜백만 담당하고 지급은 패널이 매니저에 위임한다.
// 씬에 직접 저작되므로 PooledUIBase가 아니라 SetActive 토글로 동작한다.
public class RankRewardClaimPopup : MonoBehaviour
{
    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] TMP_Text titleText;  // 티어 표시명(선택)
    [SerializeField] Button claimButton;  // [획득]

    [Tooltip("보상 칸(아이콘 + 수량). 저작한 보상이 칸 수보다 적으면 남는 칸은 꺼진다.")]
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("코인이 출발할 자리(선택). 비우면 각 재화 수치 자리에서 튀어 제자리로 돌아온다.")]
    [SerializeField] RectTransform burstOrigin;

    static bool s_overflowWarned;

    // 확인 콜백. 지급 성공 여부를 돌려받아 연출 여부를 정한다. 중복 클릭 방지를 위해 한 번 쓰면 비운다.
    Func<bool> m_onConfirm;

    // 표시 중인 행의 보상. 재화가 갈리면 각자의 HUD로 각자의 코인이 날아가므로 종류별로 담아 둔다.
    readonly CurrencyGainBucket m_rewards = new CurrencyGainBucket();

    // 이 팝업이 세운 분출. 딤을 눌러 닫고 다음 행을 바로 수령할 수 있어 두 연출이 겹친다.
    Sequence m_burst;

    public void Show(RankRewardInfo _info, Func<bool> _onConfirm)
    {
        this.m_onConfirm = _onConfirm;

        this.m_rewards.Clear();
        for (int t_i = 0; t_i < _info.Rewards.Count; t_i++) this.m_rewards.Add(_info.Rewards[t_i].Gain);

        if (this.titleText != null) this.titleText.text = _info.DisplayName;
        this.BindRewardSlots(_info.Rewards);

        if (this.claimButton != null)
        {
            this.claimButton.onClick.RemoveAllListeners(); // 재표시마다 중복 등록 방지
            this.claimButton.onClick.AddListener(this.OnClaimClicked);
            this.claimButton.interactable = true;
        }

        this.SetVisible(true);
    }

    public void Hide()
    {
        this.m_onConfirm = null;
        this.SetVisible(false);
    }

    // 팝업은 자기 자신이 토글 대상이라 OnDisable이 정상 동작한다 — 잘린 퇴장 마무리와 표시 원복을 여기서 위임한다.
    // 분출 시퀀스는 죽이지 않는다(팝업 밖 노드에서 도는 연출이라 끊으면 코인이 허공에 굳는다).
    void OnDisable()
    {
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    void OnClaimClicked()
    {
        // 콜백을 먼저 비워 연타로 두 번 지급되는 경로를 막는다(매니저 가드와 이중 방어).
        var t_callback = this.m_onConfirm;
        this.m_onConfirm = null;

        if (this.claimButton != null) this.claimButton.interactable = false;

        // 지급·영속은 이 호출에서 끝난다. 아래 분출은 확정된 결과를 보여주기만 한다.
        bool t_granted = t_callback != null && t_callback.Invoke();

        // 실패(팝업이 뜬 사이 상태가 바뀌어 가드에 걸림)면 줄 것이 없으니 연출도 없다.
        if (!t_granted || !CurrencyGainEffectPlayer.TryGet(this, out var t_player))
        {
            this.Hide();
            return;
        }

        // 직전 분출을 새 롤업 고정(BuildGain 안의 BeginGainRollUp)보다 먼저 마무리한다 — 순서가 뒤집히면
        // 옛 도착 콜백이 새 고정을 덮어 숫자가 뒤로 튄다(로비 획득 연출과 같은 이유).
        // 소유권을 먼저 떼야 옛 시퀀스의 종료 콜백이 이번에 다시 연 팝업을 닫지 않는다.
        var t_prev = this.m_burst;
        this.m_burst = null;
        if (t_prev != null && t_prev.IsActive()) t_prev.Complete(true);

        // 재화가 갈리면 분출기도 갈려야 한다 — 공용 재생기가 종류별 시퀀스를 조립하고 수치 고정 해제 안전망까지 붙여 온다.
        var t_burst = t_player.BuildGain(this.burstOrigin, this.m_rewards);
        if (t_burst == null)
        {
            this.Hide();
            return;
        }

        // 연출 레이어가 랭크 오버레이보다 아래면 코인이 딤에 가린다(로비 획득 연출과 같은 처리).
        t_player.transform.SetAsLastSibling();

        this.m_burst = t_burst;

        // 정상 완료와 강제 종료를 한 경로로 덮는다 — OnComplete만 두면 시퀀스가 끊겼을 때
        // 팝업이 [획득] 비활성 상태로 열린 채 남는다. autoKill은 전역 기본값에 기대지 않고 명시한다.
        t_burst.SetAutoKill(true).OnKill(this.OnBurstEnded);

        // BuildGain은 재생을 호출자에게 맡긴다 — 전역 autoPlay 설정에 기대지 않고 여기서 명시적으로 돌린다.
        t_burst.Play();
    }

    // 이 팝업이 소유한 분출이 끝났을 때만 닫는다(소유권을 넘긴 뒤 끝난 옛 연출은 무시).
    void OnBurstEnded()
    {
        if (this.m_burst == null) return;

        this.m_burst = null;
        this.Hide();
    }

    void BindRewardSlots(IReadOnlyList<RankReward> _rewards)
    {
        if (this.rewardSlots == null) return;

        for (int t_i = 0; t_i < this.rewardSlots.Length; t_i++)
        {
            if (this.rewardSlots[t_i] == null) continue;

            if (t_i < _rewards.Count) this.rewardSlots[t_i].Bind(_rewards[t_i].Icon, _rewards[t_i].Gain.Amount);
            else this.rewardSlots[t_i].Hide();
        }

        // 저작 문제라 표시할 때마다 찍으면 소음이다 — 세션에 한 번이면 족하다.
        if (_rewards.Count > this.rewardSlots.Length && !s_overflowWarned)
        {
            s_overflowWarned = true;
            Debug.LogWarning($"[RankRewardClaimPopup] 티어 보상 {_rewards.Count}건이 슬롯 {this.rewardSlots.Length}칸을 초과 — 앞칸만 표시한다.", this);
        }
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
