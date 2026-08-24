using System.Collections.Generic;
using UnityEngine;

// 아웃게임 첫시작 튜토리얼의 챕터 시퀀스(에디터 저작, SO)
[CreateAssetMenu(fileName = "OutgameTutorial", menuName = "Card Battle/Outgame Tutorial")]
public class OutgameTutorialData : ScriptableObject
{
    [Header("챕터 시퀀스 (순서 = 진행 순서, 세이브가 붙잡는 것은 스텝의 stepId)")]
    [Tooltip("챕터 하나 = 기획의 '튜토리얼 N편'.\n"
           + "행을 복제하면 stepId까지 복제되므로, 복제한 뒤에는 우클릭 메뉴 [스텝 ID 부여]를 다시 돌려라 "
           + "— 겹친 번호를 걷어 새로 매긴다. 그러지 않으면 두 행이 같은 스텝으로 보인다")]
    public List<OutgameTutorialChapter> chapters = new List<OutgameTutorialChapter>();

    // 다음에 내줄 번호. 단조 증가만 하고 지운 번호를 재사용하지 않는다 —
    // 재사용하면 삭제된 스텝에 서 있던 세이브가 "삭제 경고" 없이 무관한 새 스텝으로 조용히 옮겨간다.
    // 이 줄을 지우거나 머지에서 떨어뜨리지 마라: 값을 잃으면 남은 최댓값에서 다시 세는데,
    // 하필 가장 큰 번호의 스텝을 지운 뒤였다면 그 번호가 재발급된다(위의 조용한 이동이 그때 난다).
    [SerializeField, HideInInspector] int nextStepId = 1;

#if UNITY_EDITOR
    /// <summary>저작 도구 전용 — 런타임은 읽기만 한다. 다음 번호를 한 개 떼어 준다(떼면 카운터가 올라간다).
    /// 새 스텝·복제본에 번호를 내주는 <b>유일한 창구</b>다. 여기를 거치지 않고 손으로 번호를 매기면
    /// 지운 번호를 재발급하게 되고, 삭제된 스텝에 서 있던 세이브가 경고 없이 무관한 스텝으로 옮겨간다.</summary>
    public int TakeNextStepIdForEditor() => nextStepId++;

    /// <summary>빈 stepId를 채우고 겹친 번호를 걷는다. 스텝을 추가하거나 행을 복제한 뒤에 돌린다.
    /// 겹쳤을 때는 먼저 나온 칸이 번호를 지키고 뒤쪽이 새로 받는다 — 그 외에는 아무 번호도 건드리지 않아
    /// 몇 번을 돌려도 결과가 같다.</summary>
    [ContextMenu("스텝 ID 부여")]
    public void AssignMissingStepIds()
    {
        var t_taken    = new HashSet<int>();
        var t_needs    = new List<(TutorialStepDef Step, string Coord, bool Duplicate)>();
        var t_assigned = new List<string>();
        var t_freed    = new List<string>();
        int t_max      = 0;

        // 1패스 — 살아 있는 번호를 먼저 전부 모은다. 한 번에 훑으면서 나눠 주면 아직 안 본 뒤쪽 번호를
        // 새 칸에 내주게 되고, 그러면 그 뒤가 전부 한 칸씩 밀린다(이 도구가 막으려던 바로 그 사고다).
        for (int t_c = 0; t_c < chapters.Count; t_c++)
        {
            var t_chapter = chapters[t_c];
            if (t_chapter == null) continue;

            for (int t_s = 0; t_s < t_chapter.StepCount; t_s++)
            {
                if (!t_chapter.TryGetStep(t_s, out var t_step)) continue;

                // 먼저 나온 칸이 번호를 지킨다 — 런타임 해석(첫 일치 채택)과 같은 규칙이라
                // 부여를 안 돌린 SO도 다르게가 아니라 똑같이 degrade한다.
                if (t_step.StepId > 0 && t_taken.Add(t_step.StepId))
                {
                    if (t_step.StepId > t_max) t_max = t_step.StepId;
                    continue;
                }

                t_needs.Add((t_step, $"{t_c}-{t_s}", t_step.StepId > 0));
            }
        }

        // 옛 도구로 매겨진 에셋을 흡수한다 — 카운터가 이미 쓰인 번호 뒤로 가야 재발급이 없다.
        bool t_counterMoved = nextStepId <= t_max;
        if (t_counterMoved) nextStepId = t_max + 1;

        // 2패스 — 빈 칸과 겹친 칸에만 새 번호를 내준다
        for (int t_i = 0; t_i < t_needs.Count; t_i++)
        {
            t_needs[t_i].Step.SetStepIdForEditor(TakeNextStepIdForEditor());

            if (t_needs[t_i].Duplicate) t_freed.Add(t_needs[t_i].Coord);
            else                        t_assigned.Add(t_needs[t_i].Coord);
        }

        // 카운터만 움직인 경우에도 저장한다 — 안 하면 다음 로드에 되돌아가 지운 번호를 재발급할 수 있다.
        if (t_assigned.Count == 0 && t_freed.Count == 0 && !t_counterMoved)
        {
            Debug.Log($"[OutgameTutorialData] 모든 스텝에 ID가 있습니다({t_taken.Count}개, 다음 번호 #{nextStepId}) — 바뀐 것이 없습니다.", this);
            return;
        }

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(this);

        if (t_assigned.Count == 0 && t_freed.Count == 0)
        {
            Debug.Log($"[OutgameTutorialData] 스텝 ID는 그대로({t_taken.Count}개), 다음 번호를 #{nextStepId}로 맞췄습니다.", this);
            return;
        }

        if (t_assigned.Count > 0) Debug.Log($"[OutgameTutorialData] 빈 스텝 {t_assigned.Count}칸에 ID 부여 — {string.Join(", ", t_assigned)}", this);

        // 복제 행은 예상된 일이지만, 어느 쪽이 새 번호를 받았는지는 알려 줘야 한다
        // — 복제본을 원본보다 앞에 붙였다면 원본이 새 번호를 받고 그 스텝에 서 있던 세이브가 밀린다.
        if (t_freed.Count > 0) Debug.LogWarning($"[OutgameTutorialData] ID가 겹쳐 새로 매긴 칸 {t_freed.Count}개 — {string.Join(", ", t_freed)}. 복제한 행이라면 정상입니다.", this);
    }
#endif
}
