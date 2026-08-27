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

    currency: { balances: { Gold: 100 } },
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

/** 신규 계정의 첫 업로드 모양. ProfileSaveData 의 문자열 3개는 초기값이 없어 null 로 실린다. */
export function freshAccountDocument() {
  return saveDocument(1, { profile: { nickname: null, avatarId: null, frameId: null } });
}
