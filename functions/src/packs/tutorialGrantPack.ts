// 튜토리얼 무료 팩 지급 판정. 무엇을 줄지는 CardPackDrop 풀 **전량**이 정하고,
// 줄 자격은 CardPack 행의 price 하나가 가른다 — 추첨이 아니라 확정 지급이라 drawCount·weight 를 보지 않는다.
//
// 순수 모듈 제약: firebase-admin · HttpsError 를 들이지 마라. functions/scripts 의 회귀가
// lib/ 를 직접 require 하고 돈다.

import type {DropRow} from "./packDraw";
import type {CardPackRow} from "./packSpecReader";

/**
 * price 셀이 유한한 수로 읽히는가. 빈 셀 · 수가 아닌 값은 false 다.
 * `Number("")` 가 0 이라 이 구분이 없으면 결손이 무료로 통과한다.
 * @param {unknown} raw CardPack.price 원본 값
 * @return {boolean} 읽혔으면 true
 */
export function isPriceAuthored(raw: unknown): boolean {
  if (raw === undefined || raw === null) return false;
  if (String(raw).trim() === "") return false;
  return Number.isFinite(Number(raw));
}

/** 판정 결과. reason 은 와이어 계약이라 문자열을 바꾸면 클라 대조가 깨진다. */
export type TutorialGrantVerdict =
  | {ok: true; cardIds: number[]}
  | {ok: false; reason: "GrantNotFound" | "GrantNotAllowed"};

/**
 * 이 팩이 지급하는 카드 id(드롭 행 id 오름차순, 중복 제거). 못 읽는 id 와 카탈로그 밖 카드는 버린다
 * — 존재하지 않는 카드가 소유 목록에 붙으면 클라에서 되돌릴 방법이 없다.
 * 카탈로그 대조는 openPack 의 drawPack 이 쓰는 집합(cardCatalog.loadCatalogIds)과 같은 것이어야 한다.
 * @param {DropRow[]} rows 이 팩의 CardPackDrop 행
 * @param {ReadonlySet<number>} catalogIds 이 env 에서 노출되는 카드 id
 * @return {number[]} 지급 카드 id
 */
export function packGrantCardIds(rows: DropRow[], catalogIds: ReadonlySet<number>): number[] {
  const seen = new Set<number>();
  const cardIds: number[] = [];
  for (const row of rows) {
    const cardId = Number(row.cardId);
    if (!Number.isInteger(cardId) || cardId <= 0 || seen.has(cardId)) continue;
    if (!catalogIds.has(cardId)) continue;
    seen.add(cardId);
    cardIds.push(cardId);
  }
  return cardIds;
}

/**
 * 튜토리얼 지급 판정. 줄 카드가 없거나 CardPack 에 그 행이 없으면 GrantNotFound,
 * 무료가 아니면 GrantNotAllowed — 유료 packId 를 넣어 공짜로 받는 길을 막는다.
 *
 * price 셀을 못 읽은 팩도 유료로 본다. 이 판정이 지급 경로의 유일한 권한 게이트라
 * 표 결손 앞에서 열리면 상점 팩이 통째로 공짜가 된다.
 * @param {CardPackRow | null} packRow CardPack 행(없으면 null)
 * @param {DropRow[]} dropRows 이 팩의 CardPackDrop 행
 * @param {ReadonlySet<number>} catalogIds 이 env 에서 노출되는 카드 id
 * @return {TutorialGrantVerdict} 지급 카드 또는 거절 사유
 */
export function judgeTutorialGrant(
  packRow: CardPackRow | null,
  dropRows: DropRow[],
  catalogIds: ReadonlySet<number>,
): TutorialGrantVerdict {
  const cardIds = packGrantCardIds(dropRows, catalogIds);
  if (cardIds.length === 0) return {ok: false, reason: "GrantNotFound"};
  if (packRow === null) return {ok: false, reason: "GrantNotFound"};
  if (!packRow.priceAuthored || packRow.price !== 0) return {ok: false, reason: "GrantNotAllowed"};

  return {ok: true, cardIds};
}
