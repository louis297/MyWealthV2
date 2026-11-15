import type { TradeFeeSchedule } from "@/features/fees/feesApi";

export type ScheduleState = "Applies today" | "Earlier" | "Scheduled" | "Scheduled, locked";

/** adviser-portal-fees State table. `today` is today's UTC date (`yyyy-MM-dd`). */
export function scheduleStates(_items: TradeFeeSchedule[], _today: string): Map<string, ScheduleState> {
  return new Map();
}

/** Today's UTC date as `yyyy-MM-dd`. */
export function todayUtc(_now: Date = new Date()): string {
  return "";
}
