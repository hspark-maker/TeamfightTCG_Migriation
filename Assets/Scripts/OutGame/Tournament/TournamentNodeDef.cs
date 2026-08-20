using System;
using System.Collections.Generic;
using UnityEngine;

// 보상 토너먼트 정점 하나의 저작 항목(순서 = 리스트 순서, 진행은 영구)
[Serializable]
public struct TournamentNodeDef
{
    [Tooltip("정점 안정 키(토너먼트 내 유일). 클리어 낙인이 이 키로 세이브에 남는다. " +
             "비워 두면 그 정점은 영구 잠금이고 뒤 정점도 열리지 않는다. " +
             "한 번 저작한 키는 바꾸지 마라 — 바꾸면 기존 유저의 클리어가 통째로 풀리고 보상을 다시 준다. " +
             "정점을 지울 때도 키를 재사용하지 마라(옛 낙인이 새 정점을 클리어 상태로 만든다).")]
    public string nodeId;

    [Tooltip("정점 표시명(상대 이름). 표시 전용이라 바꿔도 진행에는 영향이 없다.")]
    public string displayName;

    [Tooltip("상대 초상. 비우면 맵 셀 프리팹에 저작된 스프라이트를 그대로 쓴다.")]
    public Sprite avatar;

    [Tooltip("이 정점의 고정 상대 덱. 비면 전투를 열 수 없다(검증기가 보고한다).")]
    public List<CardData> enemyDeck;

    [Tooltip("상대 카드 레벨. 0 이하면 미강화(1)로 떨어진다.")]
    public int aiCardLevel;

    [Tooltip("승리 보상(복수). 비워두면 클리어해도 지급이 없다(해금만 넘어간다).")]
    public List<AlbumRewardDef> rewards;

    // 거짓이면 이 정점은 영구 Locked다 — 낙인을 남길 키가 없다
    public bool HasStableKey => !string.IsNullOrEmpty(this.nodeId);

    // 저작 0(struct 기본값)을 미강화로 흡수 — 인스펙터 기본값이 없는 자리라 코드에서 바닥을 깐다
    public int AiCardLevelOrBase => this.aiCardLevel < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : this.aiCardLevel;
}
