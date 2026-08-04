using System;
using System.Collections.Generic;

// 카드 성장(강화 레벨·진화 단계) 세이브 값 객체. 카드는 안정 문자열 키(CardCatalog.KeyOf)로 저장 — 인덱스 금지.
// 필드는 추가만(하위호환) — 기존 필드 의미 변경·삭제·리네임 금지. 모르는/누락 키는 기본값(Lv0·미진화) 처리.
[Serializable]
public class CardGrowthSaveData
{
    // 성장 스키마를 바꿔야 할 때만 올린다(마이그레이션 진입점).
    public const int VERSION = 1;

    public int version = VERSION;

    // 성장한 카드만 담는다(순서 무의미 — 키로 조회). Lv0·미진화 카드는 항목 자체를 두지 않는다.
    public List<CardGrowthEntry> entries = new List<CardGrowthEntry>();
}

// 카드 한 장의 성장 진행도.
[Serializable]
public class CardGrowthEntry
{
    // 카드 안정 키(CardCatalog.KeyOf = SO 파일명). 배열 위치가 아님.
    public string cardKey;

    // 강화 레벨(0 = 미강화).
    public int level;

    // 진화 단계(0 = 미진화). 레벨에서 파생시키지 않고 따로 저장한다 —
    // "게이트 레벨에 도달했지만 아직 진화하지 않은" 상태가 유효한 상태이기 때문.
    public int evolutionStage;
}
