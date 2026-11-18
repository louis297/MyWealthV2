import type { FeeBand } from "@/features/fees/feesApi";
import { formatAmount } from "@/shared/format/money";

/** "0.5% to 10,000.00 · 0.3% above". Each band runs up to the next band's `from`. */
export function bandsText(bands: FeeBand[], decimalPlaces?: number): string {
  if (bands.length === 1) {
    return `${bands[0].ratePercent}% on any amount`;
  }
  return bands
    .map((band, index) => {
      const next = bands[index + 1];
      return next ? `${band.ratePercent}% to ${formatAmount(next.from, decimalPlaces)}` : `${band.ratePercent}% above`;
    })
    .join(" · ");
}

/** F20: trade fee schedules apply to trades on Custodial accounts only (external-caller-numbers R22). */
export const custodialOnlyLine =
  "Schedules apply to trades on Custodial accounts. A trade on an External account carries the fee entered with it.";
