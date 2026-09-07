// 튜토리얼 무료 한 방 문서 픽스처.
//
// 진실원: functions/src/growth/tutorialGrants.ts 의 GRANT_SCHEMA_VERSION · writeGrantUsed ·
// writePackGranted (필드 이름·모양).
// 지갑 픽스처와 같은 성격이다 — 룰에 값 검증이 없으므로(write: if false) 이 픽스처는
// 거부 케이스가 **실재하는 문서** 위에서 거부되는지를 보기 위한 seed 재료다.
// 문서가 없으면 거부가 아니라 부재로 실패해 통과처럼 보인다.
import { serverTimestamp } from 'firebase/firestore';

/** tutorialGrants.ts 의 GRANT_SCHEMA_VERSION 쌍둥이 상수. 세이브·지갑과 별개 축이다. */
export const GRANT_SCHEMA_VERSION = 1;

/**
 * 서버가 쓰는 문서. 강화 무료 한 방 축 둘(writeGrantUsed)과 무료 팩 지급 낙인(writePackGranted)이
 * 한 문서에 산다. 서버는 축마다 merge 로 자기 필드만 쓰므로 실제 문서에는 일부만 있을 수 있다.
 */
export function grantsDocument(_overrides = {}) {
  return {
    schemaVersion: GRANT_SCHEMA_VERSION,
    enhanceCard: true,
    enhanceKeyword: false,
    packs: { StarterPack: true },
    updatedAt: serverTimestamp(),
    ..._overrides,
  };
}
