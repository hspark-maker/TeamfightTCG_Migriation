import {SlotPatch} from "./saveDocument";

/**
 * 신규 계정 최초 지급 골드. 클라 CurrencyManager.STARTING_GOLD 의 쌍둥이 —
 * 그쪽은 이제 튜토리얼 되감기 전용 안전망이고, 정상 부팅의 진실원은 여기다.
 */
export const STARTER_GOLD = 100;

/** 덱 슬롯 개수. 클라 DeckSaveManager.SLOT_COUNT 와 같아야 한다 — NormalizedSlots 가 항상 이 길이로 패딩한다. */
export const DECK_SLOT_COUNT = 6;

/** 덱 한 벌의 장수. 클라 DeckSaveManager.DECK_SIZE. */
export const STARTER_DECK_SIZE = 6;

/** 스타터 덱 이름. 클라 StarterDeck.DECK_NAME. */
export const STARTER_DECK_NAME = "스타터 덱";

/** 재화 4종. firestore.rules 의 isValidSave 가 이 4키를 정확히 요구한다 — 하나라도 빠지면 이후 저장이 영구 거부된다. */
const CURRENCY_KEYS = ["Gold", "Diamond", "Energy", "Shard"];

/**
 * 신규 계정 문서의 슬롯 10개. 메타 5키(schemaVersion/revision/updatedAt/deviceId/appVersion)는
 * ensureSaveDocument 가 얹는다.
 *
 * 모양의 진실원은 Tools/firestore-rules-tests/fixtures/saveDocument.js 의
 * serverFreshAccountDocument() 다 — 저기와 갈리면 신규 계정의 첫 클라 저장이 룰에 막힌다.
 * @param {number[]} starterCardIds 지급할 카드 id (STARTER_DECK_SIZE 장)
 * @return {SlotPatch} 슬롯 10개
 */
export function buildFreshAccountSlots(starterCardIds: number[]): SlotPatch {
  const balances: Record<string, number> = {};
  for (const key of CURRENCY_KEYS) balances[key] = 0;
  balances.Gold = STARTER_GOLD;

  const slots = [];
  for (let i = 0; i < DECK_SLOT_COUNT; i++) {
    slots.push(i === 0 ?
      // imageKey 는 빈 문자열로 둔다 — 덱 이미지 카탈로그는 클라 SO 라 서버가 키를 모른다.
      // DeckImages.ResolveForSlot 이 빈 키를 첫 카드 아트로 폴백하는 정상 경로다.
      {name: STARTER_DECK_NAME, cardIds: [...starterCardIds], imageKey: ""} :
      {name: "", cardIds: [] as number[], imageKey: ""});
  }

  return {
    currency: {balances},
    ownership: {cardIds: [...starterCardIds]},
    deck: {slots},
    cardGrowth: {entries: {}},
    keywordGrowth: {levels: {}},
    rank: {points: 0, claimedTiers: []},
    albumReward: {claimedKeys: []},
    tournament: {clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: ""},
    tutorial: {
      outgameCompleted: false,
      chapterIndex: 0,
      chapterStepIndex: 0,
      stepId: 0,
      // -1 은 "아직 부팅 좌표를 본 적 없다"는 뜻이다. 0 으로 두면 클라 TutorialSaveData 초기값과 갈린다.
      lastBootChapterIndex: -1,
      lastBootStepIndex: -1,
      sameCoordBootCount: 0,
      completedTriggers: [],
    },
    // 3필드 모두 null 이 설계다 — ProfileManager 가 IsNullOrEmpty 폴백으로 기본 아바타·프레임을 고른다.
    // 빈 문자열을 넣어도 동작은 같지만 "저작된 적 없음"과 "빈 값으로 저작됨"이 구분되지 않는다.
    profile: {nickname: null, avatarId: null, frameId: null},
  } as unknown as SlotPatch;
}
