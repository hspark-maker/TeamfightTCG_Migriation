// 튜토리얼 무료 한 방 문서 픽스처.
//
// 진실원: functions/src/growth/tutorialGrants.ts 의 GRANT_SCHEMA_VERSION · writeGrantUsed (필드 이름·모양).
// 지갑 픽스처와 같은 성격이다 — 룰에 값 검증이 없으므로(write: if false) 이 픽스처는
// 거부 케이스가 **실재하는 문서** 위에서 거부되는지를 보기 위한 seed 재료다.
// 문서가 없으면 거부가 아니라 부재로 실패해 통과처럼 보인다.
import { serverTimestamp } from 'firebase/firestore';

/** tutorialGrants.ts 의 GRANT_SCHEMA_VERSION 쌍둥이 상수. 세이브·지갑과 별개 축이다. */
export const GRANT_SCHEMA_VERSION = 1;

/** writeGrantUsed 가 쓰는 문서. 축은 둘이고 각각 계정당 1회다. */
export function grantsDocument(_overrides = {}) {
  return {
    schemaVersion: GRANT_SCHEMA_VERSION,
    enhanceCard: true,
    enhanceKeyword: false,
    updatedAt: serverTimestamp(),
    ..._overrides,
  };
}
