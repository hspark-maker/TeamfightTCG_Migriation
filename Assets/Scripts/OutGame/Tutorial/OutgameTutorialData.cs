using System.Collections.Generic;
using UnityEngine;

// 아웃게임 첫시작 튜토리얼의 챕터 시퀀스(에디터 저작, SO)
[CreateAssetMenu(fileName = "OutgameTutorial", menuName = "Card Battle/Outgame Tutorial")]
public class OutgameTutorialData : ScriptableObject
{
    [Header("챕터 시퀀스 (순서 = 진행 순서, 챕터·스텝 인덱스가 곧 세이브 진행도)")]
    [Tooltip("챕터 하나 = 기획의 '튜토리얼 N편'. 스텝 행은 상태를 갖지 않으므로 같은 행을 여러 자리에 복제해도 된다")]
    public List<OutgameTutorialChapter> chapters = new List<OutgameTutorialChapter>();
}
