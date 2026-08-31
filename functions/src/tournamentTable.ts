// TournamentChapter 표 해석 — 챕터 완주의 **모수**와 정점 해금의 **사슬**을 함께 낸다.
// 완성 판정 자체(isCompleted)는 도감과 공유하므로 completionTable.ts 에 남는다.
//
// 순수 모듈 제약: firebase-admin · HttpsError 를 들이지 마라. functions/scripts 의 회귀가
// lib/ 를 직접 require 하고 돈다.

/**
 * TournamentChapter 시트 한 줄. 컬럼 이름을 그대로 쓴다
 * (id | chapterId | nodeId | order | prevNodeId | requiredPoints).
 */
export type ChapterNodeRow = {
  id: number;
  chapterId: string;
  nodeId: string;
  order: number;
  /** 전역 직전 정점. 사슬의 첫 정점만 빈 문자열이고 챕터 경계에서 끊기지 않는다. */
  prevNodeId: string;
  /** 이 정점이 속한 챕터의 랭크 잠금(rank.points 기준). 첫 등급은 0 이다. */
  requiredPoints: number;
};

/** 정점 해금 판정 결과. 거절 사유는 **와이어 계약**이라 클라가 문자열을 그대로 대조한다. */
export type NodeUnlockVerdict =
  | {ok: true}
  | {ok: false; reason: NodeUnlockReject};

/** 해금 거절 사유. */
export type NodeUnlockReject =
  | "NodeNotFound"
  | "ChainBlocked"
  | "RankLocked"
  | "ChainUnreadable";

/**
 * 정수로 읽고 못 읽으면 0. completionTable.looseInteger 와 같은 규약이다.
 * @param {unknown} value 표 값
 * @return {number} 정수
 */
function looseInteger(value: unknown): number {
  const numeric = Number(value ?? 0);
  return Number.isFinite(numeric) ? Math.trunc(numeric) : 0;
}

/**
 * TournamentChapter 표를 읽는다. 챕터·정점 키가 빈 줄은 버린다.
 *
 * prevNodeId · requiredPoints 는 없으면 ""·0 으로 관대하게 읽는다 — 이 두 열이 없던
 * 구 블롭에서도 챕터 완주 수령(chapterNodeIds)은 계속 서야 한다. 사슬을 잴 수 없다는
 * 판정은 파서가 아니라 judgeNodeUnlock 이 fail-closed 로 내린다.
 * @param {Record<string, unknown>[]} rows 표 전량
 * @return {ChapterNodeRow[]} 읽힌 줄만
 */
export function parseChapterNodeRows(rows: Record<string, unknown>[]): ChapterNodeRow[] {
  return rows
    .map((row) => ({
      id: looseInteger(row.id),
      chapterId: String(row.chapterId ?? "").trim(),
      nodeId: String(row.nodeId ?? "").trim(),
      order: looseInteger(row.order),
      prevNodeId: String(row.prevNodeId ?? "").trim(),
      requiredPoints: looseInteger(row.requiredPoints),
    }))
    .filter((row) => row.chapterId.length > 0 && row.nodeId.length > 0);
}

/** 정점·챕터 키의 최대 길이. claimReward 의 ownerId 상한과 같은 값이어야 한다. */
export const MAX_NODE_ID_LENGTH = 64;

/**
 * 세이브 슬롯의 문자열 id 목록을 안전하게 읽는다(중복·비문자열·과길이 제거).
 * 문서가 손상돼도 판정이 죽지 않게 관대하게 읽되, 길이 상한은 룰과 같은 값으로 지킨다.
 * @param {unknown} value 슬롯 필드
 * @return {string[]} 읽힌 id
 */
export function readNodeIdList(value: unknown): string[] {
  if (!Array.isArray(value)) return [];

  const seen = new Set<string>();
  for (const entry of value) {
    if (typeof entry !== "string") continue;
    if (entry.length === 0 || entry.length > MAX_NODE_ID_LENGTH) continue;
    seen.add(entry);
  }
  return [...seen];
}

/**
 * 그 챕터가 요구하는 정점 id(중복 제거, 표 순서 유지). 빈 배열은 "모수 없음"이다.
 * @param {ChapterNodeRow[]} rows TournamentChapter 표 전량
 * @param {string} chapterId 챕터 키
 * @return {string[]} 요구 정점 id
 */
export function chapterNodeIds(rows: ChapterNodeRow[], chapterId: string): string[] {
  const matched = rows.filter((row) => row.chapterId === chapterId);
  return [...new Set(matched.map((row) => row.nodeId))];
}

/**
 * 그 정점이 표에 있는가. 구 클라가 남긴 임의 낙인을 수령 단계에서 거르는 데 쓴다.
 * @param {ChapterNodeRow[]} rows TournamentChapter 표 전량
 * @param {string} nodeId 정점 키
 * @return {boolean} 표에 있으면 true
 */
export function hasNode(rows: ChapterNodeRow[], nodeId: string): boolean {
  return rows.some((row) => row.nodeId === nodeId);
}

/**
 * 정점에 지금 도전해 이겼다고 신고할 자격이 있는가 — 선행 사슬과 랭크 잠금을 함께 잰다.
 *
 * 사슬의 진실원은 prevNodeId 다. order 는 챕터 **안**의 순서라 챕터 경계를 넘지 못하고,
 * id 는 저작 순회 위치라 앞에 행 하나만 끼워도 밀린다.
 * @param {ChapterNodeRow[]} rows TournamentChapter 표 전량
 * @param {string} nodeId 신고 대상 정점
 * @param {ReadonlySet<string>} cleared 이미 클리어한 정점
 * @param {number} points 현재 rank.points
 * @return {NodeUnlockVerdict} 통과 여부와 사유
 */
export function judgeNodeUnlock(
  rows: ChapterNodeRow[],
  nodeId: string,
  cleared: ReadonlySet<string>,
  points: number,
): NodeUnlockVerdict {
  const target = rows.find((row) => row.nodeId === nodeId);
  if (target === undefined) return {ok: false, reason: "NodeNotFound"};

  if (target.requiredPoints > points) return {ok: false, reason: "RankLocked"};

  // 사슬의 시작은 전역에 딱 하나다. 둘 이상이면 prevNodeId 열이 없는 구 블롭이라는 뜻이고,
  // 그 상태를 "전부 첫 정점"으로 통과시키면 사슬 검사가 통째로 무력해진다 — fail-closed 로 막는다.
  const roots = rows.filter((row) => row.prevNodeId.length === 0);
  if (roots.length !== 1) return {ok: false, reason: "ChainUnreadable"};

  if (target.prevNodeId.length === 0) return {ok: true};

  if (!hasNode(rows, target.prevNodeId)) return {ok: false, reason: "ChainUnreadable"};
  if (!cleared.has(target.prevNodeId)) return {ok: false, reason: "ChainBlocked"};

  return {ok: true};
}
