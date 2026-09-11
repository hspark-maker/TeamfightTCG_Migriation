import * as logger from "firebase-functions/logger";
import {readSpecRows} from "../packs/packSpecReader";
import {MissionDef} from "./catalog";
import {evaluateGuideProgress} from "./guideProgress";
import type {MissionBump} from "./missionStore";

/**
 * 가이드 병합용 표. 못 읽으면 기존 세이브 트리거가 처리하도록 명령 자체는 계속한다.
 * @param {string} env 환경 ID
 * @return {Promise<Array>} 카드 표 또는 빈 배열
 */
export async function readGuideCards(env: string): Promise<Record<string, unknown>[]> {
  try {
    const cards = await readSpecRows(env, "Card");
    if (cards.length > 0) return cards;
  } catch (error) {
    logger.warn("guide_inline_spec_unavailable", {env, error: String(error)});
    return [];
  }
  logger.warn("guide_inline_spec_empty", {env});
  return [];
}

/**
 * 갱신 후 세이브로 가이드를 계산해 이미 읽은 미션 상태에 합친다. DB I/O는 없다.
 * @param {MissionBump} bump 이번 트랜잭션의 미션 상태
 * @param {Record} current 기존 세이브
 * @param {Record} slots 갱신할 최상위 슬롯
 * @param {Array} cards 카드 표
 * @param {Array} catalog 미션 정의
 * @return {void}
 */
export function applyGuideProgress(
  bump: MissionBump, current: Record<string, unknown>, slots: Record<string, unknown>,
  cards: Record<string, unknown>[], catalog: readonly MissionDef[],
): void {
  if (cards.length === 0) return;
  bump.state.progress = evaluateGuideProgress({...current, ...slots}, cards, catalog, bump.state.progress);
}
