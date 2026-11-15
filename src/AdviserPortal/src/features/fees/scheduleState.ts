import type { TradeFeeSchedule } from "@/features/fees/feesApi";

export type ScheduleState = "Applies today" | "Earlier" | "Scheduled" | "Scheduled, locked";

/**
 * adviser-portal-fees State table. `today` is today's UTC date (`yyyy-MM-dd`). Dates compare as strings
 * so they never go through `new Date`. The server decides what may change; the labels only explain it.
 */
export function scheduleStates(items: TradeFeeSchedule[], today: string): Map<string, ScheduleState> {
  const applying = new Map<string, TradeFeeSchedule>();
  for (const item of items) {
    if (item.effectiveFrom > today) {
      continue;
    }
    const current = applying.get(item.quoteCurrency);
    if (!current || item.effectiveFrom > current.effectiveFrom) {
      applying.set(item.quoteCurrency, item);
    }
  }

  const states = new Map<string, ScheduleState>();
  for (const item of items) {
    if (item.effectiveFrom > today) {
      states.set(item.id, item.inEffect ? "Scheduled, locked" : "Scheduled");
    } else {
      states.set(item.id, applying.get(item.quoteCurrency)?.id === item.id ? "Applies today" : "Earlier");
    }
  }
  return states;
}

/** Today's UTC date as `yyyy-MM-dd`. */
export function todayUtc(now: Date = new Date()): string {
  return now.toISOString().slice(0, 10);
}
