using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 챕터 하나의 마무리 띠(챕터 타일 윗변, 이음매 구름 자리에 앉는다).
// 챕터 제목 · 진행 눈금 · 완주 보상 수령이 한 자리에 모인다 — 완주 보상을 여는 계기는 여기 하나다.
//
// 정점 뷰와 같은 규약 — 인덱스만 들고 표시값은 매번 TournamentProgress에서 다시 받는다(스냅샷은 클리어 후 stale).
public class TournamentChapterBandView : MonoBehaviour
{
    [SerializeField] TMP_Text titleText;      // 챕터 표시명(미저작이면 프리팹 저작값 유지)
    [SerializeField] TMP_Text progressText;   // "3 / 6"
    [SerializeField] Button claimButton;      // 완주 보상 [받기]

    [Tooltip("완주 보상 미리보기 칸(아이콘 + 수량). 저작한 보상이 칸 수보다 적으면 남는 칸은 꺼진다.")]
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;

    [Header("상태 레이어(선택 — 미배선 시 null 가드)")]
    [Tooltip("상태마다 켜지는 '묶음'이다 — 표식 한 장이 아니라 그 상태에서만 보여야 할 것을 통째로 담는다.\n" +
             "진행 중: 옅은 띠 + 눈금 / 완주 미수령: [받기] 버튼 + 보상칸 + 반짝임 / 완주 수령: 금색 띠 + 체크.")]
    [SerializeField] GameObject progressMark;    // 아직 완주하지 못한 챕터
    [SerializeField] GameObject claimableMark;   // 완주 + 미수령(받을 것이 남아 있다)
    [SerializeField] GameObject clearedMark;     // 완주(수령까지 끝난 상태)

    [Tooltip("맵의 끝 표지. 마지막 챕터를 완주했을 때만 켜진다 — 저작된 여정이 여기서 끝난다는 안내다.\n" +
             "미배선이면 끝 상태가 화면에 드러나지 않을 뿐 진행에는 영향이 없다.")]
    [SerializeField] GameObject endMark;

    [Tooltip("완주하지 않은 챕터 띠의 알파. 배경 그림을 가리지 않을 만큼만 죽인다.")]
    [SerializeField] float lockedAlpha = 0.88f;

    [SerializeField] CanvasGroup canvasGroup;

    // 보상 조회용 공용 버퍼 — 칸이 값을 즉시 복사하므로 띠마다 리스트를 들 이유가 없다.
    static readonly List<RewardLine> s_rewardBuffer = new List<RewardLine>();

    static bool s_overflowWarned;

    // 표시 대상 챕터. -1 = 미바인딩(Refresh 무시).
    int m_index = -1;

    // 마지막 챕터인가 — 끝 표지를 켤 자격이다(맵이 알려 준다. 띠가 챕터 수를 다시 세지 않게).
    bool m_isLast;

    /// <summary>챕터 인덱스 배선 + 리스너 1회 등록(재빌드마다 중복 방지).</summary>
    public void Bind(int _chapterIndex, bool _isLast)
    {
        this.m_index = _chapterIndex;
        this.m_isLast = _isLast;

        if (this.claimButton != null)
        {
            this.claimButton.onClick.RemoveAllListeners();
            this.claimButton.onClick.AddListener(this.OnClaimTapped);
        }

        this.Refresh();
    }

    /// <summary>상태 표시 갱신. 진행 통지(OnChanged)마다 맵이 전 띠에 호출한다.</summary>
    public void Refresh()
    {
        if (this.m_index < 0) return;

        // 저작에서 사라진 챕터면 띠를 내린다 — 남겨 두면 프리팹 목업 제목이 실제 챕터인 양 서 있게 된다.
        if (!TournamentProgress.TryGetChapter(this.m_index, out TournamentChapterDef t_chapter)
            || !TournamentProgress.TryGetChapterProgress(this.m_index, out int t_cleared, out int t_total))
        {
            this.gameObject.SetActive(false);
            return;
        }

        // 제목 미저작이면 프리팹에 저작된 값을 그대로 둔다(빈 값으로 덮으면 목업이 사라진다).
        if (this.titleText != null && !string.IsNullOrEmpty(t_chapter.title)) this.titleText.text = t_chapter.title;

        if (this.progressText != null) this.progressText.text = $"{t_cleared} / {t_total}";

        this.BindRewardSlots();

        // 완주 판정을 눈금에서 파생시킨다 — IsChapterComplete를 따로 부르면 같은 정점을 한 번 더 훑는다.
        // 정점 0개 챕터가 완주로 통과하는 것도 그대로다(0 == 0).
        bool t_complete = t_cleared == t_total;

        // 수령 자격은 흐름이 단독으로 판정한다 — 여기서 조건을 더 곱하면 띠와 팝업이 서로 다른 자격을 보게 된다.
        bool t_claimable = TournamentChapterRewardFlow.CanClaim(this.m_index);

        if (this.progressMark != null) this.progressMark.SetActive(!t_complete);
        if (this.claimableMark != null) this.claimableMark.SetActive(t_claimable);
        if (this.clearedMark != null) this.clearedMark.SetActive(t_complete && !t_claimable);
        if (this.endMark != null) this.endMark.SetActive(this.m_isLast && t_complete);
        if (this.claimButton != null) this.claimButton.interactable = t_claimable;
        if (this.canvasGroup != null) this.canvasGroup.alpha = t_complete ? 1f : this.lockedAlpha;
    }

    // 수령은 흐름이 소유한다 — 자격 판정 · 팝업 · 지급이 한 자리에 있어야 띠와 판정이 갈리지 않는다.
    // 지급이 끝나면 OnChanged가 돌아 맵이 이 띠를 다시 그린다(여기서 직접 Refresh하지 않는 이유).
    void OnClaimTapped()
    {
        if (this.m_index < 0) return;

        TournamentChapterRewardFlow.Open(this.m_index);
    }

    void BindRewardSlots()
    {
        TournamentProgress.FillChapterRewards(this.m_index, s_rewardBuffer);

        if (this.rewardSlots == null || this.rewardSlots.Length == 0) return;

        for (int t_i = 0; t_i < this.rewardSlots.Length; t_i++)
        {
            if (this.rewardSlots[t_i] == null) continue;

            if (t_i < s_rewardBuffer.Count)
                this.rewardSlots[t_i].Bind(s_rewardBuffer[t_i].Icon, s_rewardBuffer[t_i].Gain.Amount);
            else
                this.rewardSlots[t_i].Hide();
        }

        // 저작 문제라 띠마다 찍으면 소음이다 — 세션에 한 번이면 족하다.
        if (s_rewardBuffer.Count > this.rewardSlots.Length && !s_overflowWarned)
        {
            s_overflowWarned = true;
            Debug.LogWarning($"[TournamentChapterBandView] 완주 보상 {s_rewardBuffer.Count}건이 슬롯 {this.rewardSlots.Length}칸을 초과 — 앞칸만 표시한다.", this);
        }
    }
}
