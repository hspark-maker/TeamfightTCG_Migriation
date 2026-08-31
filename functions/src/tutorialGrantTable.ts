// 튜토리얼 진행 단계가 지급하는 **카드 표** 해석. 무엇을 주는가만 정하고, 줄 자격은 보지 않는다
// — 소유는 집합이라 같은 단계를 두 번 불러도 늘어나는 것이 없다(그래서 낙인 문서가 없다).
//
// 순수 모듈 제약: firebase-admin · HttpsError 를 들이지 마라. functions/scripts 의 회귀가
// lib/ 를 직접 require 하고 돈다.

/** TutorialGrant 시트 한 줄. 컬럼 이름을 그대로 쓴다(id | stepId | cardId | order). */
export interface TutorialGrantRow {
  id: number;
  stepId: number;
  cardId: number;
  order: number;
}

/**
 * 정수로 읽고 못 읽으면 0. completionTable.looseInteger 와 같은 규약이다 —
 * 한 줄이 비었다고 표 전체 파싱이 죽으면 튜토리얼이 통째로 멈춘다.
 * @param {unknown} value 표 값
 * @return {number} 정수
 */
function looseInteger(value: unknown): number {
  const numeric = Number(value ?? 0);
  return Number.isFinite(numeric) ? Math.trunc(numeric) : 0;
}

/**
 * TutorialGrant 표를 읽는다. 단계 키나 카드 id 가 0 이하인 줄은 버린다
 * — 못 읽는 줄을 남기면 존재하지 않는 카드가 소유 목록에 들어간다.
 * @param {Record<string, unknown>[]} rows 표 전량
 * @return {TutorialGrantRow[]} 읽힌 줄만
 */
export function parseTutorialGrantRows(rows: Record<string, unknown>[]): TutorialGrantRow[] {
  return rows
    .map((row) => ({
      id: looseInteger(row.id),
      stepId: looseInteger(row.stepId),
      cardId: looseInteger(row.cardId),
      order: looseInteger(row.order),
    }))
    .filter((row) => row.stepId > 0 && row.cardId > 0);
}

/**
 * 그 단계가 지급하는 카드 id(order → id 순, 중복 제거). **빈 배열은 "저작 없음"이다** —
 * 거절할지는 부르는 쪽이 정한다(completionTable.isCompleted 와 같은 태도).
 * @param {TutorialGrantRow[]} rows TutorialGrant 표 전량
 * @param {number} stepId 튜토리얼 단계 id
 * @return {number[]} 지급 카드 id
 */
export function stepGrantCardIds(rows: TutorialGrantRow[], stepId: number): number[] {
  const matched = rows
    .filter((row) => row.stepId === stepId)
    .sort((a, b) => (a.order !== b.order ? a.order - b.order : a.id - b.id));

  return [...new Set(matched.map((row) => row.cardId))];
}
