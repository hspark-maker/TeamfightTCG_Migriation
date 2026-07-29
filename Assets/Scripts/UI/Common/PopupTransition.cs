using DG.Tweening;
using UnityEngine;

// 씬에 직접 저작된 팝업의 등장·퇴장 표시 상태 한 벌(페이드 + 스케일 팝).
// MonoBehaviour가 아니라 뷰가 필드로 소유한다 — 씬 저작 뷰는 베이스 클래스로 묶기 어렵고,
// 같은 연출 규칙이 뷰마다 복제되면 진실원이 갈라진다.
//
// 토글 대상(root)은 뷰가 계속 쥔다(씬에 이미 배선된 필드라 여기로 옮기면 배선이 끊긴다) — 호출마다 넘겨받는다.
// 대상이 꺼질 때는 뷰가 HandleDisabled를 불러야 잘린 퇴장이 마무리된다.
[System.Serializable]
public class PopupTransition
{
    [Tooltip("스케일 팝 대상. 미배선이면 페이드만 한다(딤까지 커지지 않게 root가 아닌 패널을 물릴 것).")]
    [SerializeField] RectTransform panel;

    [SerializeField] float openDuration = 0.25f;
    [SerializeField] float closeDuration = 0.15f;
    [SerializeField] float openFromScale = 0.9f;
    [SerializeField] float closeToScale = 0.9f;

    // 페이드 대상. 씬에 저작돼 있지 않으면 지연 확보한다.
    CanvasGroup m_group;

    // 진행 중 등장·퇴장 시퀀스.
    Sequence m_seq;

    // 퇴장 진행 중 표식. 완료 콜백이 오기 전에 잘리면 대상이 켜진 채 남으므로 HandleDisabled가 이걸 보고 마무리한다.
    bool m_closing;

    /// <summary>_target을 연출과 함께 켜고 끈다. 퇴장은 트윈이 끝난 뒤에 비활성화된다.</summary>
    public void SetVisible(GameObject _target, bool _visible)
    {
        if (_target == null) return;

        var t_group = this.ResolveGroup(_target);

        this.Kill();
        t_group.DOKill();
        if (this.panel != null) this.panel.DOKill();

        if (_visible)
        {
            _target.SetActive(true);

            t_group.alpha = 0f;
            t_group.blocksRaycasts = true;
            if (this.panel != null) this.panel.localScale = Vector3.one * this.openFromScale;

            this.m_seq = DOTween.Sequence().SetLink(_target);
            this.m_seq.Append(t_group.DOFade(1f, this.openDuration));
            if (this.panel != null)
                this.m_seq.Join(this.panel.DOScale(1f, this.openDuration).SetEase(Ease.OutBack));

            this.m_seq.OnComplete(() => this.m_seq = null);
            this.m_seq.Play();   // 재생 책임을 코드에 남긴다(전역 autoPlay 설정에 기대지 않게).
            return;
        }

        // 이미 화면에 없으면(자신 또는 부모가 꺼짐) 트윈 없이 즉시 정리한다 — 다음 열기의 유령 프레임 차단.
        if (!_target.activeInHierarchy)
        {
            _target.SetActive(false);
            this.RestoreVisual();
            return;
        }

        t_group.blocksRaycasts = false;   // 퇴장 중 클릭이 뒤 요소로 새지 않게.

        this.m_closing = true;

        this.m_seq = DOTween.Sequence().SetLink(_target);
        this.m_seq.Append(t_group.DOFade(0f, this.closeDuration));
        if (this.panel != null)
            this.m_seq.Join(this.panel.DOScale(this.closeToScale, this.closeDuration).SetEase(Ease.InBack));

        this.m_seq.OnComplete(() =>
        {
            this.m_seq = null;
            this.m_closing = false;   // 아래 비활성화가 부를 HandleDisabled가 "잘림"으로 오판하지 않게 먼저 내린다.
            _target.SetActive(false);
            this.RestoreVisual();
        });
        this.m_seq.Play();
    }

    /// <summary>
    /// 대상이 꺼졌을 때(뷰의 OnDisable) 호출. 퇴장이 완료 전에 잘렸으면 대상을 마저 비활성화한다 —
    /// 부모가 먼저 꺼져 트윈이 죽으면 activeSelf가 켜진 채 남아 다음 열기에서 유령 프레임이 뜬다.
    /// </summary>
    public void HandleDisabled(GameObject _target)
    {
        bool t_cutOff = this.m_closing;

        this.Kill();

        if (t_cutOff && _target != null && _target.activeSelf) _target.SetActive(false);

        this.RestoreVisual();
    }

    void Kill()
    {
        this.m_seq?.Kill();
        this.m_seq = null;
        this.m_closing = false;
    }

    // 씬에 CanvasGroup이 저작돼 있지 않아도 페이드가 성립하도록 지연 확보한다.
    CanvasGroup ResolveGroup(GameObject _target)
    {
        if (this.m_group != null) return this.m_group;

        this.m_group = _target.GetComponent<CanvasGroup>();
        if (this.m_group == null) this.m_group = _target.AddComponent<CanvasGroup>();

        return this.m_group;
    }

    // 다음 열기가 중간값(반투명·축소)에서 시작하지 않게 원복. 확보 전이면 건드릴 것도 없다.
    void RestoreVisual()
    {
        if (this.m_group != null)
        {
            this.m_group.alpha = 1f;
            this.m_group.blocksRaycasts = true;
        }

        if (this.panel != null) this.panel.localScale = Vector3.one;
    }
}
