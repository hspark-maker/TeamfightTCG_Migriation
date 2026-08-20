using System;
using System.Collections.Generic;
using UnityEngine;

// 보상 토너먼트 챕터 하나의 저작 항목(순서 = 진행 순서, 챕터 안 정점도 리스트 순서대로)
[Serializable]
public struct TournamentChapterDef
{
    [Tooltip("챕터 안정 키(토너먼트 내 유일). 완주 보상 수령 낙인이 이 키로 세이브에 남는다. " +
             "비워 두면 그 챕터의 완주 보상을 영영 받을 수 없다(정점 진행 자체는 영향 없다). " +
             "한 번 저작한 키는 바꾸지 마라 — 바꾸면 기존 유저가 완주 보상을 다시 받는다. " +
             "챕터를 지울 때도 키를 재사용하지 마라(옛 낙인이 새 챕터를 수령 완료로 만든다).")]
    public string chapterId;

    [Tooltip("챕터 표시명. 예: \"제1장 · 안개 숲\". 표시 전용이라 바꿔도 진행에는 영향이 없다.")]
    public string title;

    [Tooltip("이 챕터의 배경 그림 · 정점 자리 · 길 조각을 통째로 담은 타일 프리팹. 좌표의 진실원은 이 프리팹이고 코드는 좌표를 만들지 않는다. 저작 규약:\n" +
             "· 루트 RectTransform 크기 1080 x 2160 (anchor·pivot은 코드가 (0.5,0)으로 강제한다)\n" +
             "· 배경은 자식 Image 한 장. 폭은 Content 폭에 맞춰 균등 스케일되고 높이는 원본 비율을 따른다\n" +
             "· 자식 \"PathRoot\" 아래에 정점 자리를 아래 nodes와 같은 수만큼 둔다. 형제 순서 = 정점 순서(첫 자식 = 첫 정점)\n" +
             "· 자식 \"LinkRoot\" 아래에 길 조각을 (정점 수 - 1)개 둔다. 형제 순서 = 구간 순서(첫 자식 = 정점 0→1 구간). 조각 내부 구성은 자유다\n" +
             "· 챕터 경계 구간에는 길 조각이 없다 — 조각이 두 타일에 걸치면 저작이 쪼개진다\n" +
             "· 코드는 길 조각의 색만 바꾼다. 위치·회전·크기는 손대지 않으니 굽은 배경 길을 따라 직접 배치할 것(그림은 흰색 중립으로 저작)\n" +
             "· 앵커·피벗은 아무렇게나 저작해도 된다 — 코드가 부모를 타고 올라가며 실제 자리를 계산한다\n" +
             "· 표식 그림은 저작 중에만 보이고 실행할 때 꺼진다\n" +
             "· 개수가 안 맞으면 코드가 메우지 않고 에러를 낸다 — 자리가 없는 정점은 화면에 나오지 않는다\n" +
             "비우면 그 챕터의 정점과 길이 통째로 안 나온다(에러).")]
    public GameObject tilePrefab;

    [Tooltip("챕터 안 정점 목록. 리스트 순서 = 도전 순서이며, 앞 챕터 마지막 정점을 깨야 다음 챕터 첫 정점이 열린다. " +
             "0개면 완주 판정 모수가 없어 런타임에선 그냥 완주로 넘어간다 — 진행이 막히진 않지만 검증 오류다.")]
    public List<TournamentNodeDef> nodes;

    [Tooltip("챕터 완주 보상(복수). 챕터의 모든 정점을 깬 뒤 한 번만 지급된다. " +
             "비워두면 완주해도 지급이 없다(다음 챕터 해금만 된다).")]
    public List<AlbumRewardDef> completionRewards;

    // 거짓이면 완주 보상을 수령할 수 없다 — 낙인을 남길 키가 없다
    public bool HasStableKey => !string.IsNullOrEmpty(this.chapterId);

    public int NodeCount => this.nodes != null ? this.nodes.Count : 0;
}
