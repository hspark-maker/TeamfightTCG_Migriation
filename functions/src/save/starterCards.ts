import * as logger from "firebase-functions/logger";
import {HttpsError} from "firebase-functions/v2/https";
import {readSpecRows} from "../specs/specBlobReader";
import {
  DropRow,
  FRESH_ACCOUNT_GRADE,
  resolveStarterCardsFromRows,
  STARTER_PACK_ID,
} from "./starterPool";

/** 카드 카탈로그 표. 클라·서버 모두 Card 하나만 읽는다(Card_Test 표는 폐기). */
const CARD_TABLES = ["Card"];

/** 스타터 카드가 어디서 나왔는가. 로그·응답에 실어 사후에 갈래를 판별한다. */
export type StarterSource = "spec";

/**
 * 카드 카탈로그의 id와 등급. 최초 지급 성장값에도 같은 표를 쓴다.
 * @param {string} env 환경 id
 * @return {Promise<Map<number, string>>} 카드 id별 등급
 */
async function readKnownCardGrades(env: string): Promise<Map<number, string>> {
  const grades = new Map<number, string>();

  for (const table of CARD_TABLES) {
    for (const row of await readSpecRows(env, table)) {
      const id = Number(row.id);
      if (Number.isInteger(id) && id > 0) grades.set(id, String(row.grade));
    }
  }

  return grades;
}

/**
 * 스펙 표에서 스타터 카드를 읽는다. 읽기에 실패하면 재시도를 요청한다.
 * 코드에 남은 과거 희귀 카드 목록으로 계정을 만들지 않고 표를 유일한 지급 출처로 둔다.
 * @param {string} env 환경 id
 * @return {Promise<object>} 카드 목록, 등급과 출처
 */
export async function resolveStarterCardIds(
  env: string,
): Promise<{cardIds: number[]; source: StarterSource; grades: Map<number, string>}> {
  let grades = new Map<number, string>();
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

    // 카탈로그를 못 읽으면 계정 생성을 멈춘다 — 검증 없이 지급하면
    // 카탈로그에 없는 카드가 덱에 굳어 클라가 덱 0개로 초기화되고 복구 경로가 없다.
    grades = await readKnownCardGrades(env);
    const knownCardIds = new Set(grades.keys());

    const cardIds = knownCardIds.size > 0 ?
      resolveStarterCardsFromRows(rows, FRESH_ACCOUNT_GRADE, knownCardIds) :
      [];

    if (cardIds.length === 0) throw new Error("StarterPack does not contain a complete valid deck.");
    // 지급 구성과 등급은 표가 정한다. 우드혼처럼 희귀 카드가 저작돼도 정상 지급한다.
    return {cardIds, source: "spec", grades};
  } catch (error) {
    logger.error("starter card spec read failed", {
      env,
      message: error instanceof Error ? error.message : String(error),
    });
    throw new HttpsError("unavailable", "Starter card data is unavailable. Please retry.");
  }
}
