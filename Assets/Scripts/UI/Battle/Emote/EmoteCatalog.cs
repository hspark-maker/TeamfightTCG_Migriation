using System;
using UnityEngine;

/// <summary>
/// 감정표현 한 칸. sprite는 선택 표의 대표 그림이자 정지 폴백이고, clip이 있으면 스티커가
/// 떠 있는 동안 그 클립을 반복한다. clip은 루트 Image의 m_Sprite를 애니메이션하는 파일이어야 한다.
/// </summary>
[Serializable]
public class EmoteEntry
{
    [Tooltip("선택 표의 아이콘이자 AnimationClip이 없거나 유효하지 않을 때 표시할 정지 그림.")]
    public Sprite sprite;

    [Tooltip("스티커 표시 중 반복 재생할 선택 AnimationClip. 비어 있으면 sprite를 정지 상태로 표시한다. 클립은 루트 Image.m_Sprite를 애니메이션해야 한다.")]
    public AnimationClip clip;

    [Tooltip("그림과 클립이 모두 없을 때 표시할 문자. 선택 표에서도 같은 값을 쓴다.")]
    public string label = "?";
}

/// <summary>
/// 선택 표와 실제 스티커가 함께 보는 감정표현 목록의 단일 진실원. 각 칸은 정지 sprite와 선택 clip을
/// 독립적으로 가질 수 있으며, clip이 없는 칸은 별도 재생기 없이 정지 그림만 유지한다.
/// </summary>
[CreateAssetMenu(fileName = "EmoteCatalog", menuName = "Card Battle/Emote Catalog")]
public class EmoteCatalog : ScriptableObject
{
    public const int Capacity = 6;   // 2×3(가로 2 × 세로 3)

    [SerializeField] EmoteEntry[] entries = new EmoteEntry[Capacity];

    [Header("표시")]
    [Tooltip("스티커가 화면에 머무는 전체 시간(초).")]
    [Min(0.1f)] public float showDuration = 2f;

    [Tooltip("문자 폴백이 나타나고 사라지는 데 쓰는 시간(초).")]
    [Min(0f)] public float fadeDuration = 0.15f;

    [Tooltip("문자 폴백 등장 시 최대 배율.")]
    [Min(1f)] public float popScale = 1.25f;

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

    public int Count => this.entries != null ? Mathf.Min(this.entries.Length, Capacity) : 0;

    public EmoteEntry Get(int _index)
    {
        if (this.entries == null || _index < 0 || _index >= this.entries.Length) return null;
        return this.entries[_index];
    }
}
