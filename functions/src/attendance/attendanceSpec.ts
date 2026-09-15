import {RewardGain, RewardItem, RewardRow, resolveRewards} from "../rewardTable";
import {ATTENDANCE_DAYS} from "./attendanceState";

export interface AttendanceDay {
  day: number;
  reward: {currencies: RewardGain[]; items: RewardItem[]};
}

/**
 * Fail closed when any day is missing or malformed, rather than consume an empty reward.
 * @param {RewardRow[]} rows Authored reward rows.
 * @return {AttendanceDay[]} Validated rewards for every attendance day.
 */
export function attendanceDays(rows: RewardRow[]): AttendanceDay[] {
  return Array.from({length: ATTENDANCE_DAYS}, (_, index) => {
    const day = index + 1;
    const reward = resolveRewards(rows, "Attendance", `day_${day}`);
    if (reward.dropped.length || reward.gains.length + reward.items.length === 0 ||
        reward.items.some((item) => item.rewardType === "PackChoice")) {
      throw new Error(`Attendance/day_${day} reward is missing or invalid.`);
    }
    return {day, reward: {currencies: reward.gains, items: reward.items}};
  });
}
