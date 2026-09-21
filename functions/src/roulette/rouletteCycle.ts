import {createHash} from "node:crypto";
import {effectiveWeight, RollFn, RouletteSlot} from "./rouletteDraw";

/** 서버 전용. 날짜/로그아웃으로 초기화하지 않고 가중치 합계만큼 돌린 뒤 새 주기를 시작한다. */
export interface RouletteCycle {
  signature: string;
  remaining: number[];
}

/**
 * 문서 ID에 요청 문자열의 경로 구분자가 들어가지 않도록 한다.
 * @param {string} rouletteId 판 식별자
 * @return {string} 경로에 안전한 상태 문서 ID
 */
export function rouletteCycleKey(rouletteId: string): string {
  return createHash("sha256").update(rouletteId).digest("hex");
}

/**
 * 가중치를 주기당 지급 횟수로 사용한다. 당첨된 칸의 남은 몫만 1 줄인다.
 * 현재 표는 합계 100: 순서는 무작위지만 한 주기의 칸별 지급 횟수는 모든 유저가 같다.
 * @param {RouletteSlot[]} slots 현재 판
 * @param {unknown} saved 서버에 저장한 이전 주기. 신규 계정이면 undefined
 * @param {RollFn} roll [0, max) 정수 난수원
 * @return {{slot: RouletteSlot, cycle: RouletteCycle}} 당첨 칸과 지급 트랜잭션에서 저장할 다음 상태
 */
export function drawRouletteCycle(
  slots: RouletteSlot[], saved: unknown, roll: RollFn
): {slot: RouletteSlot; cycle: RouletteCycle} {
  const ordered = [...slots].sort((a, b) => a.slotIndex - b.slotIndex);
  const quotas = ordered.map((slot) => effectiveWeight(slot.weight));
  const total = quotas.reduce((sum, value) => sum + value, 0);
  if (!ordered.length || !quotas.every((value) => Number.isSafeInteger(value) && value > 0) ||
      !Number.isSafeInteger(total) || total >= 2 ** 48) {
    throw new Error("Invalid roulette cycle weights.");
  }
  // 슬롯 순서 변경은 무시한다. 상품/수량/확률 변경 시에만 새 판의 주기를 시작한다.
  const signature = createHash("sha256").update(JSON.stringify(ordered.map((slot, i) =>
    [slot.slotIndex, slot.rewardType, slot.rewardId, slot.amount, quotas[i]]))).digest("hex");
  const previous = saved as Partial<RouletteCycle> | undefined;
  let remaining = [...quotas];
  if (previous?.signature === signature) {
    if (!Array.isArray(previous.remaining) || previous.remaining.length !== quotas.length ||
        !previous.remaining.every((value, i) => Number.isSafeInteger(value) && value >= 0 && value <= quotas[i])) {
      throw new Error("Invalid saved roulette cycle.");
    }
    if (previous.remaining.some((value) => value > 0)) remaining = [...previous.remaining];
  }
  const available = remaining.reduce((sum, value) => sum + value, 0);
  let ticket = roll(available);
  if (!Number.isInteger(ticket) || ticket < 0 || ticket >= available) throw new Error("Invalid roulette roll.");
  for (let i = 0; i < remaining.length; i++) {
    ticket -= remaining[i];
    if (ticket < 0) {
      remaining[i]--;
      return {slot: ordered[i], cycle: {signature, remaining}};
    }
  }
  throw new Error("Roulette cycle exhausted unexpectedly.");
}
