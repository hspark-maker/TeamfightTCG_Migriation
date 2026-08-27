// 세이브 문서 픽스처.
//
// 진실원: Assets/Scripts/OutGame/Save/4.Cloud/PlayerSaveDocument.cs 의 ToFieldMap
//         (필드 이름) + OutGame/Save/2.Domain/*SaveData.cs (슬롯 내부 모양).
// 저기가 바뀌면 여기도 바뀌어야 한다. 안 맞추면 룰이 조용히 낡고, 배포하는 날
// 전 유저 저장이 거부된다 — 커밋 809d040d3 이 정확히 그 사고였다.
import { serverTimestamp } from 'firebase/firestore';

/** UserSaveData.VERSION 과 functions/src/save/saveDocument.ts 의 SCHEMA_VERSION 쌍둥이 상수. */
export const SCHEMA_VERSION = 7;

/** PlayerSaveDocument.DeviceId() = Guid.ToString("N") → 32자 hex. */
export const DEVICE_ID = '0123456789abcdef0123456789abcdef';

/** 메타 5 + 슬롯 10 = 15키. 슬롯 값은 각 SaveData 의 기본 생성 상태를 옮긴 것이다. */
export function saveDocument(_revision, _overrides = {}) {
  return {
    schemaVersion: SCHEMA_VERSION,
    revision: _revision,
    updatedAt: serverTimestamp(),
    deviceId: DEVICE_ID,
    appVersion: '0.1.0',

    // 4재화가 전부 실린다. CurrencySaveData.Normalize 가 ECurrencyType.Count 까지
    // 순회하며 없는 키를 0으로 채우고, CurrencyManager 가 Init·Save 양쪽에서 그걸 부른다.
    // 에뮬레이터에 클라가 실제로 만든 신규 계정 문서도 이 모양이다.
    // Gold 하나만 넣으면 합성 페이로드가 되어 룰을 틀렸다고 오판하게 된다.
    currency: { balances: { Gold: 100, Diamond: 0, Energy: 0, Shard: 0 } },
    ownership: { cardIds: [1, 2, 3] },
    deck: { slots: [{ name: '기본 덱', cardIds: [1, 2, 3], imageKey: '' }] },
    cardGrowth: { entries: { 1: { level: 1, snack: 0, limitBreak: 0 } } },
    keywordGrowth: { levels: { Ranged: 0 } },
    rank: { points: 0, claimedTiers: [] },
    albumReward: { claimedKeys: [] },
    tournament: { clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: '' },
    tutorial: {
      outgameCompleted: false,
      chapterIndex: 0,
      chapterStepIndex: 0,
      stepId: 0,
      lastBootChapterIndex: -1,
      lastBootStepIndex: -1,
      sameCoordBootCount: 0,
      completedTriggers: [],
    },
    profile: { nickname: '', avatarId: '', frameId: '' },

    ..._overrides,
  };
}

/**
 * R4 이전에 Unity 클라가 직접 만들던 첫 문서(에뮬레이터에 붙여 캡처한 모양).
 * 지금은 서버가 문서를 만들므로 이 모양은 더 이상 생기지 않는다 — 남겨 둔 이유는
 * "옛 클라가 보내던 그대로여도 create 는 거부된다"를 14d 가 못박기 때문이다.
 *
 * 위 saveDocument 와 세 군데가 다르다:
 * - cardGrowth.entries / keywordGrowth.levels 가 빈 map (기본값 이하 항목은 저장에서 빠진다)
 * - deck.slots 는 고정 길이라 빈 슬롯도 원소로 들어간다 (빈 배열·빈 문자열)
 * - profile 3필드가 전부 null (기본 id 를 세이브에 굳히지 않는 설계)
 */
export function freshAccountDocument() {
  return saveDocument(1, {
    ownership: { cardIds: [1, 2, 3] },
    deck: {
      slots: [
        { name: '', cardIds: [1, 2, 3], imageKey: '' },
        { name: '', cardIds: [], imageKey: '' },
        { name: '', cardIds: [], imageKey: '' },
      ],
    },
    cardGrowth: { entries: {} },
    keywordGrowth: { levels: {} },
    rank: { points: 0, claimedTiers: [] },
    albumReward: { claimedKeys: [] },
    tournament: { clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: '' },
    profile: { nickname: null, avatarId: null, frameId: null },
  });
}

/**
 * ensureAccount 가 만드는 첫 문서. functions/src/save/freshAccount.ts 의 buildFreshAccountSlots 쌍둥이다 —
 * 저기가 바뀌면 여기도 바꾼다(서버 쪽은 scripts/test-fresh-account.js 가 같은 값을 반대편에서 못박는다).
 *
 * R4 이후 create 는 룰이 막으므로 이 문서는 Admin SDK 로만 생긴다. 하네스가 여기서 봐야 하는 것은
 * "서버가 만든 문서 위에서 클라의 다음 update 가 통과하는가" 다 — 서버 산출물이 isValidSave 를 깨면
 * 그 계정은 이후 모든 저장이 영구 거부되고 delete: if false 라 룰 층에 복구 경로가 없다.
 */
export const STARTER_CARD_IDS = [1, 28, 20, 6, 11, 30];

/** 클라 DeckSaveManager.SLOT_COUNT. NormalizedSlots 가 항상 이 길이로 패딩한다. */
export const DECK_SLOT_COUNT = 6;

export function serverFreshAccountDocument(_overrides = {}) {
  const t_slots = [{ name: '스타터 덱', cardIds: [...STARTER_CARD_IDS], imageKey: '' }];
  while (t_slots.length < DECK_SLOT_COUNT) t_slots.push({ name: '', cardIds: [], imageKey: '' });

  return saveDocument(1, {
    currency: { balances: { Gold: 100, Diamond: 0, Energy: 0, Shard: 0 } },
    ownership: { cardIds: [...STARTER_CARD_IDS] },
    deck: { slots: t_slots },
    cardGrowth: { entries: {} },
    keywordGrowth: { levels: {} },
    rank: { points: 0, claimedTiers: [] },
    albumReward: { claimedKeys: [] },
    tournament: { clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: '' },
    // 베이스에서 물려받지 않고 명시한다 — 여기 lastBoot* = -1 은 서버와 맞춰야 하는 계약이라,
    // 기존 계정 픽스처를 손댔을 때 "서버 쌍둥이"가 조용히 따라 바뀌면 안 된다.
    tutorial: {
      outgameCompleted: false,
      chapterIndex: 0,
      chapterStepIndex: 0,
      stepId: 0,
      lastBootChapterIndex: -1,
      lastBootStepIndex: -1,
      sameCoordBootCount: 0,
      completedTriggers: [],
    },
    profile: { nickname: null, avatarId: null, frameId: null },
    ..._overrides,
  });
}
