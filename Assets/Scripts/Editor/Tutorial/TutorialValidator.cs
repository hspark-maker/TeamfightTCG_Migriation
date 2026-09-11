using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>저작 문제의 심각도. Error는 "진행이 실제로 막히거나 세이브가 깨진다"에만 쓴다 —
/// 남발하면 목록이 배경 소음이 되어 진짜 정지 사고를 가린다.</summary>
public enum ETutorialIssueLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>저작 문제 한 건 — 어디가(Coord) 무엇이 틀렸고(Message) 어떻게 고치는가(Fix)</summary>
public readonly struct TutorialIssue
{
    public readonly ETutorialIssueLevel Level;
    public readonly int    Chapter;   // 트리거 시퀀스에서는 엔트리 인덱스
    public readonly int    Step;
    public readonly int    StepId;    // 없으면 0
    public readonly string Rule;
    public readonly string Message;
    public readonly string Fix;

    public TutorialIssue(ETutorialIssueLevel _level, int _chapter, int _step, int _stepId,
                         string _rule, string _message, string _fix)
    {
        Level   = _level;
        Chapter = _chapter;
        Step    = _step;
        StepId  = _stepId;
        Rule    = _rule;
        Message = _message;
        Fix     = _fix;
    }

    public string Coord => StepId > 0 ? $"{Chapter}-{Step} #{StepId}" : $"{Chapter}-{Step}";
}

/// <summary>튜토리얼 저작을 플레이 없이 정적으로 판정한다.
///
/// 이 도구가 필요한 이유: 저작 실수는 런타임에 조용히 삼켜지거나(TutorialStepExecutor.Fail — 기본 Skip이면
/// 경고 한 줄 뒤 그냥 전진) 초기화 로그에만 뜬다. 게다가 진행이 막히면 fail-open이 남은 기능을 전부 열어
/// (OutgameFeatureLock.NotifyStalled) 증상 자체가 "정상처럼" 보인다. 그래서 사람 눈으로는 잡히지 않는다.</summary>
public static class TutorialValidator
{
    // 액션이 쓰지 않는 축에 값이 남았는지 보려면 저작값 원본이 필요한데, TutorialStepDef의 게터 상당수가
    // 스스로 게이트를 걸어 그 값을 감춘다(예: FreeOfCharge는 액션이 안 쓰면 무조건 false를 준다).
    // 그래서 직렬화 필드를 직접 읽는다 — 그 이름은 세이브 계약이라 함부로 바뀌지 않는 자리다.
    static readonly (EStepField Field, FieldInfo Info, string Label)[] s_probes;

    // anchorCard만 축이 액션이 아니라 앵커라 액션 테이블 밖에 남는다(TutorialStepDef.UsesAnchorCard)
    static readonly FieldInfo s_anchorCard;

    static TutorialValidator()
    {
        // useDim은 일부러 뺐다 — 기본값이 true라 "남은 값"과 손대지 않은 기본 상태를 구분할 수 없다(전부 오탐이 된다).
        var t_axes = new (EStepField Field, string Name, string Label)[]
        {
            (EStepField.Anchor,           "anchor",           "앵커"),
            (EStepField.Spotlight,        "spotlight",        "함께 밝힐 영역"),
            (EStepField.GuideMessage,     "guideMessage",     "안내 문구"),
            (EStepField.MessagePlacement, "messageAtBottom",  "문구 하단 배치"),
            (EStepField.FreeOfCharge,     "freeOfCharge",     "무료 지급"),
            (EStepField.WaitUnlockIntro,  "waitUnlockIntro",  "해금 연출 대기"),
            (EStepField.RewardTitle,      "rewardTitle",      "보상 제목"),
            (EStepField.ParallelGain,     "parallelGain",     "획득 연출 병행"),
            (EStepField.Pack,             "packId",           "팩 ID"),
            (EStepField.PackPriceLabel,   "packPriceLabel",   "가격 표기"),
            (EStepField.Scenario,         "scenario",         "시나리오"),
            (EStepField.ShowDeckGate,     "showDeckGate",     "덱 게이트"),
            (EStepField.DeckName,         "deckName",         "덱 이름"),
            (EStepField.FailurePolicy,    "onFailure",        "실패 정책"),
            (EStepField.Card,             "cardId",           "카드 ID"),
            (EStepField.Cards,            "cardIds",          "카드 ID 묶음"),
            (EStepField.ContentIntros,    "contentIntros",    "콘텐츠 해금 소개"),
        };

        var t_probes = new List<(EStepField, FieldInfo, string)>(t_axes.Length);
        for (int t_i = 0; t_i < t_axes.Length; t_i++)
        {
            var t_info = FieldOf(t_axes[t_i].Name);
            if (t_info != null) t_probes.Add((t_axes[t_i].Field, t_info, t_axes[t_i].Label));
        }

        s_probes     = t_probes.ToArray();
        s_anchorCard = FieldOf("anchorCardId");
    }

    /// <summary>온보딩 시퀀스 점검. 좌표 순서로 돌려준다(심각도 정렬은 창이 한다).</summary>
    public static List<TutorialIssue> Validate(OutgameTutorialData _data)
    {
        var t_issues = new List<TutorialIssue>();
        if (_data == null || _data.chapters == null) return t_issues;

        if (!ContentUnlockConfig.TryValidate(_data.contentUnlocks, out string t_unlockError))
            t_issues.Add(new TutorialIssue(ETutorialIssueLevel.Error, 0, 0, 0, "ContentUnlock",
                t_unlockError, "SO 최상위 콘텐츠 해금 조건을 수정하세요."));

        ValidateContentIntroDefinitions(_data, t_issues);

        var t_state    = TutorialSequenceState.Build(_data);
        var t_ids      = new Dictionary<int, string>();
        var t_triggers = new Dictionary<EOutgameTutorialTrigger, int>();

        bool t_seenGuided = false;

        for (int t_c = 0; t_c < _data.chapters.Count; t_c++)
        {
            var  t_chapter = _data.chapters[t_c];
            bool t_guided  = t_chapter != null && t_chapter.IsGuided;

            // (19) 자율 챕터 뒤에 선 강제 챕터. 런타임은 선두의 연속 강제 챕터까지만 강제 커서로 읽으므로
            //      이 챕터는 조용히 잘린다 — 졸업이 앞당겨지고 그 안의 지급·해금이 영영 돌지 않는다.
            if (t_guided) t_seenGuided = true;
            else if (t_seenGuided)
                t_issues.Add(new TutorialIssue(ETutorialIssueLevel.Error, t_c, 0, 0, "자율 뒤의 강제 챕터",
                                               "자율 챕터 뒤에 강제 챕터가 있습니다 — 러너가 이 챕터를 강제 시퀀스로 읽지 않아 통째로 건너뜁니다.",
                                               "강제 챕터를 자율 챕터 앞으로 옮기거나, 이 챕터를 자율로 바꾸세요."));

            if (t_chapter != null) ValidateChapterKind(t_chapter, t_c, t_triggers, t_issues);

            // (8) 스텝이 없는 챕터. 진행이 막히지는 않는다 — OutgameTutorialRunner.TryGetNext가 빈 챕터를 건너뛰고,
            //     좌표가 서더라도 CloseOrWarnOnMissingStep이 다음 좌표로 정정해 Advanced를 준다(런타임 판정도 Warning이다).
            //     그래도 저작이 미완이라는 신호라 남긴다.
            if (t_chapter == null || t_chapter.StepCount == 0)
            {
                t_issues.Add(new TutorialIssue(ETutorialIssueLevel.Warning, t_c, 0, 0, "빈 챕터",
                                               "이 챕터에 스텝이 하나도 없습니다 — 러너가 통째로 건너뜁니다(저작이 남았거나 챕터 행이 남은 것입니다).",
                                               "스텝을 저작하거나 챕터 행을 지우세요."));
                continue;
            }

            ScanDeckGates(t_chapter, out bool[] t_gateOpen, out bool[] t_gateUnclosed);

            for (int t_s = 0; t_s < t_chapter.StepCount; t_s++)
            {
                // (17) 목록 중간의 빈 행. 런타임이 진짜로 닫히는 유일한 경로다 —
                //      CloseOrWarnOnMissingStep이 이 좌표에서 Failed를 돌려주고, 그것을 받은 브리지가
                //      fail-open으로 남은 기능을 전부 연다(증상이 "다 열렸다"로 위장된다).
                if (!t_chapter.TryGetStep(t_s, out var t_def))
                {
                    t_issues.Add(new TutorialIssue(ETutorialIssueLevel.Error, t_c, t_s, 0, "빈 스텝 행",
                                                   "이 칸에 스텝이 없습니다 — 좌표가 여기 서면 진행이 Failed로 닫히고 fail-open이 남은 기능을 전부 엽니다.",
                                                   "행을 지우거나 액션을 저작하세요."));
                    continue;
                }

                ValidateStepId(t_def, t_c, t_s, t_ids, t_issues);

                if (t_guided)
                {
                    ValidateGuidedStep(t_def, t_c, t_s, t_s == t_chapter.StepCount - 1, t_issues);
                }
                else
                {
                    ValidateAnchorGate(t_def, t_c, t_s, t_state, t_issues);
                    ValidateDeckGate(t_def, t_c, t_s, t_gateOpen[t_s], t_gateUnclosed[t_s], t_issues);
                }

                ValidateStep(t_def, t_c, t_s, !t_guided, t_issues);
                ValidateContentIntroStep(_data, t_def, t_c, t_s, t_issues);
            }
        }

        return t_issues;
    }

    // ── 챕터 성격 규칙 ──────────────────────────────────────────────────────

    static void ValidateContentIntroDefinitions(OutgameTutorialData _data, List<TutorialIssue> _issues)
    {
        if (_data.contentIntros == null) return;

        var t_seen = new HashSet<EContentUnlockIntro>();
        for (int t_i = 0; t_i < _data.contentIntros.Count; t_i++)
        {
            var t_intro = _data.contentIntros[t_i];
            string t_error = null;
            if (t_intro == null || t_intro.content == EContentUnlockIntro.None
             || !Enum.IsDefined(typeof(EContentUnlockIntro), t_intro.content))
                t_error = $"해금 소개 정의 {t_i}의 콘텐츠가 비어 있거나 유효하지 않습니다.";
            else if (!t_seen.Add(t_intro.content))
                t_error = $"{t_intro.content} 해금 소개 정의가 중복입니다.";
            else if (string.IsNullOrWhiteSpace(t_intro.contentName)
                  || string.IsNullOrWhiteSpace(t_intro.description) || t_intro.icon == null)
                t_error = $"{t_intro.content} 해금 소개의 이름·본문·아이콘 중 빠진 값이 있습니다.";

            if (t_error != null)
                _issues.Add(new TutorialIssue(ETutorialIssueLevel.Error, 0, 0, 0, "해금 소개 정의",
                    t_error, "SO 최상위 콘텐츠 해금 소개 목록을 수정하세요."));
        }
    }

    static void ValidateContentIntroStep(OutgameTutorialData _data, TutorialStepDef _def,
                                        int _chapter, int _index, List<TutorialIssue> _issues)
    {
        if (_def.Action != EOutgameTutorialAction.ContentUnlockIntro) return;

        var t_contents = _def.ContentIntros;
        if (t_contents == null || t_contents.Count == 0)
        {
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "해금 소개 대상 없음",
                "해금 소개 스텝의 콘텐츠 목록이 비어 있습니다.", "소개할 콘텐츠를 하나 이상 지정하세요.");
            return;
        }

        var t_seen = new HashSet<EContentUnlockIntro>();
        for (int t_i = 0; t_i < t_contents.Count; t_i++)
        {
            var t_content = t_contents[t_i];
            string t_error = null;
            if (t_content == EContentUnlockIntro.None || !Enum.IsDefined(typeof(EContentUnlockIntro), t_content))
                t_error = "해금 소개 대상에 None 또는 유효하지 않은 콘텐츠가 있습니다.";
            else if (!t_seen.Add(t_content))
                t_error = $"해금 소개 대상 {t_content}가 중복입니다.";
            else if (!_data.TryGetContentIntro(t_content, out _))
                t_error = $"{t_content} 해금 소개 정의가 없습니다.";

            if (t_error != null)
                Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "해금 소개 대상",
                    t_error, "스텝 대상 목록과 SO 최상위 콘텐츠 해금 소개 목록을 맞추세요.");
        }
    }

    static void ValidateChapterKind(OutgameTutorialChapter _chapter, int _index,
                                    Dictionary<EOutgameTutorialTrigger, int> _triggers, List<TutorialIssue> _issues)
    {
        if (!_chapter.IsGuided)
        {
            // 강제 챕터의 발화 키는 읽히지 않는다 — 자율에서 강제로 되돌린 흔적일 가능성이 크다.
            if (_chapter.EditorTrigger != EOutgameTutorialTrigger.None)
                _issues.Add(new TutorialIssue(ETutorialIssueLevel.Warning, _index, 0, 0, "쓰지 않는 발화 키",
                                              "강제 챕터에 trigger가 저작돼 있습니다 — 강제 챕터는 발화 키를 읽지 않습니다.",
                                              "None으로 되돌리거나, 이 챕터를 자율로 바꾸세요."));
            return;
        }

        var t_trigger = _chapter.Trigger;

        // (20) 발화 키가 없으면 깨울 수단이 없다 — 완주 낙인의 식별도 이 값이 하므로 대체 경로도 없다
        if (t_trigger == EOutgameTutorialTrigger.None)
        {
            _issues.Add(new TutorialIssue(ETutorialIssueLevel.Error, _index, 0, 0, "발화 키 없음",
                                          "자율 챕터의 trigger가 None이라 영영 발화하지 않습니다 — 저작 전체가 죽은 값입니다.",
                                          "trigger에 발화 키를 고르세요."));
        }
        else if (_triggers.TryGetValue(t_trigger, out int t_first))
        {
            // (24) 같은 트리거의 자율 챕터가 둘이면 먼저 나온 챕터가 이긴다(OutgameTutorialRunner.TryGetGuidedChapter) — 뒤는 죽은 저작이다
            _issues.Add(new TutorialIssue(ETutorialIssueLevel.Error, _index, 0, 0, "발화 키 중복",
                                          $"{t_first}편과 같은 발화 키 {t_trigger}입니다 — 먼저 나온 챕터만 발화하고 이 챕터는 영영 서지 않습니다.",
                                          "발화 키를 다르게 고르거나 두 챕터를 합치세요."));
        }
        else
        {
            _triggers[t_trigger] = _index;

            // (25) 폐기된 발화 키. 발화처가 0이라 저작해도 아무도 깨우지 않는다
            if (t_trigger == EOutgameTutorialTrigger.FirstEvolutionReady)
                _issues.Add(new TutorialIssue(ETutorialIssueLevel.Warning, _index, 0, 0, "폐기된 발화 키",
                                              $"{t_trigger}는 발화처가 없는 폐기된 키입니다 — 이 챕터는 아무도 깨우지 않습니다.",
                                              "살아 있는 발화 키로 바꾸세요."));
        }

        // (26) 선행 기능이 없으면 알림 점이 졸업 직후부터 항상 뜬다 — 의도라면 그대로 두어도 된다
        if (_chapter.Prerequisite == EOutgameFeature.None
         && t_trigger != EOutgameTutorialTrigger.ContentUnlocksAvailable)
            _issues.Add(new TutorialIssue(ETutorialIssueLevel.Info, _index, 0, 0, "선행 기능 없음",
                                          "prerequisite가 None이라 졸업 직후부터 알림 점이 뜹니다.",
                                          "안내가 가리키는 화면을 여는 기능을 선행 기능으로 두면 잠긴 동안 점이 숨습니다."));
    }

    // 자율 챕터 스텝만의 규약. 메모리 커서·로비 안 완결·좌표 파생 없음이라는 세 전제에서 나온다.
    static void ValidateGuidedStep(TutorialStepDef _def, int _chapter, int _index, bool _isLast, List<TutorialIssue> _issues)
    {
        // (21) 해금·잠금은 강제 좌표에서만 파생된다 — 자율 스텝의 값은 읽히지 않는데 저작자는 잠긴 줄 안다
        if (_def.UnlocksAll || HasAny(_def.Unlocks) || HasAny(_def.Locks))
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "자율 스텝의 해금/잠금",
                "자율 챕터 스텝에 해금/잠금이 저작돼 있습니다 — 해금은 강제 좌표에서만 파생되어 이 값은 읽히지 않습니다.",
                "지우세요. 정말 필요한 잠금이면 강제 시퀀스로 옮겨야 합니다.");

        // (23) 강제 전용 액션. 지급·전투 진입·첫 랭크 준비는 세이브 좌표와 되감기를 전제로 하고, 자율에는 그 둘이 없다
        switch (_def.Action)
        {
            case EOutgameTutorialAction.AutoPurchase:
            case EOutgameTutorialAction.DeckGrant:
            case EOutgameTutorialAction.CardGrant:
            case EOutgameTutorialAction.CardSetGrant:
            case EOutgameTutorialAction.PackNotice:
            case EOutgameTutorialAction.EnterFirstRank:
            case EOutgameTutorialAction.BattleEntry:
            case EOutgameTutorialAction.AutoBattle:
            case EOutgameTutorialAction.WaitPurchase:
            case EOutgameTutorialAction.WaitPackOpen:
            case EOutgameTutorialAction.WaitAlbumInsert:
            case EOutgameTutorialAction.DeckAutoEquip:
                Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "자율에서 못 쓰는 액션",
                    $"{_def.Action}는 세이브 좌표·되감기·지급 재생을 전제로 하는 강제 전용 액션입니다 — 자율 챕터에서는 메모리 커서라 그 전제가 없습니다.",
                    "강제 시퀀스로 옮기거나 다른 액션으로 바꾸세요.");
                return;
        }

        // (22) 자율은 로비 안에서 시작해 로비 안에서 끝난다 — 씬을 떠나는 스텝은 마지막이어야 하고, 그때 낙인은 이탈 직전 클릭에서 찍힌다
        if (_def.LeavesScene)
        {
            if (!_isLast)
                Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "자율 도중 씬 이탈",
                    $"{_def.Action}가 씬을 떠나는데 뒤에 스텝이 남아 있습니다 — 자율 커서는 메모리라 씬을 넘지 못하고 남은 스텝은 영영 서지 않습니다.",
                    "씬을 떠나는 스텝을 챕터 마지막으로 옮기세요.");
            else
                Add(_issues, ETutorialIssueLevel.Info, _def, _chapter, _index, "이탈로 완주",
                    "마지막 스텝이 씬을 떠납니다 — 완주 낙인은 이탈 직전 클릭에서 찍힙니다.",
                    null);
        }
    }

    // ── 좌표에 기대는 규칙 ──────────────────────────────────────────────────

    // (1)(2) 세이브가 붙잡는 것은 stepId 하나뿐이다 — 없으면 좌표로만 지목되어 저작이 바뀔 때 밀리고,
    //        겹치면 앞 칸이 이겨 진행이 되감긴다(그 사이의 지급이 다시 실행된다).
    static void ValidateStepId(TutorialStepDef _def, int _chapter, int _index,
                               Dictionary<int, string> _ids, List<TutorialIssue> _issues)
    {
        if (_def.StepId <= 0)
        {
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "스텝 ID 없음",
                $"{_def.Action} 스텝에 ID가 없습니다 — 세이브가 좌표로만 지목해 앞에 스텝 한 칸만 끼어도 진행도가 밀립니다.",
                "시퀀스 SO를 우클릭해 [스텝 ID 부여]를 돌리세요.");
            return;
        }

        if (_ids.TryGetValue(_def.StepId, out string t_first))
        {
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "스텝 ID 중복",
                $"{t_first}과(와) 같은 ID #{_def.StepId}입니다(행 복제?) — 앞 칸이 이겨 진행이 그리로 되감기고 그 사이 지급이 다시 실행됩니다.",
                "시퀀스 SO를 우클릭해 [스텝 ID 부여]를 돌리세요.");
            return;
        }

        _ids[_def.StepId] = $"{_chapter}-{_index}";
    }

    // (3)(4) 앵커가 가리키는 위젯이 잠겨 있으면 유저가 누를 수 없고 완료 신호가 영영 오지 않는다.
    //        둘은 같은 증상의 서로 다른 원인이라 한 번만 보고한다(자기 locks가 원인이면 그쪽만).
    static void ValidateAnchorGate(TutorialStepDef _def, int _chapter, int _index,
                                   TutorialSequenceState _state, List<TutorialIssue> _issues)
    {
        var t_anchor = _def.Anchor;
        if (t_anchor == EOutgameTutorialAnchor.None) return;

        var t_gate = TutorialAnchorMeta.Of(t_anchor).Gate;
        if (t_gate == EOutgameFeature.None) return;   // 잠금 키가 없는 위젯은 잠길 대상이 없다

        if (Contains(_def.Locks, t_gate))
        {
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "자기 발등 잠금",
                $"이 스텝의 locks가 자기 앵커({t_anchor})의 기능 {t_gate}을(를) 닫습니다 — 눌러야 할 대상을 스스로 막아 진행이 멎습니다.",
                $"locks에서 {t_gate}을(를) 빼세요(옆길만 막고 싶다면 다른 기능을 고르세요).");
            return;
        }

        if (!_state.TryGet(_chapter, _index, out var t_step)) return;

        if (!t_step.IsUnlocked(t_gate))
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "앵커 잠김",
                $"앵커 {t_anchor}의 기능 {t_gate}이(가) 이 스텝에서 아직 잠겨 있습니다 — 게이트가 열리지 않아 무한 대기합니다.",
                $"이 스텝까지의 unlocks에 {t_gate}을(를) 넣으세요(같은 스텝에 넣어도 자기 자신에게 적용됩니다).");
    }

    // (6) 덱 게이트가 세우는 화면은 시나리오가 붙잡는 것이라 앱을 끄면 사라진다 —
    //     그 구간에서 재초기화하면 좌표가 진입 스텝으로 되감기고 사이 스텝이 다시 재생된다.
    static void ValidateDeckGate(TutorialStepDef _def, int _chapter, int _index,
                                 bool _inSpan, bool _unclosed, List<TutorialIssue> _issues)
    {
        if (_unclosed)
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "덱 게이트 미종료",
                "덱 게이트를 켠 BattleEntry인데 같은 챕터 안에서 BattleStart(또는 AutoBattle)로 닫히지 않습니다 — 덱 화면이 열린 채 챕터가 끝납니다.",
                "같은 챕터 안에 전투 시작 스텝을 두세요.");

        if (_inSpan && IsGrantOnEnter(_def.Action))
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "게이트 구간 지급",
                $"덱 게이트가 열려 있는 구간의 {_def.Action} — 이 구간에서 재초기화하면 좌표가 진입 스텝으로 되감겨 지급이 다시 실행됩니다.",
                "이 스텝을 게이트 구간 밖(전투 진입 전 또는 전투 후)으로 옮기세요.");
    }

    // 덱 게이트가 열려 있는 구간을 미리 표시한다.
    // 여는 것은 BattleEntry뿐이다 — AutoBattle은 켜더라도 그 자리에서 전투 씬으로 나가 "사이 구간"이 생기지 않는다.
    static void ScanDeckGates(OutgameTutorialChapter _chapter, out bool[] _open, out bool[] _unclosed)
    {
        int t_count = _chapter.StepCount;

        _open     = new bool[t_count];
        _unclosed = new bool[t_count];

        for (int t_s = 0; t_s < t_count; t_s++)
        {
            if (!_chapter.TryGetStep(t_s, out var t_def)) continue;
            if (t_def.Action != EOutgameTutorialAction.BattleEntry || !t_def.ShowDeckGate) continue;

            int t_close = -1;
            for (int t_n = t_s + 1; t_n < t_count; t_n++)
            {
                if (!_chapter.TryGetStep(t_n, out var t_next)) continue;
                if (t_next.Action != EOutgameTutorialAction.BattleStart
                 && t_next.Action != EOutgameTutorialAction.AutoBattle) continue;

                t_close = t_n;
                break;
            }

            // 닫히지 않았으면 챕터 끝까지가 그 구간이다 — 되풀이 위험은 닫힘 여부와 무관하게 그대로다
            int t_end = t_close < 0 ? t_count : t_close;
            for (int t_i = t_s + 1; t_i < t_end; t_i++) _open[t_i] = true;

            if (t_close < 0) _unclosed[t_s] = true;
        }
    }

    // ── 좌표와 무관한 규칙(강제·자율 공용) ──────────────────────────────────

    static void ValidateStep(TutorialStepDef _def, int _chapter, int _index, bool _forced, List<TutorialIssue> _issues)
    {
        var t_action = _def.Action;

        // (5) Halt는 좌표를 되돌려 재시도를 노리는 정책인데, 앵커도 완료 신호도 없으면 되돌려 봐야 다시 세울 수단이 없다.
        //     되돌린 좌표가 강제는 세이브에 남아 다음 초기화를 노릴 수라도 있지만, 자율은 메모리 전용이라 그 기회조차 없다.
        if (_def.OnFailure == EOutgameTutorialFailure.Halt
         && _def.Completion == EOutgameTutorialCompletion.Auto
         && _def.Anchor == EOutgameTutorialAnchor.None)
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "재개 불가 Halt",
                $"{t_action}가 Halt인데 앵커도 완료 신호도 없습니다 — " + (_forced
                    ? "되돌려도 이 초기화에서 다시 세울 수단이 없어 그 자리에서 안내가 끝납니다(재시도는 다음 초기화뿐입니다)."
                    : "자율 커서는 메모리 전용이라 되돌린 자리에서 이 안내가 그대로 끝납니다."),
                "onFailure를 Skip으로 바꾸거나, 되돌아왔을 때 진행을 다시 세울 앵커를 주세요.");

        // (7) 액션이 요구하는 참조가 비면 실행기가 실패 분기로 빠진다 — 기본 Skip이면 경고 한 줄 남기고 그냥 전진한다
        ValidatePack(_def, t_action, _chapter, _index, _issues);

        if (TutorialStepDef.UsesCard(t_action) && _def.CardId <= 0)
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "카드 미배선",
                $"{t_action}가 지급할 카드가 비어 있습니다.",
                "cardId에 카드 ID를 배선하세요.");

        if (TutorialStepDef.UsesCards(t_action)) ValidateCards(_def, _chapter, _index, _issues);

        // 설명 스텝(Confirm)만 예외다 — 강조 없이 문구만 띄우는 저작이 정상이고 완료가 딤 탭이라 진행이 막히지 않는다
        // (OutgameTutorialBridge). 나머지는 게이트를 못 걸고 CloseGate로 빠져 완료 신호가 영영 오지 않는다.
        if (TutorialStepDef.UsesAnchor(t_action) && _def.Anchor == EOutgameTutorialAnchor.None
         && _def.Completion != EOutgameTutorialCompletion.Confirm)
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "앵커 미지정",
                $"{t_action}는 지목할 타깃이 있어야 하는데 앵커가 None입니다 — 브리지가 게이트를 걸지 못하고 안내가 그 자리에서 닫힙니다.",
                "anchor를 고르세요.");

        // (10) 같은 미배선이라도 결말이 갈린다.
        //      DeckGrant는 시나리오가 덱의 정본이라 없으면 Fail로 빠지고(TutorialStepExecutor.EnterDeckGrant),
        //      전투 진입 계열은 실패로 치지 않고 대본 없는 일반 전투가 열린다("저하된 성공").
        if (TutorialStepDef.UsesScenario(t_action) && _def.Scenario == null)
        {
            if (t_action == EOutgameTutorialAction.DeckGrant)
                Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "덱 정본 미배선",
                    "DeckGrant에 시나리오가 없습니다 — 덱이 지급되지 않고 조용히 지나갑니다(기본 Skip이면 경고 한 줄뿐입니다).",
                    "scenario에 덱의 정본이 될 TutorialScenarioData를 배선하세요.");
            else
                Add(_issues, ETutorialIssueLevel.Warning, _def, _chapter, _index, "시나리오 미배선",
                    $"{t_action}에 시나리오가 없습니다 — 실패로 치지 않고 대본 없는 일반 전투가 열립니다.",
                    "scenario에 TutorialScenarioData를 배선하세요.");
        }

        // (11) 폐기된 기능. 소비처가 0이라 여닫아도 아무 일도 일어나지 않는다
        //      (자율 스텝에서는 (21)이 이미 더 넓게 잡으므로 중복해서 쏟지 않는다)
        if (_forced && (Contains(_def.Unlocks, EOutgameFeature.CollectionHarvest)
                         || Contains(_def.Locks,   EOutgameFeature.CollectionHarvest)))
            Add(_issues, ETutorialIssueLevel.Warning, _def, _chapter, _index, "폐기된 기능",
                "unlocks/locks에 CollectionHarvest(구 도감 수확)가 있습니다 — 소비처가 없어 아무 것도 여닫지 않습니다.",
                "그 항목을 지우세요.");

        // (12) 등록하는 위젯이 없으면 브리지가 OnRegistered를 무기한 기다린다(OutgameTutorialBridge.TryOpenGate)
        if (_def.Anchor != EOutgameTutorialAnchor.None && !TutorialAnchorMeta.Of(_def.Anchor).IsRegistered)
            Add(_issues, ETutorialIssueLevel.Warning, _def, _chapter, _index, "앵커 미등록",
                $"앵커 {_def.Anchor}를 등록하는 위젯이 프로젝트 어디에도 없습니다 — 게이트가 등록 통지를 무기한 기다립니다.",
                "그 위젯에 TutorialAnchor를 붙여 키를 배선하거나, 앵커를 등록된 것으로 바꾸세요.");

        // (12-b) 함께 밝힐 영역은 없어도 진행을 막지 않는다(강조 없이 흐른다) — 그래서 켠 저작만 조용히 무효가 된다
        ValidateSpotlight(_def, _chapter, _index, _issues);

        // (13) 비면 하드코딩 폴백이 대신 서기 때문에 미저작이 화면상 정상으로 보인다(TutorialStepExecutor.TitleOf)
        if (TutorialStepDef.UsesRewardTitle(t_action) && string.IsNullOrEmpty(_def.RewardTitle))
            Add(_issues, ETutorialIssueLevel.Warning, _def, _chapter, _index, "보상 제목 없음",
                $"{t_action}의 보상 제목이 비었습니다 — 기본 문구가 대신 서서 미저작이 정상처럼 보입니다.",
                "rewardTitle을 채우세요(기본 문구를 쓸 작정이면 무시해도 됩니다).");

        ValidateLeftovers(_def, _chapter, _index, _issues);
    }

    // 중간의 빈 칸은 문제 삼지 않는다 — 승인된 저작이다(TutorialStepDef의 cards 툴팁 "빈 칸(None)은 건너뛴다",
    // TutorialStepExecutor.ToIds "빈 칸을 남긴 세트도 그대로 지급되어야 한다").
    // 실제로 무의미한 것은 지급이 0장이 되는 경우뿐이다 — 목록이 비었거나 전부 빈 칸일 때.
    static void ValidateCards(TutorialStepDef _def, int _chapter, int _index, List<TutorialIssue> _issues)
    {
        var t_cards = _def.CardIds;

        if (t_cards != null)
            for (int t_i = 0; t_i < t_cards.Count; t_i++)
                if (t_cards[t_i] > 0) return;

        Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "카드 묶음 비었음",
            $"{_def.Action}가 지급할 카드가 한 장도 없습니다 — 보상 화면만 서고 아무 것도 지급되지 않습니다.",
            "cards에 지급할 카드를 넣으세요(중간의 빈 칸은 그대로 두어도 됩니다).");
    }

    // (7)(경미) 팩. 미배선이 실제로 아프게 끝나는 것은 셋이다 — AutoPurchase는 null 팩을 물고 구매가 깨지고(EnterAutoPurchase),
    // PackNotice는 진입 즉시 null 검사로 Fail하며(EnterPackNotice), 지급 3액션은 서버에 보낼 키가 없어
    // 화면만 서고 소유가 늘지 않는다(TutorialStepExecutor.GrantPackIdOf). 나머지 둘은 문서화된 폴백이라 문제 삼지 않는다:
    // WaitPurchase는 진열을 덮어쓰지 않고 상점 기본 진열이 서며 완료는 구매 신호가 그대로 주고,
    // DeckAutoEquip은 "미지정이면 일반 편성 규칙"이다(OutgameTutorialRunner.TryGetForcedDeck).
    static void ValidatePack(TutorialStepDef _def, EOutgameTutorialAction _action, int _chapter, int _index,
                             List<TutorialIssue> _issues)
    {
        if (!TutorialStepDef.UsesPack(_action)) return;

        if (string.IsNullOrEmpty(_def.PackId))
        {
            if (IsCardGrant(_action))
                Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "지급 팩 미배선",
                    $"{_action}가 무엇을 줄지 정하는 팩이 비어 있습니다 — 서버에 보낼 키가 없어 화면만 서고 소유는 늘지 않습니다.",
                    "pack에 그 스텝이 지급할 무료 팩(price 0)을 배선하세요.");
            else if (_action == EOutgameTutorialAction.AutoPurchase || _action == EOutgameTutorialAction.PackNotice)
                Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "팩 미배선",
                    $"{_action}가 팩을 요구하는데 비어 있습니다 — 실패 분기로 빠집니다(기본 Skip이면 경고 한 줄뿐입니다).",
                    "pack에 CardPackData를 배선하세요.");

            return;
        }

        // 값이 붙은 팩을 지급으로 보내면 서버가 거절한다 — 화면은 그대로 서므로 증상이 "받았는데 안 늘었다"로만 보인다.
        // 가격의 진실원은 시트라 에디터가 읽는 값이 배포본과 다를 수 있어 Error까지 올리지 않는다.
        if (!PackSpec.TryGetPack(_def.PackId, out CardPack t_pack))
        {
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "팩 ID 오류",
                $"CardPack 표에 '{_def.PackId}'가 없습니다.", "packId를 CardPack.packId와 맞추세요.");
            return;
        }

        if (IsCardGrant(_action) && t_pack.price != 0)
            Add(_issues, ETutorialIssueLevel.Warning, _def, _chapter, _index, "지급 팩이 유료",
                $"팩 '{_def.PackId}'의 가격이 {t_pack.price}입니다 — 서버는 가격이 붙은 팩의 튜토리얼 지급을 거절합니다.",
                "그 팩의 price를 0으로 두거나, 무료 팩으로 바꾸세요(가격 진실원은 CardPack 시트입니다).");

        // 자동 편성만 pack.Pool을 직독한다(TutorialStepDef.TryGetForcedDeck) — 풀이 0이면 미지정과 똑같이
        // 일반 편성으로 조용히 떨어져, 저작한 덱이 아닌 덱이 서도 아무 신호가 없다.
        // 다른 팩 액션은 실제 드로우가 rankPools까지 보므로 여기서 묻지 않는다(오탐이 된다).
        if (_action == EOutgameTutorialAction.DeckAutoEquip && PackSpec.ResolveDrops(_def.PackId, ERankGrade.Bronze).Count == 0)
            Add(_issues, ETutorialIssueLevel.Warning, _def, _chapter, _index, "편성 풀 비었음",
                $"팩 '{_def.PackId}'의 기본 풀이 비어 있습니다 — 자동 편성이 지정 없는 것으로 보고 일반 편성 규칙으로 조용히 떨어집니다.",
                "그 팩의 pool을 채우거나, 풀이 있는 팩으로 바꾸세요.");
    }

    // 타깃과 함께 딤 위로 올릴 영역의 저작 점검. 둘 다 안내를 멈추지 않는 실수라 런타임 로그로는 드러나지 않는다.
    static void ValidateSpotlight(TutorialStepDef _def, int _chapter, int _index, List<TutorialIssue> _issues)
    {
        var t_spotlight = _def.Spotlight;
        if (t_spotlight == EOutgameTutorialAnchor.None) return;

        if (t_spotlight == _def.Anchor)
        {
            Add(_issues, ETutorialIssueLevel.Info, _def, _chapter, _index, "강조 영역 중복",
                $"함께 밝힐 영역이 앵커({t_spotlight})와 같습니다 — 타깃은 이미 딤 위로 올라가므로 아무 차이가 없습니다.",
                "다른 영역을 고르거나 비우세요.");
            return;
        }

        if (!TutorialAnchorMeta.Of(t_spotlight).IsRegistered)
            Add(_issues, ETutorialIssueLevel.Warning, _def, _chapter, _index, "강조 영역 미등록",
                $"함께 밝힐 영역 {t_spotlight}를 등록하는 위젯이 프로젝트 어디에도 없습니다 — 강조 없이 그대로 흘러 저작이 조용히 무효가 됩니다.",
                "그 위젯에 TutorialAnchor를 붙여 키를 배선하거나, 등록된 영역으로 바꾸세요.");
    }

    // (16) 런타임이 무시하는 값이라 무해하지만, 읽는 사람은 그 값이 동작에 관여한다고 믿는다.
    //      필드마다 한 줄씩 쏟으면 목록이 이 규칙으로 뒤덮이므로 스텝당 한 줄로 묶는다.
    static void ValidateLeftovers(TutorialStepDef _def, int _chapter, int _index, List<TutorialIssue> _issues)
    {
        var t_meta  = TutorialActionMeta.Of(_def.Action);
        var t_stale = new List<string>();

        for (int t_i = 0; t_i < s_probes.Length; t_i++)
        {
            if (t_meta.Uses(s_probes[t_i].Field)) continue;
            if (!IsAuthored(s_probes[t_i].Info.GetValue(_def))) continue;

            t_stale.Add(s_probes[t_i].Label);
        }

        // 앵커 카드는 앵커가 정하는 축이라 액션 테이블 밖에서 따로 본다
        if (s_anchorCard != null && !TutorialStepDef.UsesAnchorCard(_def.Anchor)
         && IsAuthored(s_anchorCard.GetValue(_def)))
            t_stale.Add("앵커 카드");

        if (t_stale.Count == 0) return;

        Add(_issues, ETutorialIssueLevel.Info, _def, _chapter, _index, "쓰지 않는 값",
            $"{_def.Action}가 읽지 않는 값이 남아 있습니다: {string.Join(", ", t_stale)}.",
            "런타임은 무시합니다 — 지워도 동작은 같습니다(읽는 사람의 오해만 사라집니다).");
    }

    // ── 잡동사니 ────────────────────────────────────────────────────────────

    static void Add(List<TutorialIssue> _issues, ETutorialIssueLevel _level, TutorialStepDef _def,
                    int _chapter, int _index, string _rule, string _message, string _fix)
        => _issues.Add(new TutorialIssue(_level, _chapter, _index, _def != null ? _def.StepId : 0,
                                         _rule, _message, _fix));

    // 팩을 지급 목록의 정본으로 읽는 액션(서버가 그 팩의 카드 전량을 준다)
    static bool IsCardGrant(EOutgameTutorialAction _action)
        => _action == EOutgameTutorialAction.DeckGrant
        || _action == EOutgameTutorialAction.CardGrant
        || _action == EOutgameTutorialAction.CardSetGrant;

    // 진입만으로 소유·재화를 움직이는 액션(되풀이되면 그만큼 다시 지급된다)
    static bool IsGrantOnEnter(EOutgameTutorialAction _action)
        => _action == EOutgameTutorialAction.AutoPurchase
        || _action == EOutgameTutorialAction.CardGrant
        || _action == EOutgameTutorialAction.CardSetGrant;

    static bool Contains(IReadOnlyList<EOutgameFeature> _features, EOutgameFeature _feature)
    {
        if (_features == null) return false;

        for (int t_i = 0; t_i < _features.Count; t_i++)
            if (_features[t_i] == _feature) return true;

        return false;
    }

    static bool HasAny(IReadOnlyList<EOutgameFeature> _features)
    {
        if (_features == null) return false;

        for (int t_i = 0; t_i < _features.Count; t_i++)
            if (_features[t_i] != EOutgameFeature.None) return true;

        return false;
    }

    // "저작자가 값을 넣었는가" — 기본값(빈 문자열·false·빈 목록·0번 enum)은 넣지 않은 것으로 본다
    static bool IsAuthored(object _value)
    {
        switch (_value)
        {
            case null:                     return false;
            case string t_text:            return !string.IsNullOrEmpty(t_text);
            case bool t_flag:              return t_flag;
            case UnityEngine.Object t_obj: return t_obj != null;   // 유실 참조는 가짜 null이라 반드시 여기서 거른다
            case IList t_list:             return t_list.Count > 0;
            case Enum t_enum:              return Convert.ToInt32(t_enum) != 0;
        }

        return true;
    }

    static FieldInfo FieldOf(string _name)
    {
        var t_info = typeof(TutorialStepDef).GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (t_info == null)
            Debug.LogError($"[TutorialValidator] TutorialStepDef has no '{_name}' field — if it was renamed, that axis is silently dropped from the leftover-value check.");

        return t_info;
    }
}
