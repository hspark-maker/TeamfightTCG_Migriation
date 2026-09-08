using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 감정표현 한 칸. sprite는 선택 표의 대표 그림이자 정지 폴백이고, clip이 있으면 스티커가
/// 떠 있는 동안 그 클립을 반복한다. clip은 루트 Image의 m_Sprite를 애니메이션하는 파일이어야 한다.
/// </summary>
[Serializable]
public class EmoteEntry
{
    [Tooltip("세이브에 그대로 들어가는 영구 키. 한 번 정하면 바꾸지 마라 — 그 감정표현을 장착한 유저가 " +
             "기본 로드아웃으로 돌아간다. 아트를 갈고 싶으면 id는 두고 아래 슬롯만 교체하면 된다. " +
             "0은 '미저작'이라 비워 두면 OnValidate가 안 쓰인 최소 양수를 자동으로 채운다.")]
    [Min(0)] public int id;

    [Tooltip("선택 표의 아이콘이자 AnimationClip이 없거나 유효하지 않을 때 표시할 정지 그림. " +
             "이것도 clip도 없으면 그 칸은 아무것도 띄우지 않는다(문자 폴백은 없앴다).")]
    public Sprite sprite;

    [Tooltip("스티커 표시 중 반복 재생할 선택 AnimationClip. 비어 있으면 sprite를 정지 상태로 표시한다. " +
             "클립은 루트 Image.m_Sprite를 애니메이션해야 한다.")]
    public AnimationClip clip;
}

/// <summary>
/// 감정표현 <b>풀 전체</b>의 단일 진실원. 선택 표·스티커·프로필 편집이 모두 여기만 본다.
///
/// 풀 크기와 장착 칸 수는 다른 값이다 — <see cref="SLOT_COUNT"/>는 전투 선택 표에 올라가는 칸 수고,
/// <c>entries</c>는 그보다 훨씬 길 수 있다. 무엇을 장착했는지는 세이브(ProfileSaveData.EmoteIds)가 쥔다.
///
/// 조회는 <b>id</b>로 한다(<see cref="TryGet"/>). <see cref="PoolAt"/>의 index는 저작 순서일 뿐이라
/// 세이브·와이어에 실으면 안 된다 — 에셋 줄 순서를 한 번 바꾸는 순간 전 유저 장착이 어긋난다.
/// </summary>
[CreateAssetMenu(fileName = "EmoteCatalog", menuName = "Card Battle/Emote Catalog")]
public class EmoteCatalog : ScriptableObject
{
    // 전투 선택 표의 칸 수(2×3). 풀 크기와는 무관하다.
    public const int SLOT_COUNT = 6;

    [Tooltip("감정표현 풀 전량. 여기 길이는 장착 칸 수(6)와 상관없다 — 얼마든지 늘려도 된다. " +
             "줄 순서는 프로필 편집 화면의 나열 순서일 뿐이고, 장착은 각 줄의 id로 저장된다.")]
    [SerializeField] EmoteEntry[] entries = Array.Empty<EmoteEntry>();

    [Header("표시")]
    [Tooltip("스티커가 화면에 머무는 전체 시간(초).")]
    [Min(0.1f)] public float showDuration = 2f;

    [Header("스티커 펼침")]
    [Tooltip("왼쪽 아래에 접힌 그림이 완전히 펼쳐지는 시간(초).")]
    [Min(0f)] public float peelInDuration = 0.28f;

    [Tooltip("오른쪽 위부터 왼쪽 아래로 스티커가 떼어지는 시간(초).")]
    [Min(0f)] public float peelOutDuration = 0.22f;

    [Tooltip("등장 첫 프레임의 접힘 정도. 0은 평면, 1은 완전히 떼어진 상태.")]
    [Range(0f, 1f)] public float peelStartAmount = 0.5f;

    [Tooltip("말린 부분의 반지름. 스티커 대각선 길이에 대한 비율.")]
    [Range(0.03f, 0.5f)] public float peelCurlRadius = 0.16f;

    [Tooltip("대각선 메시 분할 수. 높을수록 부드럽지만 정점 수가 늘어난다.")]
    [Range(6, 48)] public int peelSegments = 20;

    [Header("AI 반응 (싱글 전용)")]
    [Tooltip("내가 감정표현을 내면 AI가 하나를 답한다.")]
    public bool aiReply = true;

    [Tooltip("내 감정표현 뒤 AI가 답하기까지의 시간(초).")]
    [Min(0f)] public float aiReplyDelay = 1.5f;

    // 풀 전체 길이. 장착 칸 수(SLOT_COUNT)로 자르지 않는다 — 자르면 뒤쪽 저작이 통째로 사라진다.
    public int Count => this.entries != null ? this.entries.Length : 0;

    public IReadOnlyList<EmoteEntry> Pool => this.entries ?? Array.Empty<EmoteEntry>();

    /// <summary>저작 순서 index로 훑는다. 목록을 그리는 자리에서만 써라 —
    /// 이 index를 세이브·와이어에 실으면 에셋 줄 순서가 곧 계약이 된다.</summary>
    public EmoteEntry PoolAt(int _index)
    {
        if (this.entries == null || _index < 0 || _index >= this.entries.Length) return null;
        return this.entries[_index];
    }

    /// <summary>id로 조회하는 유일한 자리. id 0은 '미저작'이라 항상 실패한다(빈 슬롯의 표현).</summary>
    public bool TryGet(int _id, out EmoteEntry _entry)
    {
        _entry = null;
        if (_id <= 0 || this.entries == null) return false;

        for (int t_i = 0; t_i < this.entries.Length; t_i++)
        {
            EmoteEntry t_entry = this.entries[t_i];
            if (t_entry == null || t_entry.id != _id) continue;

            _entry = t_entry;
            return true;
        }
        return false;
    }

    /// <summary>세이브가 비었거나 못 알아볼 때 떨어질 자리 — 풀 앞쪽에서 유효한 id를 SLOT_COUNT개까지 뽑는다.
    /// 풀이 그보다 짧으면 짧은 채로 돌려주고, 모자란 칸은 호출부가 0(빈 칸)으로 채운다.</summary>
    public List<int> DefaultLoadout()
    {
        var t_result = new List<int>(SLOT_COUNT);
        for (int t_i = 0; t_i < Count && t_result.Count < SLOT_COUNT; t_i++)
        {
            EmoteEntry t_entry = this.entries[t_i];
            if (t_entry != null && t_entry.id > 0) t_result.Add(t_entry.id);
        }
        return t_result;
    }

#if UNITY_EDITOR
    // id 미저작(0) 칸에 안 쓰인 최소 양수를 채우고 중복을 경고한다. 숫자 id는 문자열과 달리
    // 복붙 중복이 눈에 띄지 않아 저작 단계에서 잡지 않으면 두 칸이 서로를 가린다.
    //
    // 자동 배정은 에셋을 고쳐 쓰므로 SetDirty가 없으면 디스크에 남지 않는다 — 그러면 다음 실행에서
    // 다시 0으로 돌아오고, 빌드에서는 OnValidate 자체가 없어 전 칸이 조회 실패한다.
    void OnValidate()
    {
        if (this.entries == null) return;

        int t_nextId = 1;
        for (int t_i = 0; t_i < this.entries.Length; t_i++)
        {
            EmoteEntry t_entry = this.entries[t_i];
            if (t_entry != null && t_entry.id >= t_nextId) t_nextId = t_entry.id + 1;
        }

        bool t_assigned = false;
        var t_ids = new HashSet<int>();
        for (int t_i = 0; t_i < this.entries.Length; t_i++)
        {
            EmoteEntry t_entry = this.entries[t_i];
            if (t_entry == null) continue;

            if (t_entry.id == 0) { t_entry.id = t_nextId++; t_assigned = true; }
            if (!t_ids.Add(t_entry.id))
                Debug.LogWarning($"[EmoteCatalog] Duplicate emote id: {t_entry.id} (index {t_i}) — they hide each other.", this);
        }

        if (t_assigned) UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
