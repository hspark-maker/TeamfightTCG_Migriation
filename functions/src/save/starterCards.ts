import * as logger from "firebase-functions/logger";
import {readSpecRows} from "../specs/specBlobReader";
import {
  DropRow,
  FALLBACK_STARTER_CARD_IDS,
  FRESH_ACCOUNT_GRADE,
  resolveStarterCardsFromRows,
  STARTER_PACK_ID,
} from "./starterPool";

/** 카드 카탈로그가 될 수 있는 표. 클라 SpecSource 가 ContentProfile 의 RunMode 로 하나를 고르는데,
 * 서버는 그 선택을 알 수 없어 합집합으로 본다 — 여기서 걸러야 할 것은 "어느 표에도 없는 카드"다. */
const CARD_TABLES = ["Card", "Card_Test"];

/** 스타터 카드가 어디서 나왔는가. 로그·응답에 실어 사후에 갈래를 판별한다. */
export type StarterSource = "spec" | "fallback" | "specError";

/**
 * 카드 카탈로그의 id 집합. 행 문서 id 가 곧 카드 id 다(업로더가 id 열로 문서를 만든다).
 * @param {string} env 환경 id
 * @return {Promise<Set<number>>} 카탈로그에 있는 카드 id
 */
async function readKnownCardIds(env: string): Promise<Set<number>> {
  const ids = new Set<number>();

  for (const table of CARD_TABLES) {
    for (const row of await readSpecRows(env, table)) {
      const id = Number(row.id);
      if (Number.isInteger(id) && id > 0) ids.add(id);
    }
  }

  return ids;
}

/**
 * 스펙 표에서 스타터 카드를 읽는다. 표가 없거나 읽지 못해도 계정 생성을 막지 않는다.
 *
 * 클라 BattleContentSync 는 meta 문서의 rowCount·payloadHash 로 표 무결성을 대조하고 어긋나면
 * 통째로 거부하는데, 여기서는 rows 를 직접 읽어 그 검사를 건너뛴다. 업로드가 중간에 끊긴 표로
 * 만들어진 계정만 다른 스타터를 갖게 된다 — 카드 존재 검사가 그 피해를 덱 무효화까지는 가지
 * 않게 막지만, 무결성 대조까지 옮기는 것은 R3(스펙 서버화)의 몫이다.
 * @param {string} env 환경 id
 * @return {Promise<{cardIds: number[], source: StarterSource}>} 카드 목록과 출처
 */
export async function resolveStarterCardIds(
  env: string,
): Promise<{cardIds: number[]; source: StarterSource}> {
  try {
    // 표를 블롭으로 통째 읽고 packId 는 메모리에서 거른다 — where 질의도 맞는 행 수만큼 과금되고,
    // CardPackDrop 은 300행이 넘어 계정 생성 1건이 수백 읽기가 됐다. 정렬은 리더가 id 숫자로 한다.
    const rows: DropRow[] = (await readSpecRows(env, "CardPackDrop"))
      .filter((row) => String(row.packId ?? "") === STARTER_PACK_ID)
      .map((row) => ({
        id: Number(row.id),
        minGrade: String(row.minGrade ?? ""),
        cardId: Number(row.cardId),
      }));

    // 카탈로그를 못 읽으면 존재 검사 없이 뽑는 대신 폴백으로 간다 — 검증 없이 지급하면
    // 카탈로그에 없는 카드가 덱에 굳어 클라가 덱 0개로 부팅되고 복구 경로가 없다.
    const knownCardIds = rows.length > 0 ? await readKnownCardIds(env) : new Set<number>();

    const cardIds = knownCardIds.size > 0 ?
      resolveStarterCardsFromRows(rows, FRESH_ACCOUNT_GRADE, knownCardIds) :
      [];

    if (cardIds.length > 0) return {cardIds, source: "spec"};

    logger.info("starter cards fell back to the built-in list", {
      env,
      rowCount: rows.length,
      knownCardCount: knownCardIds.size,
    });
    return {cardIds: [...FALLBACK_STARTER_CARD_IDS], source: "fallback"};
  } catch (error) {
    // 스펙을 못 읽는 것이 계정을 못 만들 이유는 아니다 — 어느 갈래였는지만 남기고 폴백으로 간다.
    logger.error("starter card spec read failed", {
      env,
      message: error instanceof Error ? error.message : String(error),
    });
    return {cardIds: [...FALLBACK_STARTER_CARD_IDS], source: "specError"};
  }
}
