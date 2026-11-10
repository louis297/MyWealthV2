import type { TransactionFx } from "@/features/transactions/transactionsApi";

/** "1 NZD = 0.5912 USD (as of 2026-11-09)" (fx-conversions §9). */
export function rateLine(fx: TransactionFx) {
  return `1 ${fx.baseCurrency} = ${Number(fx.rate)} ${fx.quoteCurrency} (as of ${fx.rateAsOfDate})`;
}
