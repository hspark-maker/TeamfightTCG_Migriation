using System.Collections.Generic;

/// <summary>이전 모험 진행도를 현재 자율 안내와 콘텐츠 해금으로 이관한다.</summary>
public static class AdventureUnlock
{
    // 모험 도입이 온보딩 시퀀스의 마지막 챕터(인덱스 5)로 저작되던 시절의 좌표. 자율 챕터가 뒤에 늘어나도 이 값은 변하지 않는다.
    const int LEGACY_ADVENTURE_CHAPTER_INDEX = 5;

    // 세이브 흐름 버전. 1 = 도입 진행이 별도 플래그로 저장되던 판, 2 = 자율 챕터 낙인(CompletedTriggers)으로 통합된 판.
    const int FLOW_VERSION = 2;

    static TutorialSaveData Slot => DataSaveManager.Data.Tutorial;

    /// <summary>클라우드 세이브 채택 뒤 옛 모험 진행도를 옮긴다.</summary>
    public static void Initialize()
    {
        if (DataSaveManager.Data.Tutorial == null) DataSaveManager.Data.Tutorial = new TutorialSaveData();

        OutgameTutorialRunner.TryGetGuidedChapter(EOutgameTutorialTrigger.AdventureMapFirstOpen, out _, out var t_chapter);
        bool t_played = DataSaveManager.Data.Adventure?.ClearedNodeIds?.Count > 0;
        if (MigrateLegacyProgress(Slot, t_chapter, t_played)) Save();

    }

    /// <summary>순수 마이그레이션 — 넘겨받은 값 객체만 고친다. v0은 옛 온보딩 좌표에서 해금·완주를 되찾고,
    /// v1은 별도 플래그였던 도입 완주를 자율 챕터 낙인으로 옮긴다. 이미 최신이면 false.</summary>
    public static bool MigrateLegacyProgress(TutorialSaveData _slot, OutgameTutorialChapter _chapter, bool _hasProgress)
    {
        if (_slot == null || _slot.AdventureFlowVersion >= FLOW_VERSION) return false;

        bool t_introCompleted = _slot.AdventureIntroCompleted;

        if (_slot.AdventureFlowVersion == 0)
        {
            // 데이터 없이 옮기면 신규 계정을 해금해 버린다.
            if (_chapter == null) return false;

            int t_moved = -1;
            for (int t_i = 0; t_i < _chapter.StepCount; t_i++)
                if (_chapter.TryGetStep(t_i, out var t_step) && t_step.StepId == _slot.StepId) { t_moved = t_i; break; }

            bool t_wasAdventure = t_moved >= 0 || _slot.ChapterIndex >= LEGACY_ADVENTURE_CHAPTER_INDEX;
            if (_slot.OutgameCompleted || t_wasAdventure || _hasProgress)
            {
                // 도입 도중이던 계정은 완주가 아니다 — 낙인 없이 두면 알림 점이 자율 챕터를 다시 부른다.
                t_introCompleted        = _slot.OutgameCompleted || _hasProgress;
                _slot.AdventureUnlocked = true;
                _slot.OutgameCompleted  = true;
            }
        }

        if (t_introCompleted)
        {
            string t_key = EOutgameTutorialTrigger.AdventureMapFirstOpen.ToString();
            if (_slot.CompletedTriggers == null) _slot.CompletedTriggers = new List<string>();
            if (!_slot.CompletedTriggers.Contains(t_key)) _slot.CompletedTriggers.Add(t_key);
        }

        _slot.AdventureFlowVersion = FLOW_VERSION;
        return true;
    }

    static void Save() => DataSaveManager.Save();
}
