using System.Collections.Generic;
using UnityEngine;

/// <summary>아웃게임 첫시작 튜토리얼의 스텝 시퀀스(에디터 저작, SO). 스텝 SO를 순서대로 꽂는 조립 목록일 뿐이다.
/// 스텝 종류별 필드·실행은 각 스텝 SO가 갖고, 진행도 영속은 OutgameTutorialProgress가, 순서 해석은 러너가 맡는다.
/// </summary>
[CreateAssetMenu(fileName = "OutgameTutorial", menuName = "Card Battle/Outgame Tutorial")]
public class OutgameTutorialData : ScriptableObject
{
    [Header("스텝 시퀀스 (순서 = 진행 순서, 인덱스가 곧 세이브 진행도)")]
    [Tooltip("스텝은 상태를 갖지 않으므로 같은 에셋을 여러 칸에 재사용해도 된다")]
    public List<OutgameTutorialStep> steps = new List<OutgameTutorialStep>();
}
