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

        var t_state = TutorialSequenceState.Build(_data);
        var t_ids   = new Dictionary<int, string>();

        for (int t_c = 0; t_c < _data.chapters.Count; t_c++)
        {
            var t_chapter = _data.chapters[t_c];

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

            bool t_lastChapter = t_c == _data.chapters.Count - 1;

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
                ValidateAnchorGate(t_def, t_c, t_s, t_state, t_issues);
                ValidateDeckGate(t_def, t_c, t_s, t_gateOpen[t_s], t_gateUnclosed[t_s], t_issues);

                // (9) 챕터 = 씬 경계. 씬을 떠나지 않는 끝은 다음 챕터를 세울 화면이 없다는 뜻이다
                //     (OutgameTutorialRunner.WarnOnMisauthoredChapters — 마지막 챕터는 졸업이라 면제)
                if (!t_lastChapter && t_s == t_chapter.StepCount - 1 && !t_def.LeavesScene)
                    Add(t_issues, ETutorialIssueLevel.Warning, t_def, t_c, t_s, "챕터 끝이 씬을 안 떠남",
                        $"챕터의 마지막 스텝({t_def.Action})이 씬을 떠나지 않습니다.",
                        "챕터를 전투 스텝으로 끝내거나, 다음 챕터와 합치세요.");

                ValidateStep(t_def, t_c, t_s, true, t_issues);
            }
        }

        return t_issues;
    }

    /// <summary>트리거 시퀀스 점검. 좌표·해금에 기대는 규칙(스텝 ID·앵커 게이트·덱 게이트·챕터 경계)은 빼고 본다 —
    /// 트리거는 진행이 메모리에만 남고 OutgameFeatureLock이 열거하지도 않는다.</summary>
    public static List<TutorialIssue> ValidateTriggered(TriggeredTutorialData _data)
    {
        var t_issues = new List<TutorialIssue>();
        if (_data == null || _data.entries == null) return t_issues;

        for (int t_e = 0; t_e < _data.entries.Count; t_e++)
        {
            var t_entry = _data.entries[t_e];

            // (18) 발화 키가 없으면 깨울 수단이 없다 — 완주 낙인의 식별도 이 값이 하므로 대체 경로도 없다
            if (t_entry != null && t_entry.Trigger == EOutgameTutorialTrigger.None)
                t_issues.Add(new TutorialIssue(ETutorialIssueLevel.Error, t_e, 0, 0, "발화 키 없음",
                                               "trigger가 None이라 이 묶음은 영영 발화하지 않습니다 — 저작 전체가 죽은 값입니다.",
                                               "trigger에 발화 키를 고르세요."));

            // (8) 스텝이 없는 엔트리는 발화해도 아무 일이 없다(진행을 막지는 않는다 — 그 자리에서 완주로 닫힌다)
            if (t_entry == null || t_entry.StepCount == 0)
            {
                t_issues.Add(new TutorialIssue(ETutorialIssueLevel.Warning, t_e, 0, 0, "빈 트리거",
                                               "이 트리거 묶음에 스텝이 하나도 없습니다 — 발화해도 아무 일도 일어나지 않습니다.",
                                               "스텝을 저작하거나 엔트리 행을 지우세요."));
                continue;
            }

            for (int t_s = 0; t_s < t_entry.StepCount; t_s++)
            {
                // (17) 목록 중간의 빈 행 — TriggeredTutorialRunner.EnterCurrentStep이 남은 스텝을 버리고
                //      그 자리에서 "완주"로 닫는다. 완주 낙인은 계정당 1회라 이 트리거는 다시 뜨지 않는다.
                if (!t_entry.TryGetStep(t_s, out var t_def))
                {
                    t_issues.Add(new TutorialIssue(ETutorialIssueLevel.Error, t_e, t_s, 0, "빈 스텝 행",
                                                   "이 칸에 스텝이 없습니다 — 여기 닿으면 남은 스텝을 버리고 완주로 닫힙니다(낙인이 찍혀 다시 뜨지 않습니다).",
                                                   "행을 지우거나 액션을 저작하세요."));
                    continue;
                }

                // (14) 트리거 스텝의 해금/잠금은 완전히 무시된다 — OutgameFeatureLock은 온보딩 러너의 좌표만 열거한다
                if (t_def.UnlocksAll || HasAny(t_def.Unlocks) || HasAny(t_def.Locks))
                    Add(t_issues, ETutorialIssueLevel.Warning, t_def, t_e, t_s, "무시되는 해금",
                        "트리거 스텝에 해금/잠금이 저작돼 있습니다 — 해금은 온보딩 좌표에서만 파생되어 이 값은 읽히지 않습니다.",
                        "지우세요. 정말 필요한 잠금이면 온보딩 시퀀스로 옮겨야 합니다.");

                // (15) 트리거 진행은 세이브에 남지 않는다(앱을 끄면 처음부터) — 스텝 ID가 붙잡을 대상이 없다
                if (t_def.StepId > 0)
                    Add(t_issues, ETutorialIssueLevel.Warning, t_def, t_e, t_s, "쓸모없는 스텝 ID",
                        $"트리거 스텝에 ID #{t_def.StepId}가 붙어 있습니다 — 트리거는 stepId 개념이 없어 아무 데서도 읽지 않습니다.",
                        "온보딩에서 복제해 온 행일 가능성이 큽니다. 나머지 저작도 함께 확인하세요.");

                ValidateStep(t_def, t_e, t_s, false, t_issues);
            }
        }

        return t_issues;
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
    //     그 구간에서 재부팅하면 좌표가 진입 스텝으로 되감기고 사이 스텝이 다시 재생된다.
    static void ValidateDeckGate(TutorialStepDef _def, int _chapter, int _index,
                                 bool _inSpan, bool _unclosed, List<TutorialIssue> _issues)
    {
        if (_unclosed)
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "덱 게이트 미종료",
                "덱 게이트를 켠 BattleEntry인데 같은 챕터 안에서 BattleStart(또는 AutoBattle)로 닫히지 않습니다 — 덱 화면이 열린 채 챕터가 끝납니다.",
                "같은 챕터 안에 전투 시작 스텝을 두세요.");

        if (_inSpan && IsGrantOnEnter(_def.Action))
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "게이트 구간 지급",
                $"덱 게이트가 열려 있는 구간의 {_def.Action} — 이 구간에서 재부팅하면 좌표가 진입 스텝으로 되감겨 지급이 다시 실행됩니다.",
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

    // ── 좌표와 무관한 규칙(온보딩·트리거 공용) ──────────────────────────────

    static void ValidateStep(TutorialStepDef _def, int _chapter, int _index, bool _onboarding, List<TutorialIssue> _issues)
    {
        var t_action = _def.Action;

        // (5) Halt는 좌표를 되돌려 재시도를 노리는 정책인데, 앵커도 완료 신호도 없으면 되돌려 봐야 다시 세울 수단이 없다.
        //     되돌린 좌표가 온보딩은 세이브에 남아 다음 부팅을 노릴 수라도 있지만, 트리거는 메모리 전용이라 그 기회조차 없다.
        if (_def.OnFailure == EOutgameTutorialFailure.Halt
         && _def.Completion == EOutgameTutorialCompletion.Auto
         && _def.Anchor == EOutgameTutorialAnchor.None)
            Add(_issues, ETutorialIssueLevel.Error, _def, _chapter, _index, "재개 불가 Halt",
                $"{t_action}가 Halt인데 앵커도 완료 신호도 없습니다 — " + (_onboarding
                    ? "되돌려도 이 초기화에서 다시 세울 수단이 없어 그 자리에서 안내가 끝납니다(재시도는 다음 부팅뿐입니다)."
                    : "트리거 좌표는 메모리 전용이라 되돌린 자리에서 이 안내가 그대로 끝납니다."),
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
        //      (트리거 스텝에서는 (14)가 이미 더 넓게 잡으므로 중복해서 쏟지 않는다)
        if (_onboarding && (Contains(_def.Unlocks, EOutgameFeature.CollectionHarvest)
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
            Debug.LogError($"[TutorialValidator] TutorialStepDef에 '{_name}' 필드가 없습니다 — 이름이 바뀌었다면 남은 값 점검에서 그 축만 조용히 빠집니다.");

        return t_info;
    }
}
