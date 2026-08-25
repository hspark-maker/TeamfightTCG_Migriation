using DG.Tweening;
using UnityEngine;
using TMPro;

// 잠김 판이 걷힌 **뒤** 그 아래 내용이 들어오는 연출(키워드 섹션·시너지 섹션에 각각 한 장).
//
// 왜 필요한가: 판이 걷히면 이미 완성된 글자가 그 자리에 있다. 그러면 "가려져 있던 것이 드러났다"가 아니라
// "판이 사라졌다"로만 읽혀서, 정작 읽어야 할 설명에는 눈이 안 간다.
// 칩 줄 → 설명 순으로 한 박씩 넣으면 시선이 그 순서를 따라간다.
//
// 경계: "언제 도는가"는 전부 호출부(CardDetailOverlayView)가 판정한다. 여기는 부르면 한 번 도는 연출만 소유한다
// (SectionUnlockFx와 같은 규약). 자리가 아니라 알파·배율만 건드리는 이유는 이 노드들이 레이아웃 그룹에
// 매달려 있어서다 — anchoredPosition을 밀면 리빌드가 매 프레임 되돌린다.
public class SectionRevealFx : MonoBehaviour
{
    [Tooltip("칩이 깔린 줄. 미배선이면 이 축만 빠진다.")]
    [SerializeField] Transform chipRoot;

    [Tooltip("설명 글자. 미배선이면 이 축만 빠진다.")]
    [SerializeField] TMP_Text descText;

    [Header("박자")]
    [Tooltip("판이 걷히고 칩 줄이 들어오기까지의 뜸.")]
    [SerializeField] float chipDelay = 0.06f;
    [SerializeField] float chipDuration = 0.22f;
    [Tooltip("칩이 이 배율에서 출발해 제 크기로 앉는다. 1이면 페이드만 남는다.")]
    [SerializeField] float chipFromScale = 0.9f;

    [Tooltip("칩 줄이 앉고 설명이 들어오기까지의 뜸. 0이면 둘이 한 덩어리로 떠서 읽는 순서가 사라진다.")]
    [SerializeField] float descDelay = 0.1f;
    [SerializeField] float descDuration = 0.28f;

    Sequence m_seq;

    // 알파 손잡이. 프리팹에 없으면 붙여 준다 — 배선 여부와 무관하게 안무가 성립해야 한다.
    CanvasGroup m_chipGroup;
    CanvasGroup m_descGroup;

    /// <summary>한 번 돈다. 미배선 축은 조용히 빠지고, 둘 다 없으면 null을 돌려준다
    /// (부른 쪽은 "기다릴 것이 없다"로 읽는다).</summary>
    public Tween Play()
    {
        if (!gameObject.activeInHierarchy) return null;

        KillRunning();
        RestoreAuthored();   // 앞 연출이 잘린 자리에서 출발하지 않게

        CanvasGroup t_chip = ChipGroup;
        CanvasGroup t_desc = DescGroup;
        if (t_chip == null && t_desc == null) return null;

        float t_chipDur = Mathf.Max(0.01f, this.chipDuration);
        float t_descDur = Mathf.Max(0.01f, this.descDuration);

        var t_seq = DOTween.Sequence().SetLink(gameObject);

        if (t_chip != null)
        {
            float t_at = Mathf.Max(0f, this.chipDelay);

            this.chipRoot.localScale = Vector3.one * Mathf.Max(0.01f, this.chipFromScale);
            t_chip.alpha = 0f;

            t_seq.Insert(t_at, t_chip.DOFade(1f, t_chipDur));
            t_seq.Insert(t_at, this.chipRoot.DOScale(1f, t_chipDur).SetEase(Ease.OutBack));
        }

        if (t_desc != null)
        {
            // 설명은 칩이 다 앉은 뒤에 온다 — 겹치면 두 사건이 하나로 뭉쳐 읽는 순서가 사라진다.
            float t_at = Mathf.Max(0f, this.chipDelay) + (t_chip != null ? t_chipDur : 0f)
                       + Mathf.Max(0f, this.descDelay);

            t_desc.alpha = 0f;
            t_seq.Insert(t_at, t_desc.DOFade(1f, t_descDur));
        }

        // 정상 종료든 잘림이든 한 곳에서 끝낸다 — 값은 저작 상태로 돌아간다(멱등).
        t_seq.OnKill(() =>
        {
            this.m_seq = null;
            if (this == null) return;

            RestoreAuthored();
        });

        this.m_seq = t_seq;
        return t_seq;
    }

    /// <summary>남은 구간을 최종 상태로 끌어당긴다(탭 스킵). 돌고 있지 않으면 false
    /// (<see cref="SectionUnlockFx.RequestSkip"/>과 같은 규약).</summary>
    public bool RequestSkip()
    {
        Sequence t_seq = this.m_seq;
        if (t_seq == null || !t_seq.IsActive()) return false;

        t_seq.Complete(true);
        return true;
    }

    void OnDisable()
    {
        // 연출 도중 꺼지면 마무리 콜백이 오지 않는다 — 여기서 끊어 반투명하게 굳는 것을 막는다.
        KillRunning();
        RestoreAuthored();
    }

    CanvasGroup ChipGroup
    {
        get
        {
            if (this.chipRoot == null) return null;
            if (this.m_chipGroup == null) this.m_chipGroup = GroupOf(this.chipRoot.gameObject);
            return this.m_chipGroup;
        }
    }

    CanvasGroup DescGroup
    {
        get
        {
            if (this.descText == null) return null;
            if (this.m_descGroup == null) this.m_descGroup = GroupOf(this.descText.gameObject);
            return this.m_descGroup;
        }
    }

    void RestoreAuthored()
    {
        if (this.chipRoot != null)
        {
            this.chipRoot.DOKill();
            this.chipRoot.localScale = Vector3.one;

            CanvasGroup t_chip = ChipGroup;
            if (t_chip != null) { t_chip.DOKill(); t_chip.alpha = 1f; }
        }

        CanvasGroup t_desc = DescGroup;
        if (t_desc != null) { t_desc.DOKill(); t_desc.alpha = 1f; }
    }

    void KillRunning()
    {
        Sequence t_seq = this.m_seq;
        this.m_seq = null;
        if (t_seq != null && t_seq.IsActive()) t_seq.Kill();
    }

    static CanvasGroup GroupOf(GameObject _go)
    {
        var t_group = _go.GetComponent<CanvasGroup>();
        return t_group != null ? t_group : _go.AddComponent<CanvasGroup>();
    }
}
