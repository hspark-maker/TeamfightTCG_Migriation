using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>튜토리얼 저작을 바꾸는 편집 연산 모음 — 창은 UI만 그리고 규칙은 전부 여기 산다.
///
/// 이 파일이 따로 있는 이유는 두 가지 계약 때문이다.
/// <b>stepId 계약</b> — 세이브가 붙잡는 것은 좌표가 아니라 스텝의 번호다. 그래서 이동은 번호를 유지하고(좌표가 바뀌어도
/// 진행이 따라온다), 추가·복제는 <see cref="OutgameTutorialData.TakeNextStepIdForEditor"/>로만 번호를 받고,
/// 삭제한 번호는 영구 소각한다(재발급하면 지워진 스텝에 서 있던 세이브가 무관한 스텝으로 조용히 옮겨간다).
/// <b>되감기 예약 무효화</b> — <see cref="OutgameTutorialRewind"/>의 예약은 (챕터, 스텝) <b>인덱스</b>라,
/// 구조가 바뀌면 그 좌표가 다른 스텝을 가리키게 되고 다음 부트가 세이브를 밀고 엉뚱한 지점까지 지급을 재생한다.
///
/// 모든 메서드의 반환값은 "편집이 실제로 일어났는가"다 — 호출자는 참일 때만 캐시를 무효화한다.</summary>
public static class TutorialSequenceEditOps
{
    // ───────── 온보딩 스텝 ─────────

    /// <summary>빈 스텝을 그 자리에 끼운다(새 번호를 즉시 부여한다)</summary>
    public static bool AddStep(OutgameTutorialData _data, int _chapter, int _index)
    {
        if (!TryGetSteps(_data, _chapter, out var t_steps)) return false;
        if (_index < 0 || _index > t_steps.Count)           return false;

        Undo.RegisterCompleteObjectUndo(_data, "튜토리얼 스텝 추가");

        // 번호를 0으로 두지 않는다 — 미부여 스텝은 세이브가 지목할 수 없어 [스텝 ID 부여]를 돌릴 때까지 좌표에만 기댄다.
        var t_step = new TutorialStepDef();
        t_step.SetStepIdForEditor(_data.TakeNextStepIdForEditor());
        t_steps.Insert(_index, t_step);

        CancelRewindForStructureChange($"스텝 추가({_chapter}-{_index})");
        MarkDirty(_data);
        return true;
    }

    /// <summary>스텝을 바로 뒤에 복제한다(복제본이 새 번호를 받고, 원본 번호는 그대로 둔다)</summary>
    public static bool DuplicateStep(OutgameTutorialData _data, int _chapter, int _index)
    {
        if (!TryGetSteps(_data, _chapter, out var t_steps)) return false;
        if (_index < 0 || _index >= t_steps.Count)          return false;

        Undo.RegisterCompleteObjectUndo(_data, "튜토리얼 스텝 복제");

        // 손으로 필드를 베끼지 않는다 — InsertArrayElementAtIndex가 i번을 i+1로 통째로 복제한다(참조·리스트 포함).
        // 손복제는 필드가 늘어날 때 조용히 누락된다.
        var t_so   = new SerializedObject(_data);
        var t_list = t_so.FindProperty("chapters").GetArrayElementAtIndex(_chapter).FindPropertyRelative("stepDefs");
        t_list.InsertArrayElementAtIndex(_index);
        t_so.ApplyModifiedPropertiesWithoutUndo();   // 되돌리기는 위 스냅샷 하나로 충분하다

        // 새 번호는 반드시 복제본(뒤쪽)에 준다 — 원본 번호를 갈면 그 스텝에 서 있던 세이브가 통째로 밀린다.
        // 적용 이후에 부여한다: SerializedObject를 다시 적용하면 캐시된 옛 카운터가 되살아나 발급이 무효화된다.
        if (TryGetSteps(_data, _chapter, out var t_after) && _index + 1 < t_after.Count && t_after[_index + 1] != null)
            t_after[_index + 1].SetStepIdForEditor(_data.TakeNextStepIdForEditor());

        CancelRewindForStructureChange($"스텝 복제({_chapter}-{_index})");
        MarkDirty(_data);
        return true;
    }

    /// <summary>스텝을 지운다(확인을 받는다. 그 번호는 영구 소각된다)</summary>
    public static bool DeleteStep(OutgameTutorialData _data, int _chapter, int _index)
    {
        if (!TryGetSteps(_data, _chapter, out var t_steps)) return false;
        if (_index < 0 || _index >= t_steps.Count)          return false;

        var    t_step   = t_steps[_index];
        string t_action = t_step != null ? t_step.Action.ToString() : "(빈 칸)";
        string t_id     = t_step != null && t_step.StepId > 0 ? $"#{t_step.StepId}" : "미부여";

        bool t_ok = EditorUtility.DisplayDialog(
            "스텝 삭제",
            $"{_chapter}-{_index}  {t_action}  (stepId {t_id})\n\n"
          + "이 스텝을 지웁니다.\n"
          + $"stepId {t_id}는 영구 소각되어 다시 쓰이지 않습니다 — 이 스텝에 서 있던 세이브는 다음 부트에 다음 칸으로 밀려납니다.",
            "삭제", "취소");

        if (!t_ok) return false;

        Undo.RegisterCompleteObjectUndo(_data, "튜토리얼 스텝 삭제");
        // 지웠다고 카운터를 내리지 않는다 — 번호 재사용이 곧 세이브의 조용한 이동이다.
        // (Undo는 SO 전체 스냅샷이라 스텝과 카운터가 함께 되돌아간다 — 짝이 맞으므로 그건 문제가 아니다.)
        t_steps.RemoveAt(_index);

        CancelRewindForStructureChange($"스텝 삭제({_chapter}-{_index})");
        MarkDirty(_data);
        return true;
    }

    /// <summary>같은 챕터 안에서 스텝을 위아래로 옮긴다(stepId는 그대로)</summary>
    public static bool MoveStep(OutgameTutorialData _data, int _chapter, int _index, int _delta)
    {
        if (!TryGetSteps(_data, _chapter, out var t_steps)) return false;
        if (_index < 0 || _index >= t_steps.Count)          return false;
        if (_delta == 0)                                    return false;

        // 양 끝을 넘어가면 막는다 — 창이 이 판정으로 버튼을 비활성으로 그린다
        int t_target = _index + _delta;
        if (t_target < 0 || t_target >= t_steps.Count) return false;

        Undo.RegisterCompleteObjectUndo(_data, "튜토리얼 스텝 순서 변경");

        // 번호는 건드리지 않는다 — 세이브가 번호를 따라오므로 좌표만 바뀌고 진행은 그대로 따라간다
        var t_step = t_steps[_index];
        t_steps.RemoveAt(_index);
        t_steps.Insert(t_target, t_step);

        CancelRewindForStructureChange($"스텝 이동({_chapter}-{_index} → {_chapter}-{t_target})");
        MarkDirty(_data);
        return true;
    }

    /// <summary>스텝을 다른 챕터의 <b>맨 뒤</b>로 옮긴다(stepId를 유지하므로 그 스텝에 서 있던 세이브도 따라간다)</summary>
    public static bool MoveStepToChapter(OutgameTutorialData _data, int _chapter, int _index, int _targetChapter)
    {
        if (!TryGetSteps(_data, _chapter, out var t_steps))             return false;
        if (_index < 0 || _index >= t_steps.Count)                      return false;
        if (_targetChapter == _chapter)                                 return false;
        if (!TryGetSteps(_data, _targetChapter, out var t_targetSteps)) return false;

        Undo.RegisterCompleteObjectUndo(_data, "튜토리얼 스텝 챕터 이동");

        // 같은 인스턴스를 그대로 옮긴다 — 필드도 stepId도 그대로다. 챕터 간 이동이 안전한 근거가 이것이다.
        var t_step = t_steps[_index];
        t_steps.RemoveAt(_index);
        t_targetSteps.Add(t_step);

        CancelRewindForStructureChange($"스텝 챕터 이동({_chapter}-{_index} → {_targetChapter}-{t_targetSteps.Count - 1})");
        MarkDirty(_data);
        return true;
    }

    // ───────── 온보딩 챕터 ─────────

    /// <summary>빈 챕터를 그 자리에 끼운다(더미 스텝은 넣지 않는다 — 검증기가 "빈 챕터"로 잡아 준다)</summary>
    public static bool AddChapter(OutgameTutorialData _data, int _index)
    {
        if (!TryGetChapters(_data, out var t_chapters)) return false;
        if (_index < 0 || _index > t_chapters.Count)    return false;

        Undo.RegisterCompleteObjectUndo(_data, "튜토리얼 챕터 추가");
        t_chapters.Insert(_index, new OutgameTutorialChapter());

        CancelRewindForStructureChange($"챕터 추가({_index})");
        MarkDirty(_data);
        return true;
    }

    /// <summary>챕터를 스텝째로 지운다(확인을 받는다. 딸린 번호는 전부 영구 소각된다)</summary>
    public static bool DeleteChapter(OutgameTutorialData _data, int _index)
    {
        if (!TryGetChapters(_data, out var t_chapters)) return false;
        if (_index < 0 || _index >= t_chapters.Count)   return false;

        var    t_chapter = t_chapters[_index];
        int    t_count   = t_chapter != null ? t_chapter.StepCount : 0;
        string t_label   = t_chapter != null && !string.IsNullOrEmpty(t_chapter.Label) ? t_chapter.Label : "(이름 없음)";

        string t_body = t_count > 0
            ? $"챕터 {_index}  {t_label}\n\n"
            + $"⚠ 이 챕터의 스텝 {t_count}개가 함께 사라집니다.\n"
            + "딸린 stepId는 전부 영구 소각되어 다시 쓰이지 않습니다 — 그 스텝에 서 있던 세이브는 갈 곳을 잃습니다.\n\n"
            + "정말 지울까요?"
            : $"챕터 {_index}  {t_label}\n\n스텝이 없는 챕터입니다. 지울까요?";

        if (!EditorUtility.DisplayDialog("챕터 삭제", t_body, "삭제", "취소")) return false;

        Undo.RegisterCompleteObjectUndo(_data, "튜토리얼 챕터 삭제");
        t_chapters.RemoveAt(_index);   // 지웠다고 카운터를 내리지 않는다(지운 번호 재사용 금지)

        CancelRewindForStructureChange($"챕터 삭제({_index} · 스텝 {t_count}개)");
        MarkDirty(_data);
        return true;
    }

    /// <summary>챕터 순서를 바꾼다(딸린 stepId는 전부 그대로)</summary>
    public static bool MoveChapter(OutgameTutorialData _data, int _index, int _delta)
    {
        if (!TryGetChapters(_data, out var t_chapters)) return false;
        if (_index < 0 || _index >= t_chapters.Count)   return false;
        if (_delta == 0)                                return false;

        // 시퀀스 양 끝을 넘어가면 막는다 — 창이 이 판정으로 버튼을 비활성으로 그린다
        int t_target = _index + _delta;
        if (t_target < 0 || t_target >= t_chapters.Count) return false;

        Undo.RegisterCompleteObjectUndo(_data, "튜토리얼 챕터 순서 변경");

        // 번호는 건드리지 않는다 — 챕터가 통째로 자리를 옮겨도 세이브는 stepId로 자기 스텝을 다시 찾는다
        var t_chapter = t_chapters[_index];
        t_chapters.RemoveAt(_index);
        t_chapters.Insert(t_target, t_chapter);

        CancelRewindForStructureChange($"챕터 이동({_index} → {t_target})");
        MarkDirty(_data);
        return true;
    }

    /// <summary>챕터 이름을 바꾼다(표시용일 뿐 구조 변경이 아니라 되감기 예약을 걷지 않는다)</summary>
    public static bool SetChapterLabel(OutgameTutorialData _data, int _index, string _label)
    {
        if (!TryGetChapters(_data, out var t_chapters)) return false;
        if (_index < 0 || _index >= t_chapters.Count)   return false;

        var t_chapter = t_chapters[_index];
        if (t_chapter == null) return false;

        // 값이 같으면 아무 일도 하지 않는다 — 창이 매 그리기마다 불러도 에셋이 더러워지지 않게
        string t_next = _label ?? string.Empty;
        if (string.Equals(t_chapter.EditorLabel ?? string.Empty, t_next)) return false;

        Undo.RecordObject(_data, "튜토리얼 챕터 이름 변경");
        t_chapter.EditorLabel = t_next;

        MarkDirty(_data);
        return true;
    }

    /// <summary>트리거 묶음의 표시 이름을 바꾼다(표시·로그용일 뿐이다)</summary>
    public static bool SetTriggeredLabel(TriggeredTutorialData _data, int _entry, string _label)
    {
        if (!TryGetEntry(_data, _entry, out var t_entry)) return false;

        string t_next = _label ?? string.Empty;
        if (string.Equals(t_entry.EditorLabel ?? string.Empty, t_next)) return false;

        Undo.RecordObject(_data, "트리거 묶음 이름 변경");
        t_entry.EditorLabel = t_next;

        MarkDirty(_data);
        return true;
    }

    /// <summary>트리거 묶음의 발화 키를 바꾼다.
    /// 이 값은 완주 낙인의 식별자이기도 하다 — 바꾸면 이미 완주한 계정의 낙인과 갈려 그 묶음이 다시 뜬다.</summary>
    public static bool SetTriggeredKey(TriggeredTutorialData _data, int _entry, EOutgameTutorialTrigger _trigger)
    {
        if (!TryGetEntry(_data, _entry, out var t_entry)) return false;
        if (t_entry.EditorTrigger == _trigger)            return false;

        Undo.RecordObject(_data, "트리거 발화 키 변경");
        t_entry.EditorTrigger = _trigger;

        MarkDirty(_data);
        return true;
    }

    static bool TryGetEntry(TriggeredTutorialData _data, int _entry, out TriggeredTutorialEntry _result)
    {
        _result = null;
        if (_data == null || _data.entries == null)      return false;
        if (_entry < 0 || _entry >= _data.entries.Count) return false;

        _result = _data.entries[_entry];
        return _result != null;
    }

    // ───────── 트리거 스텝 ─────────
    // 트리거는 진행 좌표가 메모리에만 남아(앱을 끄면 처음부터) 번호가 의미를 갖지 않는다.
    // 그래서 아래 넷은 stepId를 부여하지도 지우지도 않는다 — 검증기가 "stepId가 부여돼 있음"을 경고로 잡는다.
    // 되감기 예약도 온보딩 좌표라 여기서는 걷지 않는다(트리거 저작은 그 좌표를 밀지 못한다).

    /// <summary>빈 트리거 스텝을 그 자리에 끼운다</summary>
    public static bool AddTriggeredStep(TriggeredTutorialData _data, int _entry, int _index)
    {
        if (!TryGetEntrySteps(_data, _entry, out var t_steps)) return false;
        if (_index < 0 || _index > t_steps.Count)              return false;

        Undo.RegisterCompleteObjectUndo(_data, "트리거 스텝 추가");
        t_steps.Insert(_index, new TutorialStepDef());

        MarkDirty(_data);
        return true;
    }

    /// <summary>트리거 스텝을 바로 뒤에 복제한다</summary>
    public static bool DuplicateTriggeredStep(TriggeredTutorialData _data, int _entry, int _index)
    {
        if (!TryGetEntrySteps(_data, _entry, out var t_steps)) return false;
        if (_index < 0 || _index >= t_steps.Count)             return false;

        Undo.RegisterCompleteObjectUndo(_data, "트리거 스텝 복제");

        // 온보딩과 같은 이유로 손복제를 하지 않는다(필드가 늘어날 때 조용히 누락된다)
        var t_so   = new SerializedObject(_data);
        var t_list = t_so.FindProperty("entries").GetArrayElementAtIndex(_entry).FindPropertyRelative("stepDefs");
        t_list.InsertArrayElementAtIndex(_index);
        t_so.ApplyModifiedPropertiesWithoutUndo();

        MarkDirty(_data);
        return true;
    }

    /// <summary>트리거 스텝을 지운다(확인을 받는다)</summary>
    public static bool DeleteTriggeredStep(TriggeredTutorialData _data, int _entry, int _index)
    {
        if (!TryGetEntrySteps(_data, _entry, out var t_steps)) return false;
        if (_index < 0 || _index >= t_steps.Count)             return false;

        var    t_step   = t_steps[_index];
        string t_action = t_step != null ? t_step.Action.ToString() : "(빈 칸)";

        if (!EditorUtility.DisplayDialog(
                "트리거 스텝 삭제",
                $"{_entry}-{_index}  {t_action}\n\n이 스텝을 지웁니다.",
                "삭제", "취소"))
            return false;

        Undo.RegisterCompleteObjectUndo(_data, "트리거 스텝 삭제");
        t_steps.RemoveAt(_index);

        MarkDirty(_data);
        return true;
    }

    /// <summary>같은 묶음 안에서 트리거 스텝을 위아래로 옮긴다</summary>
    public static bool MoveTriggeredStep(TriggeredTutorialData _data, int _entry, int _index, int _delta)
    {
        if (!TryGetEntrySteps(_data, _entry, out var t_steps)) return false;
        if (_index < 0 || _index >= t_steps.Count)             return false;
        if (_delta == 0)                                       return false;

        int t_target = _index + _delta;
        if (t_target < 0 || t_target >= t_steps.Count) return false;

        Undo.RegisterCompleteObjectUndo(_data, "트리거 스텝 순서 변경");

        var t_step = t_steps[_index];
        t_steps.RemoveAt(_index);
        t_steps.Insert(t_target, t_step);

        MarkDirty(_data);
        return true;
    }

    // ───────── 내부 ─────────

    static bool TryGetChapters(OutgameTutorialData _data, out List<OutgameTutorialChapter> _chapters)
    {
        _chapters = _data != null ? _data.chapters : null;
        return _chapters != null;
    }

    static bool TryGetSteps(OutgameTutorialData _data, int _chapter, out List<TutorialStepDef> _steps)
    {
        _steps = null;
        if (!TryGetChapters(_data, out var t_chapters))   return false;
        if (_chapter < 0 || _chapter >= t_chapters.Count) return false;

        var t_chapter = t_chapters[_chapter];
        if (t_chapter == null) return false;

        _steps = t_chapter.EditorSteps;
        return _steps != null;
    }

    static bool TryGetEntrySteps(TriggeredTutorialData _data, int _entry, out List<TutorialStepDef> _steps)
    {
        _steps = null;
        if (_data == null || _data.entries == null)      return false;
        if (_entry < 0 || _entry >= _data.entries.Count) return false;

        var t_entry = _data.entries[_entry];
        if (t_entry == null) return false;

        _steps = t_entry.EditorSteps;
        return _steps != null;
    }

    /// <summary>구조를 바꿨으니 되감기 예약을 걷는다.
    /// 예약은 (챕터, 스텝) 인덱스라 저작이 밀리면 다른 스텝을 가리키게 되고,
    /// 다음 부트가 세이브를 민 뒤 엉뚱한 지점까지 지급을 재생한다 — 되돌릴 수 없는 사고다.</summary>
    static void CancelRewindForStructureChange(string _reason)
    {
        if (!OutgameTutorialRewind.TryGetScheduled(out int t_chapter, out int t_step, out bool t_wipePending)) return;

        OutgameTutorialRewind.Cancel();

        string t_stage = t_wipePending ? "세이브 밀기 대기" : "지급 재생 대기";
        Debug.LogWarning($"[TutorialEditOps] 저작이 바뀌어 되감기 예약을 취소했습니다 — 좌표 {t_chapter}-{t_step}({t_stage}) · 사유: {_reason}. 필요하면 다시 예약하세요.");
    }

    static void MarkDirty(UnityEngine.Object _data)
    {
        EditorUtility.SetDirty(_data);
        AssetDatabase.SaveAssetIfDirty(_data);
    }
}
