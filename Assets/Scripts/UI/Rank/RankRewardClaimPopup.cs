using System;
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
    [SerializeField] TMP_Text amountText; // 보상 금액("x100")
    [SerializeField] Button claimButton;  // [획득]

    [Header("연출")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("수령 코인 분출(선택). 미배선이면 지급 즉시 닫는다.\n" +
             "팝업 root 하위에 배선하면 Hide와 함께 꺼져 CoinBurstEffect.OnDisable이 코인을 걷는다 — 반드시 팝업 밖 노드에 둘 것.")]
    [SerializeField] CoinBurstEffect claimBurst;

    [SerializeField] float goldPunch = UiPunch.DEFAULT_SCALE;

    // 확인 콜백. 지급 성공 여부를 돌려받아 연출 여부를 정한다. 중복 클릭 방지를 위해 한 번 쓰면 비운다.
    Func<bool> m_onConfirm;

    // 표시 중인 행의 보상. 지급 성공 시 이만큼 숫자를 되돌렸다가 코인 도착에 맞춰 올린다.
    CurrencyGain m_reward;

    public void Show(RankRewardInfo _info, Func<bool> _onConfirm)
    {
        this.m_onConfirm = _onConfirm;
        this.m_reward = _info.Reward;

        if (this.titleText != null) this.titleText.text = _info.DisplayName;
        if (this.amountText != null) this.amountText.text = $"x{_info.Reward.Amount:N0}";

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
        if (!t_granted || this.claimBurst == null)
        {
            this.Hide();
            return;
        }

        // BuildBurst는 재생을 호출자에게 맡긴다 — 전역 autoPlay 설정에 기대지 않고 여기서 명시적으로 돌린다.
        var t_rollUp = this.BeginRollUp(out var t_hud);
        var t_burst = this.claimBurst.BuildBurst(t_rollUp);
        t_burst.OnComplete(this.Hide);

        // 연출이 어떤 이유로 끊겨도 수치 고정은 반드시 풀린다(로비 획득 연출과 같은 안전망).
        if (t_rollUp != null) t_burst.OnKill(() => { if (t_hud != null) t_hud.ReleaseDisplay(); });

        t_burst.Play();
    }

    // 지급은 이미 끝났고 잔액도 최종값이다 — 로비 획득 연출과 같은 규칙으로 숫자를 코인 도착에 맞춰 올린다.
    // HUD를 못 찾거나 지급액이 0이면 롤업 없이 코인만 돈다(수령 자체를 막지 않는다).
    Action<int, int> BeginRollUp(out CurrencyHud _hud)
    {
        _hud = null;
        if (!this.m_reward.HasAmount) return null;

        if (!CurrencyHud.TryGet(this.m_reward.Type, out _hud)) return null;

        return _hud.BeginGainRollUp(this.m_reward.Amount, this.goldPunch);
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
