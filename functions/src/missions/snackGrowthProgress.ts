import {EVENTS} from "../analytics/eventNames";
import type {DrawnCard} from "../packs/packDraw";
import {applyMissionIncrement, MissionBump} from "./missionStore";

/**
 * 자동 한계돌파도 수동 명령과 동일하게 단계마다 1회를 센다. 영수증 콜백 안에서만 호출한다.
 * @param {MissionBump} missions 이미 읽은 미션 상태
 * @param {DrawnCard[]} cards 이번 지급 결과
 */
export function applySnackGrowthProgress(missions: MissionBump, cards: DrawnCard[]): void {
  const count = cards.reduce((sum, card) => sum +
    (card.snackGrowth ? card.snackGrowth.toStage - card.snackGrowth.fromStage : 0), 0);
  if (count > 0) applyMissionIncrement(missions, EVENTS.cardLimitBreakCompleted.missionKey, count);
}
