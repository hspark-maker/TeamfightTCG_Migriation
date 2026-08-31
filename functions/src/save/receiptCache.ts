/**
 * 영수증에 싣는 **캐시본**의 코덱. 같은 txId 로 다시 온 요청이 첫 응답을 그대로 받도록,
 * 응답을 영수증에 넣을 모양으로 접고(cacheableResponse) 다시 펴는(replayCached) 두 함수뿐이다.
 *
 * `firebase-admin`·`HttpsError` 를 지지 않는다 — 순수 회귀(`scripts/`)가 `lib/` 를 직접
 * require 해서 이 접기·펴기를 재기 때문이다. 거절 코드는 호출부(mutateSave)가 정한다.
 */

/** 세이브 슬롯의 갱신 후 전체 값. save/saveDocument 의 SlotPatch 와 같은 모양이다. */
type Slots = Record<string, Record<string, unknown>>;

/** 영수증에 실을 캐시본. updatedSlots 자리에 슬롯 **이름**만 남는다. */
export type CachedResponse = {slotKeys: string[]} & Record<string, unknown>;

/**
 * 응답을 영수증에 실을 캐시본으로 접는다.
 *
 * **슬롯 값을 빼는 이유**: openPack 의 ownership 은 슬롯 전체 값이라 계정이 자랄수록 커지고,
 * 영수증 문서가 1MiB 상한을 치면 트랜잭션이 통째로 실패해 **정상 명령이 죽는다**.
 * 이름만 적어 두면 재시도 때 세이브 문서에서 현재값을 꺼내 다시 지을 수 있다.
 * @param {object} response finalize 가 만든 응답
 * @param {Slots} slots 이 트랜잭션이 쓴 슬롯
 * @return {CachedResponse} 슬롯 값을 뺀 캐시본
 */
export function cacheableResponse(response: object, slots: Slots): CachedResponse {
  const body = response as Record<string, unknown>;
  // JSON.stringify 가 undefined 필드를 버리므로 updatedSlots 는 영수증에 아예 실리지 않는다.
  return {...body, updatedSlots: undefined, slotKeys: Object.keys(slots)};
}

/**
 * 영수증에 남은 캐시본으로 첫 응답을 되살린다. updatedSlots 는 슬롯 이름으로
 * **지금 세이브 문서**에서 다시 짓는다 — 값이 더 최신이라 클라 채택도 더 옳다.
 *
 * 못 쓸 캐시본은 **던진다**(readReceipt 가 깨진 JSON 에서 던지는 것과 같은 자세다).
 * 조용히 기본값을 내보내면 클라가 revision undefined 를 채택하고 그 자리에서 상태가 갈린다
 * — 되풀이되는 실패가 조용한 오답보다 낫다.
 * @param {unknown} cached 영수증의 result
 * @param {Record<string, unknown>} current 이 트랜잭션이 읽은 세이브 문서
 * @param {number} currentRevision 지금 세이브 문서의 revision
 * @return {Record<string, unknown>} 첫 응답과 같은 모양
 */
export function replayCached(
  cached: unknown,
  current: Record<string, unknown>,
  currentRevision: number,
): Record<string, unknown> {
  if (cached === null || typeof cached !== "object") {
    throw new Error("receipt has no cached response");
  }

  const body = cached as Partial<CachedResponse>;
  const revision = body.revision;
  if (!Number.isInteger(revision)) {
    // C8-1 시절 result 없이 끊긴 영수증이 여기 온다. 그대로 내보내면 revision 이 빠진 응답이 되고
    // 클라가 그것을 채택한다.
    throw new Error("cached response has no revision");
  }

  // 정상 재시도에서는 반드시 같다 — 첫 시도가 이 revision 을 커밋했고, 응답을 못 받은 클라는
  // 같은 txId 로 다시 올 뿐 그 사이에 다른 쓰기를 하지 않는다. 다르면 세상이 움직인 것이고,
  // 첫 시도의 revision·지갑에 지금 슬롯 값을 섞어 내보내면 클라 상태가 갈린다.
  if (revision !== currentRevision) {
    throw new Error(
      `cached revision ${String(revision)} does not match the document revision ${currentRevision}`);
  }

  const slotKeys = Array.isArray(body.slotKeys) ? body.slotKeys : [];
  const updatedSlots: Slots = {};
  for (const key of slotKeys) {
    const value = current[String(key)];
    // 슬롯이 사라졌을 리는 없지만, 없으면 그 슬롯만 빠질 뿐 나머지 채택은 진행돼야 한다.
    if (value !== null && typeof value === "object") {
      updatedSlots[String(key)] = value as Record<string, unknown>;
    }
  }

  const replay: Record<string, unknown> = {...body, updatedSlots};
  delete replay.slotKeys;
  return replay;
}
