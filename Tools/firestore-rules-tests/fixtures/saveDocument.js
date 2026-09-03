// 세이브 문서 픽스처.
//
// 진실원: Assets/Scripts/OutGame/Save/4.Cloud/PlayerSaveDocument.cs 의 ToFieldMap
//         (필드 이름) + OutGame/Save/2.Domain/*SaveData.cs (슬롯 내부 모양).
// 저기가 바뀌면 여기도 바뀌어야 한다. 안 맞추면 룰이 조용히 낡고, 배포하는 날
// 전 유저 저장이 거부된다 — 커밋 809d040d3 이 정확히 그 사고였다.
import { serverTimestamp } from 'firebase/firestore';

/** UserSaveData.VERSION 과 functions/src/save/saveDocument.ts 의 SCHEMA_VERSION 쌍둥이 상수. */
export const SCHEMA_VERSION = 8;

/** PlayerSaveDocument.DeviceId() = Guid.ToString("N") → 32자 hex. */
export const DEVICE_ID = '0123456789abcdef0123456789abcdef';

/**
 * 메타 5 + 슬롯 9 = 14키. 슬롯 값은 각 SaveData 의 기본 생성 상태를 옮긴 것이다.
 *
 * C6(v8) 부터 재화 슬롯 `currency` 는 여기 없다 — 잔액이 형제 문서
 * envs/{env}/users/{uid}/wallet/current 로 이사했고, 그쪽 모양은 walletDocument.js 가 맡는다.
 * 승급 후 클라(PlayerSaveDocument.ToFieldMap)가 실제로 보내는 모양이 이 14키다.
 *
 * C7 부터 currency 는 optional 이 아니라 금지 필드다 — 이 14키가 룰이 받는 유일한 모양이고,
 * 여기에 currency 를 얹은 15키는 rules.test.js 의 13c 가 거부를 못박는다.
 */
export function saveDocument(_revision, _overrides = {}) {
  return {
    schemaVersion: SCHEMA_VERSION,
    revision: _revision,
    updatedAt: serverTimestamp(),
    deviceId: DEVICE_ID,
    appVersion: '0.1.0',

    ownership: { cardIds: [1, 2, 3] },
    deck: { slots: [{ name: '기본 덱', cardIds: [1, 2, 3], imageKey: '' }] },
    cardGrowth: { entries: { 1: { level: 1, snack: 0, limitBreak: 0 } } },
    keywordGrowth: { levels: { Ranged: 0 } },
    rank: { points: 0, claimedTiers: [] },
    albumReward: { claimedKeys: [] },
    adventure: { clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: '' },
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
 * 구 클라(v7)가 싣던 재화 슬롯. C7 이 공존을 닫아 룰은 이제 이게 실린 15키를 거부한다 —
 * 남은 용도는 둘뿐이다: 13c 의 거부 픽스처와, freshAccountDocument 화석(14d).
 *
 * 4재화가 전부 실린다: CurrencySaveData.Normalize 가 ECurrencyType.Count 까지 순회하며
 * 없는 키를 0으로 채우고, CurrencyManager 가 Init·Save 양쪽에서 그걸 불렀다.
 * Gold 하나만 넣으면 합성 페이로드가 되어 구 클라 모양을 잘못 본뜨게 된다.
 */
export function legacyCurrencySlot() {
  return { balances: { Gold: 100, Diamond: 0, Energy: 0, Shard: 0 } };
}

/**
 * R4 이전에 Unity 클라가 직접 만들던 첫 문서(에뮬레이터에 붙여 캡처한 모양).
 * 지금은 서버가 문서를 만들므로 이 모양은 더 이상 생기지 않는다 — 남겨 둔 이유는
 * "옛 클라가 보내던 그대로여도 create 는 거부된다"를 14d 가 못박기 때문이다.
 *
 * 위 saveDocument 와 네 군데가 다르다:
 * - currency 가 실려 15키다 (v8 에서 베이스가 14키로 줄었어도 화석은 옛 모양을 지킨다 —
 *   여기까지 따라 줄이면 14d 가 serverFreshAccountDocument 와 같은 모양을 두 번 보게 된다)
 * - cardGrowth.entries / keywordGrowth.levels 가 빈 map (기본값 이하 항목은 저장에서 빠진다)
 * - deck.slots 는 고정 길이라 빈 슬롯도 원소로 들어간다 (빈 배열·빈 문자열)
 * - profile 3필드가 전부 null (기본 id 를 세이브에 굳히지 않는 설계)
 */
export function freshAccountDocument() {
  return saveDocument(1, {
    currency: legacyCurrencySlot(),
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
    adventure: { clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: '' },
    profile: { nickname: null, avatarId: null, frameId: null },
  });
}

/**
 * ensureAccount 가 만드는 첫 문서. functions/src/save/freshAccount.ts 의 buildFreshAccountSlots 쌍둥이다 —
 * 저기가 바뀌면 여기도 바꾼다(서버 쪽은 scripts/test-fresh-account.js 가 같은 값을 반대편에서 못박는다).
 *
 * v8 부터 슬롯은 9개고 currency 는 없다 — 스타터 골드는 buildFreshAccountBalances 가 같은
 * 트랜잭션에서 지갑 문서로 낸다. 여기에 currency 를 되살리면 서버 산출물과 갈려,
 * 하네스는 초록인데 실제 신규 계정은 다른 문서 위에서 도는 상태가 된다.
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
    ownership: { cardIds: [...STARTER_CARD_IDS] },
    deck: { slots: t_slots },
    cardGrowth: { entries: {} },
    keywordGrowth: { levels: {} },
    rank: { points: 0, claimedTiers: [] },
    albumReward: { claimedKeys: [] },
    adventure: { clearedNodeIds: [], claimedChapterIds: [], pendingRewardNodeId: '' },
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
    // 닉네임은 서버가 계정 생성 시 낱말표에서 뽑아 굳힌다(functions/src/profile/generateNickname.ts) —
    // 값 자체는 아무 12자 이하 문자열이면 되고, 룰은 profile 안쪽을 보지 않는다.
    profile: { nickname: '푸른 여우', avatarId: null, frameId: null },
    ..._overrides,
  });
}
