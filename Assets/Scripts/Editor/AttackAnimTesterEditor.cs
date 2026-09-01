using UnityEditor;
using UnityEngine;

/// <summary>연출 테스트 씬의 조작 패널. 플레이 중 인스펙터에서 골라 재생한다.
///
/// <para>연출 종류는 <see cref="SynergyPreviewKind"/> enum <b>하나만</b> 읽는다 — 연출이 늘어도 이 파일은
/// 고치지 않는다(예전처럼 연출마다 버튼을 박아 두면, 새 연출을 만든 사람이 여기까지 손대야 한다).
/// 값 칸도 고른 연출이 쓰는 것만 뜬다(<see cref="AttackAnimTester.FieldsFor"/>) — 안 쓰는 값이 같이
/// 보이면 지금 화면에 무엇이 영향을 주는지 알 수 없다.</para>
///
/// <para>재생 진입점은 전부 <see cref="AttackAnimTester"/>의 public 메서드다. 에디터 코드에 연출 로직이
/// 들어가면 빌드에서 사라져 "에디터에서만 되는 연출"이 생긴다.</para></summary>
[CustomEditor(typeof(AttackAnimTester))]
public class AttackAnimTesterEditor : Editor
{
    // 상단 조작 패널이 직접 그리는 값들. 아래 "그 밖의 설정"에서 중복으로 그리지 않으려고 제외 목록으로 쓴다.
    static readonly string[] k_handled =
    {
        "m_Script", "synergyIndex", "synergyPreview", "keywordIndex", "keywordPreview",
        "emblemSlot", "emblemTiming", "emblemAutoReplay", "emblemReplayGap",
        "flowStack", "brandDamagePerShot", "caretakerHeal", "legacyCrownCount",
        "sequence", "stepGap", "untimedStepHold",
    };

    public override void OnInspectorGUI()
    {
        var t_tester = (AttackAnimTester)target;
        serializedObject.Update();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이를 눌러야 연출을 재생할 수 있다.\n"
                                  + "카드·옵션은 지금 미리 채워 두면 된다.", MessageType.Info);
            DrawSettings(_drawAll: true);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        EditorGUILayout.LabelField(t_tester.StatusLine, EditorStyles.wordWrappedMiniLabel);

        using (new EditorGUI.DisabledScope(t_tester.Busy))
        {
            DrawSequenceSection(t_tester);
            DrawAttackSection(t_tester);
            DrawKeywordSection(t_tester);
            DrawSynergySection(t_tester);
            DrawVfxSection(t_tester);

            Section("카드");
            if (Button("배치 다시 그리기")) t_tester.RefreshField();
        }

        if (t_tester.Busy)
            EditorGUILayout.HelpBox("연출 재생 중 — 끝나면 다시 열린다.", MessageType.None);

        EditorGUILayout.Space(8);
        DrawSettings(_drawAll: false);

        serializedObject.ApplyModifiedProperties();

        // 상태가 스스로 변하는 동안에만 다시 그린다. 매 프레임 Repaint를 걸면 열려 있는 드롭다운이
        // 계속 닫혀 선택 자체가 안 된다.
        if (t_tester.Busy || t_tester.EmblemAutoReplayOn) Repaint();
    }

    /// <summary>인게임 한 번의 공격이 이어지는 모습 그대로 재생. 마디 목록은 인스펙터에서 조립한다.</summary>
    void DrawSequenceSection(AttackAnimTester _tester)
    {
        Section("연결 재생 (실제 공격 흐름)");
        EditorGUILayout.PropertyField(serializedObject.FindProperty("sequence"), new GUIContent("마디"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("stepGap"), new GUIContent("마디 간격"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("untimedStepHold"), new GUIContent("고정 대기"));

        serializedObject.ApplyModifiedProperties();   // 아래 버튼이 방금 고친 순서를 쓰도록
        if (Button("이어서 재생")) _tester.PlaySequence();
    }

    void DrawAttackSection(AttackAnimTester _tester)
    {
        Section("공격 연출");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (Button("아군 공격")) _tester.PlayPlayerAttack();
            if (Button("적 공격"))   _tester.PlayEnemyAttack();
            if (Button("처형만"))    _tester.PlayExecutionOnly();
            if (Button("교활 퇴장")) _tester.PlayCunningExit();
        }
        EditorGUILayout.LabelField("카드를 탭·드래그해도 공격이 나간다(인게임과 같은 입력).",
                                   EditorStyles.miniLabel);
    }

    void DrawKeywordSection(AttackAnimTester _tester)
    {
        Section("키워드 연출");

        CardKeyword[] t_keywords = _tester.PreviewableKeywords();
        SerializedProperty t_index = serializedObject.FindProperty("keywordIndex");
        var t_keywordLabels = new string[t_keywords.Length];
        for (int i = 0; i < t_keywords.Length; i++) t_keywordLabels[i] = t_keywords[i].ToString();

        using (var t_check = new EditorGUI.ChangeCheckScope())
        {
            int t_current = Mathf.Clamp(t_index.intValue, 0, t_keywords.Length - 1);
            int t_picked = EditorGUILayout.Popup("키워드", t_current, t_keywordLabels);
            if (t_check.changed)
            {
                t_index.intValue = t_picked;
                serializedObject.ApplyModifiedProperties();
                _tester.ClampKeywordPreviewToAvailable();
                serializedObject.Update();
            }
        }

        KeywordPreviewKind[] t_available = _tester.AvailableKeywordPreviews(_tester.SelectedKeyword);
        SerializedProperty t_kind = serializedObject.FindProperty("keywordPreview");
        var t_labels = new string[t_available.Length];
        int t_kindIndex = 0;
        for (int i = 0; i < t_available.Length; i++)
        {
            t_labels[i] = t_available[i].ToString();
            if ((int)t_available[i] == t_kind.enumValueIndex) t_kindIndex = i;
        }

        using (var t_check = new EditorGUI.ChangeCheckScope())
        {
            int t_picked = EditorGUILayout.Popup("연출", t_kindIndex, t_labels);
            if (t_check.changed) t_kind.enumValueIndex = (int)t_available[t_picked];
        }

        serializedObject.ApplyModifiedProperties();
        if (Button("키워드 재생")) _tester.PlaySelectedKeyword();
    }

    void DrawSynergySection(AttackAnimTester _tester)
    {
        Section("시너지 연출");

        // 시너지: 이름 목록 드롭다운. 연출 SO가 없는 것은 이름 옆에 표시된다.
        // 값은 반드시 SerializedProperty로 쓴다 — 런타임 필드를 직접 바꾸면 이 프레임 끝의
        // ApplyModifiedProperties가 옛 값으로 덮어써 선택이 되돌아간다.
        SerializedProperty t_index = serializedObject.FindProperty("synergyIndex");
        string[] t_names = _tester.SynergyNames();
        if (t_names.Length > 0)
        {
            using (var t_check = new EditorGUI.ChangeCheckScope())
            {
                int t_current = Mathf.Clamp(t_index.intValue, 0, t_names.Length - 1);
                int t_picked  = EditorGUILayout.Popup("시너지", t_current, t_names);
                if (t_check.changed)
                {
                    t_index.intValue = t_picked;
                    serializedObject.ApplyModifiedProperties();
                    _tester.OnSynergySelectionChanged();
                    _tester.ClampPreviewToAvailable();   // 이전 시너지의 연출이 선택으로 남지 않게
                    serializedObject.Update();           // 위에서 런타임 필드가 바뀌었으니 캐시를 새로 읽는다
                }
            }
        }
        else EditorGUILayout.LabelField("시너지", "목록이 비어 있다(플레이 시 자동으로 채워진다)");

        // 연출: 그 시너지가 실제로 가진 것만 후보로 올린다.
        SynergyPreviewKind[] t_available = _tester.AvailablePreviews();
        if (t_available.Length == 0)
        {
            EditorGUILayout.HelpBox("이 시너지엔 등록된 연출이 없다(연출 에셋 미배선).", MessageType.Warning);
            return;
        }

        SerializedProperty t_kind = serializedObject.FindProperty("synergyPreview");
        var t_labels = new string[t_available.Length];
        int t_kindIndex = -1;
        for (int i = 0; i < t_available.Length; i++)
        {
            t_labels[i] = t_available[i].ToString();
            if ((int)t_available[i] == t_kind.enumValueIndex) t_kindIndex = i;
        }

        // 저장된 선택이 이 시너지에 없는 연출이면 **그 자리에서 첫 항목으로 굳힌다.**
        // 표시만 첫 항목으로 맞추고 값을 두면 재생 버튼이 옛 값으로 분기해 "눌러도 아무 일이 없다"가 된다
        // (ClampPreviewToAvailable은 시너지를 바꿀 때만 도므로 씬을 열자마자인 경우를 못 잡는다).
        if (t_kindIndex < 0)
        {
            t_kindIndex = 0;
            t_kind.enumValueIndex = (int)t_available[0];
            serializedObject.ApplyModifiedProperties();
        }

        using (var t_kindCheck = new EditorGUI.ChangeCheckScope())
        {
            int t_pickedKind = EditorGUILayout.Popup("연출", t_kindIndex, t_labels);
            if (t_kindCheck.changed) t_kind.enumValueIndex = (int)t_available[t_pickedKind];
        }

        // 고른 연출이 쓰는 값만.
        var t_selected = (SynergyPreviewKind)t_kind.enumValueIndex;
        foreach (string t_field in AttackAnimTester.FieldsFor(t_selected))
        {
            SerializedProperty t_p = serializedObject.FindProperty(t_field);
            if (t_p != null) EditorGUILayout.PropertyField(t_p);
        }

        serializedObject.ApplyModifiedProperties();   // 아래 재생 버튼이 방금 고친 값을 쓰도록
        if (Button("재생")) _tester.PlaySelectedSynergy();
    }

    void DrawVfxSection(AttackAnimTester _tester)
    {
        Section("VFX 후보");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("피격", GUILayout.Width(40));
            if (Button("◀")) _tester.CycleHitVfx(-1);
            if (Button("▶")) _tester.CycleHitVfx(+1);
            if (Button("한 번 띄우기")) _tester.SpawnHitVfxPreview();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (Button("폴더 재스캔"))   _tester.RescanVfx();
            if (Button("선택 경로 로그")) _tester.LogVfxPaths();
        }
    }

    /// <summary>배선·옵션 칸. 플레이 중에는 위 패널이 이미 그린 값을 빼고 접어 둔다.</summary>
    void DrawSettings(bool _drawAll)
    {
        if (_drawAll)
        {
            DrawPropertiesExcluding(serializedObject, "m_Script");
            return;
        }

        this.showSettings = EditorGUILayout.Foldout(this.showSettings, "그 밖의 설정", true);
        if (this.showSettings) DrawPropertiesExcluding(serializedObject, k_handled);
    }

    bool showSettings;

    static void Section(string _title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(_title, EditorStyles.boldLabel);
    }

    static bool Button(string _label) => GUILayout.Button(_label, GUILayout.Height(22));
}
