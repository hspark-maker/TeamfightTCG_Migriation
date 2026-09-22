const DAY = 86_400_000;

export type DateRange = { startDate: string; endDate: string };
export type ChartUnit = "day" | "week" | "month";
export type DailyCount = {
  day: string;
  exists: boolean;
  settled: number | null;
};

export const utcDay = (value: number) =>
  new Date(value).toISOString().slice(0, 10);

export function presetRange(
  preset: "yesterday" | "week" | "month",
  now = Date.now(),
): DateRange {
  const today = new Date(`${utcDay(now)}T00:00:00Z`);
  const endDate = utcDay(today.getTime());
  if (preset === "yesterday") {
    const yesterday = utcDay(today.getTime() - DAY);
    return { startDate: yesterday, endDate: yesterday };
  }
  if (preset === "week")
    today.setUTCDate(today.getUTCDate() - ((today.getUTCDay() + 6) % 7));
  else today.setUTCDate(1);
  return { startDate: utcDay(today.getTime()), endDate };
}

export function rangeError(range: DateRange, now = Date.now()): string | null {
  for (const value of [range.startDate, range.endDate]) {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value))
      return "시작일과 종료일을 입력하세요.";
    const parsed = Date.parse(`${value}T00:00:00Z`);
    if (!Number.isFinite(parsed) || utcDay(parsed) !== value)
      return "올바른 날짜를 입력하세요.";
  }
  if (range.startDate > range.endDate)
    return "시작일은 종료일보다 늦을 수 없습니다.";
  if (range.endDate > utcDay(now)) return "미래 날짜는 조회할 수 없습니다.";
  if ((Date.parse(range.endDate) - Date.parse(range.startDate)) / DAY + 1 > 90)
    return "한 번에 최대 90일까지 조회할 수 있습니다.";
  return null;
}

export function groupDaily(daily: DailyCount[], unit: ChartUnit) {
  const groups = new Map<
    string,
    {
      key: string;
      startDate: string;
      endDate: string;
      knownDays: number;
      expectedDays: number;
      settled: number | null;
    }
  >();
  for (const day of [...daily].sort((a, b) => a.day.localeCompare(b.day))) {
    let key = day.day;
    if (unit === "month") key = day.day.slice(0, 7);
    if (unit === "week") {
      const date = new Date(`${day.day}T00:00:00Z`);
      date.setUTCDate(date.getUTCDate() - ((date.getUTCDay() + 6) % 7));
      key = utcDay(date.getTime());
    }
    const group = groups.get(key) ?? {
      key,
      startDate: day.day,
      endDate: day.day,
      knownDays: 0,
      expectedDays: 0,
      settled: null,
    };
    group.endDate = day.day;
    group.expectedDays++;
    if (day.exists && day.settled !== null) {
      group.knownDays++;
      group.settled = (group.settled ?? 0) + day.settled;
    }
    groups.set(key, group);
  }
  return [...groups.values()];
}
