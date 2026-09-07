/**
 * 룰렛 판정에 필요한 스펙 표 읽기. 표 자체는 `../specs/specBlobReader` 가 블롭 문서 1개로 읽는다.
 *
 * **`packs/packSpecReader` 를 타지 않는다** — 재수출을 빌리는 순간 룰렛이 팩에 묶인다.
 * 여기서 하는 일은 Roulette · RouletteSlot 두 표를 룰렛 어휘로 옮기는 것뿐이고,
 * 읽기·캐시·무결성 대조는 리더의 몫이다.
 */

import {RouletteHeaderRow, RouletteSlotRow} from "./rouletteDraw";
import {readSpecRows} from "../specs/specBlobReader";

/** rouletteId 최대 길이. 요청 인자 관문용 상한이라 시트 저작이 이보다 길면 그 판은 돌릴 수 없다. */
export const MAX_ROULETTE_ID_LENGTH = 64;

/** 판 헤더 표 이름(판 하나 = 한 행). */
const ROULETTE_TABLE = "Roulette";

/** 칸 표 이름(판 하나에 칸 여러 행). */
const ROULETTE_SLOT_TABLE = "RouletteSlot";

/**
 * 이 판의 헤더 한 행. 표를 통째로 읽어 캐시한 뒤 메모리에서 거른다
 * — rouletteId 마다 where 질의를 던지면 캐시가 판 수만큼 갈라진다.
 *
 * 모든 열을 String/Number 로 감싸는 이유는 리더의 coerce 다 — 숫자만 든 문자열 열이 number 로 온다.
 * 같은 키가 두 행이면 id 낮은 쪽을 쓴다(리더가 id 오름차순으로 세워 준다).
 * @param {string} env 환경 id
 * @param {string} rouletteId 판 키
 * @return {Promise<RouletteHeaderRow | null>} 헤더 행. 그 판이 표에 없으면 null
 */
export async function readRouletteHeaderRow(
  env: string, rouletteId: string
): Promise<RouletteHeaderRow | null> {
  const rows = await readSpecRows(env, ROULETTE_TABLE);
  const found = rows.find((row) => String(row.rouletteId ?? "") === rouletteId);
  if (found === undefined) return null;

  return {
    id: Number(found.id),
    rouletteId,
    displayName: String(found.displayName ?? ""),
    priceType: String(found.priceType ?? ""),
    price: Number(found.price ?? 0),
    sortOrder: Number(found.sortOrder ?? 0),
  };
}

/**
 * 이 판의 칸 행. 헤더와 같은 이유로 표를 통째로 읽어 메모리에서 거른다.
 * 정렬은 하지 않는다(리더가 id 오름차순으로 세워 준다). `#memo` 열은 데이터가 아니라 읽지 않는다.
 * @param {string} env 환경 id
 * @param {string} rouletteId 판 키
 * @return {Promise<RouletteSlotRow[]>} id 오름차순 칸 행
 */
export async function readRouletteSlotRows(
  env: string, rouletteId: string
): Promise<RouletteSlotRow[]> {
  const rows = await readSpecRows(env, ROULETTE_SLOT_TABLE);
  return rows
    .filter((row) => String(row.rouletteId ?? "") === rouletteId)
    .map((row) => ({
      id: Number(row.id),
      rouletteId,
      slotIndex: Number(row.slotIndex ?? -1),
      rewardType: String(row.rewardType ?? ""),
      rewardId: String(row.rewardId ?? ""),
      amount: Number(row.amount ?? 0),
      weight: Number(row.weight ?? 0),
    }));
}
