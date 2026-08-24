using System.Collections.Generic;

/// <summary>시퀀스를 처음부터 훑어 "각 스텝에 서 있을 때의 기능 해금 상태"를 미리 계산해 두는 순수 계산기.
///
/// 의미론은 <see cref="OutgameFeatureLock"/>.Recalculate의 거울이다 — 누적 해금(자기 칸 포함) · unlocksAll 고착 ·
/// locks 우선 · 저작된 unlocks가 하나도 없을 때의 전역 폴백까지 같아야 멀쩡한 저작을 오류로 찍지 않는다.
///
/// 다만 fail-open 경로(정지 판정 · ForceUnlockAllForDebug · 러너 미가동)는 일부러 모델링하지 않는다.
/// 저작 검증이 보려는 것은 "정상 진행"이고, 그 셋은 막힌 저작까지 열어 주어 증상을 가리는 쪽이다.</summary>
public sealed class TutorialSequenceState
{
    /// <summary>한 스텝에 서 있을 때의 해금 스냅샷(누적 해금 + 그 스텝만의 일시 잠금)</summary>
    public readonly struct StepState
    {
        static readonly HashSet<EOutgameFeature> s_empty = new HashSet<EOutgameFeature>();

        public readonly bool AllUnlocked;

        readonly HashSet<EOutgameFeature> m_unlocked;
        readonly HashSet<EOutgameFeature> m_locked;

        // 좌표는 싣지 않는다 — 이 값을 얻는 유일한 길이 TryGet(chapter, step, ...)이라 호출자가 이미 알고 있다
        public StepState(bool _allUnlocked, HashSet<EOutgameFeature> _unlocked, HashSet<EOutgameFeature> _locked)
        {
            AllUnlocked = _allUnlocked;
            m_unlocked  = _unlocked;
            m_locked    = _locked;
        }

        // 이 스텝까지의 누적 해금 — 자기 스텝의 unlocks가 이미 반영돼 있다(EnumerateUpTo가 자기 칸을 포함한다)
        public IReadOnlyCollection<EOutgameFeature> Unlocked => m_unlocked ?? s_empty;

        // 이 스텝 동안만 닫히는 기능(누적하지 않는다 — 다음 칸에서 저절로 풀린다)
        public IReadOnlyCollection<EOutgameFeature> Locked => m_locked ?? s_empty;

        /// <summary>그 기능이 이 스텝에서 열려 있는가(None은 항상 열림)</summary>
        public bool IsUnlocked(EOutgameFeature _feature)
        {
            if (_feature == EOutgameFeature.None) return true;

            // 일시 잠금이 해금보다 우선한다 — 이미 열린 기능도, 전체 해금 상태에서도 그 스텝 동안은 닫힌다
            if (m_locked != null && m_locked.Contains(_feature)) return false;

            return AllUnlocked || (m_unlocked != null && m_unlocked.Contains(_feature));
        }

        /// <summary>목록 한 줄 표시용 요약</summary>
        public string Summary
        {
            get
            {
                string t_head = AllUnlocked           ? "전체 해금"
                              : Unlocked.Count > 0    ? $"해금 {Unlocked.Count}"
                                                      : "해금 없음";

                return Locked.Count > 0 ? $"{t_head} · 잠금 {string.Join(", ", Locked)}" : t_head;
            }
        }
    }

    readonly Dictionary<(int, int), StepState> m_states = new Dictionary<(int, int), StepState>();

    /// <summary>시퀀스 전체를 한 번 훑어 스텝별 상태를 굽는다(_data가 null이면 빈 상태)</summary>
    public static TutorialSequenceState Build(OutgameTutorialData _data)
    {
        var t_result = new TutorialSequenceState();
        if (_data == null || _data.chapters == null) return t_result;

        // 전역 폴백의 판단 근거. unlocksAll은 세지 않는다 — OutgameFeatureLock.HasAnyAuthoredUnlock이 unlocks만 본다.
        bool t_hasAuthored = HasAnyAuthoredUnlock(_data);

        var  t_unlocked = new HashSet<EOutgameFeature>();
        bool t_all      = false;

        for (int t_c = 0; t_c < _data.chapters.Count; t_c++)
        {
            var t_chapter = _data.chapters[t_c];
            if (t_chapter == null) continue;

            for (int t_s = 0; t_s < t_chapter.StepCount; t_s++)
            {
                if (!t_chapter.TryGetStep(t_s, out var t_step)) continue;

                // 자기 칸의 저작이 자기 자신에게 이미 적용된다 — 이 순서를 뒤집으면 앵커 잠김 규칙이 오탐을 낸다
                if (t_step.UnlocksAll) t_all = true;
                AddAll(t_unlocked, t_step.Unlocks);

                var t_locked = new HashSet<EOutgameFeature>();
                AddAll(t_locked, t_step.Locks);

                // 저작된 해금이 시퀀스 어디에도 없으면 잠글 근거가 없다고 보고 전부 연다
                bool t_allHere = t_all || (t_unlocked.Count == 0 && !t_hasAuthored);

                t_result.m_states[(t_c, t_s)] =
                    new StepState(t_allHere, new HashSet<EOutgameFeature>(t_unlocked), t_locked);
            }
        }

        return t_result;
    }

    /// <summary>그 좌표의 상태(저작이 없는 칸이면 false)</summary>
    public bool TryGet(int _chapter, int _step, out StepState _state) => m_states.TryGetValue((_chapter, _step), out _state);

    // None은 담지 않는다(런타임 Collect·CollectLocks와 같은 규칙)
    static void AddAll(HashSet<EOutgameFeature> _set, IReadOnlyList<EOutgameFeature> _features)
    {
        if (_features == null) return;

        for (int t_i = 0; t_i < _features.Count; t_i++)
            if (_features[t_i] != EOutgameFeature.None) _set.Add(_features[t_i]);
    }

    static bool HasAnyAuthoredUnlock(OutgameTutorialData _data)
    {
        for (int t_c = 0; t_c < _data.chapters.Count; t_c++)
        {
            var t_chapter = _data.chapters[t_c];
            if (t_chapter == null) continue;

            for (int t_s = 0; t_s < t_chapter.StepCount; t_s++)
            {
                if (!t_chapter.TryGetStep(t_s, out var t_step) || t_step.Unlocks == null) continue;

                for (int t_i = 0; t_i < t_step.Unlocks.Count; t_i++)
                    if (t_step.Unlocks[t_i] != EOutgameFeature.None) return true;
            }
        }

        return false;
    }
}
