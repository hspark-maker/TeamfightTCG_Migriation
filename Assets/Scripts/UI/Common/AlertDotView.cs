using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

// 버튼에 얹는 "여기 볼 게 있다" 알림 점의 연출 베이스.
// 판정은 갖지 않는다 — 파생이 도메인 단일 진실원 하나를 가리키고, 여기는 그 결과를 그리기만 한다.
// 등장에 힘을 몰고 상시는 은은하게 둔다 — 늘 크게 움직이면 눈이 금방 무시한다.
public abstract class AlertDotView : MonoBehaviour
{
    [Tooltip("켜고 끌 점 노드. 이 컴포넌트가 붙은 노드의 자식이어야 한다(자기 자신을 물리면 꺼진 뒤 구독이 끊긴다).")]
    [SerializeField] GameObject dot;

    [Header("등장")]
    [SerializeField] float appearDuration = 0.3f;
    [Tooltip("점이 생기는 순간 버튼째 튀는 세기. 0이면 버튼은 가만히 있는다.")]
    [SerializeField] float buttonPunch = 0.16f;

    [Header("상시 맥동")]
    [Tooltip("점이 부풀었다 돌아오는 배율. 1이면 맥동 없이 떠 있기만 한다.")]
    [SerializeField] float pulseScale = 1.12f;
    [SerializeField] float pulseDuration = 0.75f;

    [Header("퇴장")]
    [SerializeField] float disappearDuration = 0.16f;

    // 최초 렌더를 Start로 미루기 위한 표식 — 마스터 데이터 주입(초기화 스텝)보다 OnEnable이 먼저 돌 수 있다.
    bool m_started;

    // 지금 화면에 떠 있는가. 상태가 바뀔 때만 연출하고 같은 값이 다시 오면 무시한다(변경 통지가 잦다).
    bool m_shown;

    // 등장·퇴장 시퀀스. 맥동과 같은 스케일을 잡으므로 둘이 동시에 돌지 않게 한다.
    Sequence m_seq;

    Tween m_pulse;

    /// <summary>지금 점을 띄워야 하는가. 파생이 도메인에 물어 답한다.</summary>
    protected abstract bool ShouldShow { get; }

    /// <summary>표시 시점 재조회만으로는 부족하다(수령·완주 즉시 꺼져야 한다) — 도메인 변경 통지에 물린다.</summary>
    protected abstract void Subscribe(Action _handler);

    protected abstract void Unsubscribe(Action _handler);

    /// <summary>점 노드를 런타임에 물린다(프리팹에 컴포넌트를 못 붙여 AddComponent로 얹는 대상용).</summary>
    protected void BindDot(GameObject _dot)
    {
        this.dot = _dot;

        if (this.m_started && this.isActiveAndEnabled) this.Render(true, _force: true);
    }

    IEnumerator Start()
    {
        this.m_started = true;

        // 로비 진입은 로딩 커버가 덮은 채로 Start가 돈다 — 그 아래서 팝이 끝나면 아무도 못 본다.
        yield return new WaitWhile(() => LoadingCoverView.IsCovering);

        this.Render();
    }

    void OnEnable()
    {
        this.Subscribe(this.Render);

        if (!this.m_started) return;   // 첫 활성화는 Start가 담당(탭 재진입만 여기서).

        // 탭 재진입은 이미 알던 소식이다 — 팝 없이 결과 상태로 바로 세운다.
        // 꺼져 있는 동안 온 변화는 구독이 끊겨 못 받았다 → m_shown이 그대로여도 반드시 다시 그린다.
        this.Render(true, _force: true);
    }

    void OnDisable()
    {
        this.Unsubscribe(this.Render);

        // 꺼지는 동안 트윈만 남으면 다음 활성화가 중간 스케일을 물려받는다.
        this.KillTweens();
        this.RestoreScale();
    }

    void Render() => this.Render(false);

    // _force = m_shown을 믿지 않고 지금 답을 그대로 적용한다(구독이 끊겼던 구간을 건너뛴 재활성화용).
    void Render(bool _instant, bool _force = false)
    {
        if (this.dot == null) return;

        bool t_on = this.ShouldShow;
        if (!_force && t_on == this.m_shown) return;   // 같은 상태로 다시 오면 연출을 처음부터 다시 틀지 않는다.

        this.m_shown = t_on;

        if (t_on) this.Show(_instant);
        else this.Hide(_instant);
    }

    void Show(bool _instant)
    {
        this.KillTweens();
        this.dot.SetActive(true);

        var t_rect = this.DotRect;
        if (t_rect == null) return;

        if (_instant)
        {
            t_rect.localScale = Vector3.one;
            this.StartPulse();
            return;
        }

        // 없던 것이 생기는 순간이라 0에서 튀어나온다 — OutBack이 살짝 넘겼다 돌아오며 무게를 만든다.
        t_rect.localScale = Vector3.zero;

        this.m_seq = DOTween.Sequence().SetLink(this.gameObject);
        this.m_seq.Append(t_rect.DOScale(1f, this.appearDuration).SetEase(Ease.OutBack));
        this.m_seq.OnComplete(() => { this.m_seq = null; this.StartPulse(); });

        // 점만 튀면 구석에서 혼자 노는 것처럼 보인다 — 버튼째 한 번 반응해야 "이 버튼에 생겼다"가 된다.
        if (this.buttonPunch > 0f) UiPunch.Play(this.transform, this.buttonPunch);
    }

    void Hide(bool _instant)
    {
        this.KillTweens();

        var t_rect = this.DotRect;
        if (t_rect == null || _instant)
        {
            this.RestoreScale();
            this.dot.SetActive(false);
            return;
        }

        this.m_seq = DOTween.Sequence().SetLink(this.gameObject);
        this.m_seq.Append(t_rect.DOScale(0f, this.disappearDuration).SetEase(Ease.InBack));
        this.m_seq.OnComplete(() =>
        {
            this.m_seq = null;
            this.dot.SetActive(false);
            this.RestoreScale();   // 다음 등장이 1에서 시작하지 않도록 크기를 되돌려 둔다.
        });
    }

    // 등장이 끝난 뒤에만 건다 — 팝과 같은 스케일을 쓰므로 겹치면 서로를 밟는다.
    void StartPulse()
    {
        if (this.pulseScale <= 1f) return;

        var t_rect = this.DotRect;
        if (t_rect == null || !this.isActiveAndEnabled) return;

        this.m_pulse = t_rect.DOScale(this.pulseScale, this.pulseDuration)
                             .SetLoops(-1, LoopType.Yoyo)
                             .SetEase(Ease.InOutSine)
                             .SetLink(this.gameObject);
    }

    RectTransform DotRect => this.dot != null ? this.dot.transform as RectTransform : null;

    void KillTweens()
    {
        if (this.m_seq != null) { this.m_seq.Kill(); this.m_seq = null; }
        if (this.m_pulse != null) { this.m_pulse.Kill(); this.m_pulse = null; }
    }

    void RestoreScale()
    {
        var t_rect = this.DotRect;
        if (t_rect != null) t_rect.localScale = Vector3.one;
    }
}
