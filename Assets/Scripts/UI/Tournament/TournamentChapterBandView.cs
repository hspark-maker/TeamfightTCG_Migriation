using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
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

    [Tooltip("랭크 미달로 챕터 전체가 잠겼음을 알리는 묶음. 다른 어떤 상태 묶음보다 먼저다 —\n" +
             "들어갈 수 없는 장에서 진행 눈금이나 보상 미리보기를 펴 봐야 읽을 사람이 없다.\n" +
             "미배선이면 잠김이 화면에 드러나지 않을 뿐 진입은 그대로 막힌다.")]
    [SerializeField] GameObject rankLockMark;

    [Tooltip("잠김 안내 문구. 요구 등급 표시명을 코드가 채운다.")]
    [SerializeField] TMP_Text rankLockText;

    [Tooltip("요구 등급 배지(선택). 등급에 배지가 저작되지 않았으면 그림 없이 문구만 남는다.")]
    [SerializeField] Image rankLockBadge;

    [Tooltip("완주하지 않은 챕터 띠의 알파. 배경 그림을 가리지 않을 만큼만 죽인다.")]
    [SerializeField] float lockedAlpha = 0.88f;

    [SerializeField] CanvasGroup canvasGroup;

    [Header("해금 연출 — 맵 진입 1회")]
    [Tooltip("잠긴 모습을 보여 주는 한 박. 무엇이 풀리는지 먼저 보여야 풀림이 사건으로 읽힌다.")]
    [SerializeField] float introHold = 0.4f;

    [Tooltip("자물쇠가 터지고 판이 걷히는 박 — lockFx가 미배선일 때만 쓰는 폴백이다.\n" +
             "배선돼 있으면 그 안무의 실제 트윈 길이가 이 박이 되고 이 값은 무시된다(빛이 모이는 도중에 박이 끊기지 않게).")]
    [SerializeField] float introShed = 0.3f;

    [Tooltip("탈채도가 풀리며 제목·눈금이 드는 박. 이 박이 끝난 화면은 연출이 없을 때와 같다.")]
    [SerializeField] float introSettle = 0.4f;

    [Tooltip("풀리는 순간 띠가 튀는 세기.")]
    [SerializeField] float introPunch = 0.2f;

    [Tooltip("띠가 튀는 시간. introSettle 안에서 끝나야 총 길이와 어긋나지 않는다.")]
    [SerializeField] float introPunchTime = 0.35f;

    [Tooltip("자물쇠가 부서지는 연출(rankLockMark에 붙인다). 미배선이면 그 박에 자물쇠를 즉시 걷고 나머지 박은 그대로 돈다.")]
    [SerializeField] SectionUnlockFx lockFx;

    // 보상 조회용 공용 버퍼 — 칸이 값을 즉시 복사하므로 띠마다 리스트를 들 이유가 없다.
    static readonly List<RewardLine> s_rewardBuffer = new List<RewardLine>();

    static bool s_overflowWarned;

    // 표시 대상 챕터. -1 = 미바인딩(Refresh 무시).
    int m_index = -1;

    // 마지막 챕터인가 — 끝 표지를 켤 자격이다(맵이 알려 준다. 띠가 챕터 수를 다시 세지 않게).
    bool m_isLast;

    // 도는 해금 안무. null = 지금 화면이 곧 진실이다.
    Sequence m_introSeq;

    // 미리 세워 재워 둔 자물쇠 안무. 길이를 먼저 받으려고 앞당겨 세운 것이라 터지는 박에서 깨운다.
    // null = 미배선이거나 세우지 못했다(그 박은 저작 폴백으로 돈다).
    Tween m_lockTween;

    // 참인 동안 Refresh를 억제한다 — 잠긴 모습을 보여 주는 박에 진행 통지가 끼어들면 결말이 먼저 새어 나간다.
    bool m_introHold;

    // 해금 안무의 탈채도를 되돌릴 자리. null = 지금 색이 살아 있다.
    List<UiGrayscale.Toned> m_introToned;

    // 연출이 [받기] 버튼을 클릭 경로에서 내려 둔 상태. true = 되돌릴 것이 남아 있다.
    bool m_claimMuted;

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
        // 해금 안무가 잠긴 모습을 쥐고 있는 동안은 진실이 화면을 덮지 않게 한다(정점의 ApplyStampRest와 같은 관용구).
        if (this.m_introHold) return;
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

        // 랭크 잠금은 진행과 축이 다르다 — 완주한 장이 저작 변경·강등으로 다시 잠겨도 그 성취까지 가리지는 않는다.
        bool t_rankLocked = TournamentProgress.IsChapterRankLocked(this.m_index) && t_cleared < t_total;
        this.ApplyRankLock(t_rankLocked, this.m_index);

        // 완주 판정을 눈금에서 파생시킨다 — IsChapterComplete를 따로 부르면 같은 정점을 한 번 더 훑는다.
        // 정점 0개 챕터가 완주로 통과하는 것도 그대로다(0 == 0).
        bool t_complete = t_cleared == t_total;

        // 수령 자격은 진행도가 단독으로 판정한다 — 여기서 조건을 더 곱하면 띠와 팝업이 서로 다른 자격을 보게 된다.
        bool t_claimable = TournamentProgress.CanClaimChapterReward(this.m_index);

        if (this.progressMark != null) this.progressMark.SetActive(!t_complete && !t_rankLocked);
        if (this.claimableMark != null) this.claimableMark.SetActive(t_claimable && !t_rankLocked);
        if (this.clearedMark != null) this.clearedMark.SetActive(t_complete && !t_claimable);
        if (this.endMark != null) this.endMark.SetActive(this.m_isLast && t_complete);
        if (this.claimButton != null) this.claimButton.interactable = t_claimable && !t_rankLocked;
        if (this.canvasGroup != null) this.canvasGroup.alpha = t_complete ? 1f : this.lockedAlpha;
    }

    /// <summary>해금 무대에 세운다 — 잠긴 모습으로 굳히고 진실 갱신을 막는다. 재생 없이 세우기만 한다.</summary>
    public void StageChapterLocked()
    {
        // 재생과 같은 잣대다 — 꺼진 띠에서는 트윈이 링크에 잘려 자물쇠 안무가 서지 않는다.
        if (this.m_index < 0 || !this.gameObject.activeInHierarchy) return;

        // 두 번 세우면 lockFx.Play()가 다시 나가 재워 둔 트윈을 잃는다 — 이미 선 무대는 그대로 둔다.
        if (this.m_introHold) return;

        this.m_introHold = true;
        this.ApplyIntroLocked();
        this.StageLockFx();
    }

    /// <summary>챕터 해금(맵 진입 1회). 잠긴 모습을 한 박 보여준 뒤 풀린다. 총 길이를 돌려준다.</summary>
    public float PlayChapterUnlock()
    {
        // 꺼진 띠에서는 트윈이 링크에 잘려 안무가 서지 않는다 — 맵이 기다릴 것도 없다.
        // 가드가 정리보다 먼저다. 돌던 안무를 두고 띠가 꺼진 경로는 OnDisable이 이미 걷었다.
        if (this.m_index < 0 || !this.gameObject.activeInHierarchy) return 0f;

        // 미리 세워 둔 무대는 그대로 이어받는다 — 다시 세우면 재워 둔 자물쇠 안무를 잃고,
        // 맵이 화면을 연 첫 프레임부터 잠겨 있던 모습이 한 박 되감기는 것처럼 보인다.
        // 돌던 안무가 있으면 그 무대는 이미 절반이 지나간 것이라 세우기부터 다시 한다.
        bool t_staged = this.m_introHold && this.m_introSeq == null;

        if (!t_staged)
        {
            this.AbortUnlock();
            this.StageChapterLocked();
        }

        float t_hold   = Mathf.Max(0f, this.introHold);
        float t_settle = Mathf.Max(0f, this.introSettle);

        // 걷히는 박은 자물쇠 안무의 실제 길이다 — 짧게 잡으면 빛이 모이는 도중에 표식이 꺼져
        // 이 연출의 핵심인 "터지는" 박을 한 번도 못 본다. 맵도 이 총 길이로 다음 대상의 시작 시각을 잡는다.
        float t_shed = this.ShedDuration();

        Sequence t_seq = DOTween.Sequence().SetLink(this.gameObject);

        t_seq.AppendInterval(t_hold);
        t_seq.AppendCallback(this.BreakIntroLock);
        t_seq.AppendInterval(t_shed);
        t_seq.AppendCallback(this.SettleIntro);
        t_seq.AppendInterval(t_settle);

        // Kill은 콜백을 건너뛴다 — 손잡이만 여기서 놓고, 진실로 되돌리는 일은 AbortUnlock이 맡는다.
        t_seq.OnKill(() => this.m_introSeq = null);

        this.m_introSeq = t_seq;
        return t_hold + t_shed + t_settle;
    }

    /// <summary>도는 연출을 진실로 스냅시킨다(이탈·스킵). 돌고 있지 않으면 아무 일도 하지 않는다.</summary>
    public void AbortUnlock()
    {
        Sequence t_seq = this.m_introSeq;
        this.m_introSeq = null;
        if (t_seq != null && t_seq.IsActive()) t_seq.Kill();

        // 자물쇠가 반쯤 부푼 자리에서 굳지 않게 그쪽 안무도 끝으로 당긴다(아직 재워 둔 것도 여기서 끝난다).
        this.m_lockTween = null;
        if (this.lockFx != null) this.lockFx.RequestSkip();

        UiGrayscale.Restore(this.m_introToned);
        this.m_introToned = null;

        this.UnmuteClaimButton();

        if (!this.m_introHold) return;

        this.m_introHold = false;
        this.Refresh();
    }

    /// <summary>도는 해금 안무를 결말까지 당긴다. 당길 것이 있었으면 true.</summary>
    public bool RequestSkipUnlock()
    {
        Sequence t_seq = this.m_introSeq;
        if (t_seq == null || !t_seq.IsActive()) return false;

        // 중첩까지 완료시켜야 BreakIntroLock·SettleIntro가 실제로 돈다 — Kill과 갈리는 자리가 여기다.
        t_seq.Complete(true);
        this.m_introSeq = null;

        // 자물쇠 안무는 시퀀스 밖 트윈이라 Complete가 닿지 않는다. 순서를 뒤집어 먼저 당기면
        // 아직 자고 있는 트윈이 끝나 버리고, 뒤이은 BreakIntroLock이 깨울 대상을 잃어 자물쇠가 화면에 남는다.
        this.m_lockTween = null;
        if (this.lockFx != null) this.lockFx.RequestSkip();

        // SettleIntro가 [받기]를 되살리지만 당기기는 사슬 한복판이다 — 남은 대상이 도는 동안
        // 그 버튼이 눌리면 보상 팝업이 사슬 위에 겹친다. 사슬이 끝나면 AbortUnlock이 되돌린다.
        this.MuteClaimButton();

        return true;
    }

    // 잠김 안내. 요구 등급의 표시명·배지는 랭크가 소유하므로 여기서 문자열을 짓지 않는다.
    void ApplyRankLock(bool _locked, int _chapterIndex)
    {
        if (this.rankLockMark != null) this.rankLockMark.SetActive(_locked);

        if (!_locked)
        {
            this.SetRankLockBadge(null);
            return;
        }

        // 해금 첫 박이 문구를 접어 두는 경로가 있어(ApplyIntroLocked) 잠김을 그릴 때마다 다시 편다.
        this.ShowRankLockCopy(true);

        // 등급을 읽지 못하면 배지도 함께 내린다 — 여기서 그냥 빠져나가면 이전 등급의 그림이 켜진 채 선다.
        if (!TournamentProgress.TryGetRequiredGrade(_chapterIndex, out ERankGrade t_grade)
            || !RankManager.TryGetGradeDisplay(t_grade, out string t_name, out Sprite t_badge))
        {
            this.SetRankLockBadge(null);
            return;
        }

        if (this.rankLockText != null) this.rankLockText.text = $"{t_name} 도달 시 해금";

        this.SetRankLockBadge(t_badge);
    }

    // 요구 등급 배지 활성의 유일한 주인. 그림이 없으면 칸째로 내린다.
    void SetRankLockBadge(Sprite _badge)
    {
        if (this.rankLockBadge == null) return;

        this.rankLockBadge.gameObject.SetActive(_badge != null);
        if (_badge != null) this.rankLockBadge.sprite = _badge;
    }

    // 잠김 안내 문구만 여닫는다 — 자물쇠 표식은 세운 채 문구만 접어야 하는 자리가 있어 축을 나눈다.
    // 배지는 여기서 만지지 않는다(활성 주인이 둘이면 술어가 갈려 이전 등급 그림이 남는다).
    void ShowRankLockCopy(bool _shown)
    {
        if (this.rankLockText != null) this.rankLockText.gameObject.SetActive(_shown);
    }

    // 해금 안무의 첫 박. 진행·수령·완주·끝 표지를 모두 접고 잠김 하나만 세운다 —
    // 들어갈 수 없던 장이라 그 순간의 화면에 진행 눈금이나 보상 미리보기가 서 있으면 안 된다.
    void ApplyIntroLocked()
    {
        if (this.rankLockMark != null) this.rankLockMark.SetActive(true);
        if (this.progressMark != null) this.progressMark.SetActive(false);
        if (this.claimableMark != null) this.claimableMark.SetActive(false);
        if (this.clearedMark != null) this.clearedMark.SetActive(false);
        if (this.endMark != null) this.endMark.SetActive(false);
        this.MuteClaimButton();
        if (this.canvasGroup != null) this.canvasGroup.alpha = this.lockedAlpha;

        // 랭크로 잠긴 적 없는 장에까지 "○○ 도달 시 해금"을 세우면 오출력이다 —
        // 신규 계정 첫 진입에서 프리팹 저작 문구가 그대로 새던 자리라 그런 장은 자물쇠 표식만 남긴다.
        if (TournamentProgress.IsChapterRankLocked(this.m_index))
        {
            this.ApplyRankLock(true, this.m_index);
        }
        else
        {
            this.ShowRankLockCopy(false);
            this.SetRankLockBadge(null);
        }

        // 자물쇠만 색이 살아남는다 — 잠김을 말하는 표식이 저 혼자 회색이면 읽히지 않는다(정점 ApplyLockedTone과 같은 제외 규약).
        UiGrayscale.Restore(this.m_introToned);
        this.m_introToned = UiGrayscale.Apply(this.gameObject,
            this.rankLockMark != null ? this.rankLockMark.transform : null);
    }

    // 자물쇠 안무를 미리 세워 재운다. 재워 둔 동안 화면에 새는 것은 없다 — 알갱이는 배율 0으로 태어나고
    // 뒷빛은 저작 알파에서 오르며, 멈춘 트윈은 몇 초를 기다려도 제 시작값을 화면에 쓰지 않는다.
    void StageLockFx()
    {
        this.m_lockTween = null;

        if (this.lockFx == null) return;

        Tween t_fx = this.lockFx.Play();
        if (t_fx == null || !t_fx.IsActive()) return;

        t_fx.Pause();
        this.m_lockTween = t_fx;
    }

    // 걷히는 박의 길이. 그쪽 저작(빛 알갱이 수·간격·터짐)이 쥐고 있어 산술로 흉내 내면 반드시 어긋난다 —
    // 세워 둔 트윈의 실측 길이만이 답이고, 세우지 못했을 때만 저작 폴백으로 돈다.
    float ShedDuration()
        => this.m_lockTween != null && this.m_lockTween.IsActive()
               ? Mathf.Max(0f, this.m_lockTween.Duration())
               : Mathf.Max(0f, this.introShed);

    // 자물쇠가 터지는 박. lockFx는 제 시퀀스를 스스로 돌리므로 남의 시퀀스에 끼우지 않고 재워 둔 것을 깨우기만 한다.
    void BreakIntroLock()
    {
        if (this.m_lockTween != null && this.m_lockTween.IsActive())
        {
            this.m_lockTween.Play();
            return;
        }

        if (this.rankLockMark != null) this.rankLockMark.SetActive(false);
    }

    // 해금의 결말. 제목·눈금을 따로 그리지 않고 진실을 한 번 그린다 —
    // 끝난 화면이 연출 없을 때와 같음을 산술이 아니라 같은 코드로 보장하는 자리다.
    void SettleIntro()
    {
        UiGrayscale.Restore(this.m_introToned);
        this.m_introToned = null;

        // 자연 종료에도 자물쇠 손잡이를 놓는다 — 중단 경로하고만 대칭이 맞지 않으면 죽은 트윈 참조가 남는다.
        this.m_lockTween = null;

        this.UnmuteClaimButton();

        this.m_introHold = false;
        this.Refresh();

        UiPunch.Play(this.transform, this.introPunch, this.introPunchTime);
    }

    // 연출이 도는 동안 [받기] 버튼을 클릭 경로에서 통째로 내린다.
    // interactable=false로는 모자라다 — uGUI는 isActiveAndEnabled만 보고 클릭을 그 자리에서 삼키므로,
    // 연출이 화면 한가운데 세워 둔 바로 그 띠를 탭해도 맵의 스킵까지 올라가지 못한다.
    // 자격 축(interactable)은 지금처럼 Refresh가 소유한다.
    void MuteClaimButton()
    {
        if (this.claimButton == null) return;
        if (this.m_claimMuted) return;

        this.m_claimMuted = true;
        this.claimButton.enabled = false;
    }

    // 내려 둔 버튼을 되돌린다. 빠지면 완주 보상 [받기]가 영영 죽으므로 중단·정상 종료·비활성 모든 경로에서 부른다.
    void UnmuteClaimButton()
    {
        if (!this.m_claimMuted) return;

        this.m_claimMuted = false;
        if (this.claimButton != null) this.claimButton.enabled = true;
    }

    void OnDisable() => this.AbortUnlock();

    // 수령은 흐름이 소유한다 — 자격 판정 · 팝업 · 지급이 한 자리에 있어야 띠와 판정이 갈리지 않는다.
    // 지급이 끝나면 OnChanged가 돌아 맵이 이 띠를 다시 그린다(여기서 직접 Refresh하지 않는 이유).
    // 버튼 리스너는 동기 델리게이트라 대기를 여기서 끊는다(RewardClaimPopup의 [획득]과 같은 형태).
    void OnClaimTapped() => this.OpenClaimAsync().Forget();

    async UniTaskVoid OpenClaimAsync()
    {
        if (this.m_index < 0) return;

        await TournamentChapterRewardFlow.Open(this.m_index);
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
