/**
 * 룰렛 추첨. 판 하나(Roulette 표의 헤더 한 행 + RouletteSlot 표의 칸 행들)를 합쳐 비용·칸 목록을
 * 세우고, 칸 하나를 뽑는다.
 *
 * **팩 추첨(packs/packDraw)을 import 하지 않는다.** 두 축은 우연히 닮았을 뿐이고, 묶는 순간
 * 팩 확률 규칙을 고칠 때마다 룰렛 확률이 함께 움직인다. RollFn 은 구조적 타입이라
 * node:crypto 의 randomInt 가 양쪽에 그대로 들어간다.
 *
 * 계약:
 * - 칸 순서는 RouletteSlot 행의 id 오름차순이다(리더가 세워서 넘긴다)
 * - 유효 가중치는 weight > 0 ? weight : 1 (클라 RouletteSlotDef.EffectiveWeight 와 같다)
 * - 비용은 Roulette 표의 헤더 한 행이 든다 — 헤더가 없거나 못 읽으면 과금하지 않고 접는다
 * - 상품 재화는 CURRENCY_KEYS 로 **정확히** 대조한다(parseCurrency 의 Gold 폴백 금지)
 *
 * Firestore 도 HttpsError 도 모른다(scripts/test-roulette.js 가 직접 부른다).
 */

import {CURRENCY_KEYS, CurrencyKey} from "../currency/currencyKeys";

/** 판 하나의 칸 수. 클라 RouletteConfig.SLOT_COUNT 와 같아야 한다 — 판 그림이 8쐐기다. */
export const ROULETTE_SLOT_COUNT = 8;

/** 지원하는 상품 종류. */
export const REWARD_TYPE_CURRENCY = "Currency";

/** 룰렛 티켓 키. 티켓 칸은 회전이 스스로를 재생산하므로 상품이 될 수 없다. */
const ROULETTE_TICKET_KEY = "RouletteTicket";

/** 난수원. 0 이상 max 미만의 정수를 낸다(crypto.randomInt 형태). */
export type RollFn = (max: number) => number;

/** Roulette 표의 한 행(판 하나 = 헤더 한 행). 스펙시트 열 그대로다. */
export interface RouletteHeaderRow {
  id: number;
  rouletteId: string;
  displayName: string;
  priceType: string;
  price: number;
  sortOrder: number;
}

/** RouletteSlot 표의 한 행(칸 하나). 스펙시트 열 그대로다(#memo 는 데이터가 아니라 읽지 않는다). */
export interface RouletteSlotRow {
  id: number;
  rouletteId: string;
  slotIndex: number;
  rewardType: string;
  rewardId: string;
  amount: number;
  weight: number;
}

/** 판 위의 칸 하나. */
export interface RouletteSlot {
  slotIndex: number;
  rewardType: "Currency" | "Pack";
  rewardId: string;
  currency: CurrencyKey | null;
  amount: number;
  weight: number;
}

/** 판 한 벌. 비용은 헤더 행에서, 칸은 칸 표 id 오름차순이다. */
export interface RouletteBoard {
  rouletteId: string;
  priceType: CurrencyKey;
  price: number;
  slots: RouletteSlot[];
  /** 저작 결함으로 버린 칸 행 수. 로그 전용 — 사고를 조용히 삼키지 않기 위해 센다. */
  droppedRows: number;
}

/**
 * 가중치 0·음수를 균등 1로 읽는다. 클라 RouletteSlotDef.EffectiveWeight 와 같다.
 * @param {number} weight 저작 가중치
 * @return {number} 유효 가중치
 */
export function effectiveWeight(weight: number): number {
  return weight > 0 ? weight : 1;
}

/**
 * 재화 키를 정확히 대조한다. 못 찾으면 null — parseCurrency 를 쓰면 오타가 금화가 된다
 * (commands/claimBattleReward 가 같은 지점을 짚는다).
 * @param {string} raw 표에 실린 재화 이름
 * @return {CurrencyKey | null} 재화 키
 */
function exactCurrency(raw: string): CurrencyKey | null {
  return CURRENCY_KEYS.find((key) => key === raw) ?? null;
}

/**
 * 판 한 벌을 세운다. 헤더가 없거나 비용을 못 읽으면 null 이다(fail-closed).
 * 개별 칸 행 결함은 그 행만 버리고 droppedRows 로 센다 — 칸 하나가 빠져도 판은 돌아야 한다.
 * @param {RouletteHeaderRow | null} header 이 판의 헤더 행. 표에 없으면 null
 * @param {RouletteSlotRow[]} slotRows 이 판의 칸 행 전부(id 오름차순)
 * @return {RouletteBoard | null} 판. 저작이 쓸 수 없으면 null
 */
export function resolveRouletteBoard(
  header: RouletteHeaderRow | null, slotRows: RouletteSlotRow[]
): RouletteBoard | null {
  if (!header) return null;

  const priceType = exactCurrency(String(header.priceType));
  if (priceType === null) return null;

  const price = Number(header.price);
  if (!Number.isInteger(price) || price <= 0) return null;

  const slots: RouletteSlot[] = [];
  const taken = new Set<number>();
  let droppedRows = 0;

  for (const row of slotRows) {
    const type = String(row.rewardType).trim().toLowerCase();
    if (type !== "currency" && type !== "pack") {
      droppedRows++;
      continue;
    }

    const amount = Number(row.amount);
    if (!Number.isSafeInteger(amount) || amount <= 0 || (type === "pack" && amount > 100)) {
      droppedRows++;
      continue;
    }

    const rewardId = String(row.rewardId ?? "");
    const currency = type === "currency" ? exactCurrency(rewardId) : null;
    // 티켓 상품 차단의 두 번째 관문이다(첫 관문은 시트 저작 안내다).
    if ((type === "currency" && (currency === null || currency === ROULETTE_TICKET_KEY)) ||
        (type === "pack" && !/^[A-Za-z0-9_-]{1,128}$/.test(rewardId))) {
      droppedRows++;
      continue;
    }

    const slotIndex = Number(row.slotIndex);
    if (!Number.isInteger(slotIndex) || slotIndex < 0 || slotIndex >= ROULETTE_SLOT_COUNT) {
      droppedRows++;
      continue;
    }

    // 같은 자리를 두 행이 주장하면 id 낮은 쪽을 남긴다 — 행 순서가 곧 저작 순서다.
    if (taken.has(slotIndex)) {
      droppedRows++;
      continue;
    }
    taken.add(slotIndex);

    slots.push({slotIndex, rewardType: type === "pack" ? "Pack" : "Currency",
      rewardId, currency, amount, weight: Number(row.weight)});
  }

  return {
    rouletteId: String(header.rouletteId),
    priceType,
    price,
    slots,
    droppedRows,
  };
}

/**
 * 칸 하나를 가중치로 고른다. 비복원 제거가 없어 후보 목록이 필요 없다.
 * @param {RouletteSlot[]} slots 판의 칸 목록
 * @param {RollFn} roll 난수원
 * @return {RouletteSlot | null} 뽑힌 칸. 칸이 없으면 null
 */
export function drawRouletteSlot(slots: RouletteSlot[], roll: RollFn): RouletteSlot | null {
  if (slots.length === 0) return null;

  let sum = 0;
  for (const slot of slots) sum += effectiveWeight(slot.weight);

  // 유효 가중치가 최소 1이라 구조상 도달하지 않는다.
  if (sum <= 0) return slots[slots.length - 1];

  let remaining = roll(sum);
  for (const slot of slots) {
    remaining -= effectiveWeight(slot.weight);
    if (remaining < 0) return slot;
  }
  return slots[slots.length - 1];
}

/**
 * 배포 전에 발행된 재화 전용 영수증만 허용한다. 새 슬롯 영수증의 검증은 우회하지 않는다.
 * @param {unknown} value 저장된 응답
 * @return {boolean} 검증된 옛 재화 영수증인가
 */
export function isLegacyRouletteReceipt(value: unknown): boolean {
  if (!value || typeof value !== "object") return false;
  const body = value as Record<string, unknown>;
  const gain = body.gain as {currency?: string; amount?: number} | undefined;
  const wallet = body.wallet as {rev?: number; balances?: Record<string, unknown>} | undefined;
  return body.revision === undefined && body.slotKeys === undefined && body.updatedSlots === undefined &&
    body.rewardType === undefined && body.cards === undefined &&
    typeof body.rouletteId === "string" && body.rouletteId.length > 0 &&
    Number.isInteger(body.slotIndex) && Number(body.slotIndex) >= 0 && Number(body.slotIndex) < ROULETTE_SLOT_COUNT &&
    !!gain && CURRENCY_KEYS.includes(gain.currency as CurrencyKey) && gain.currency !== ROULETTE_TICKET_KEY &&
    Number.isSafeInteger(gain.amount) && Number(gain.amount) > 0 &&
    !!wallet && Number.isSafeInteger(wallet.rev) && Number(wallet.rev) >= 0 &&
    !!wallet.balances && CURRENCY_KEYS.every((key) =>
    Number.isSafeInteger(wallet.balances![key]) && Number(wallet.balances![key]) >= 0);
}
