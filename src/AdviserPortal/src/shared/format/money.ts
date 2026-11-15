/** "1,250.00 NZD". Shows at least `decimalPlaces` decimals and never rounds an exact value away. */
export function formatMoney(value: number, currency: string, decimalPlaces?: number): string {
  return `${formatAmount(value, decimalPlaces)} ${currency}`;
}

/** "1,250.00": the number part of `formatMoney`. Unknown places → the value as sent, grouped. */
export function formatAmount(value: number, decimalPlaces?: number): string {
  // 10 fraction digits cover a 4-decimal amount times a 4-decimal percent, and trim float noise.
  return new Intl.NumberFormat("en-US", {
    minimumFractionDigits: decimalPlaces ?? 0,
    maximumFractionDigits: 10,
  }).format(value);
}

/** Adds ledger amounts (at most 4 decimals) without binary float drift. */
export function sumAmounts(amounts: number[]): number {
  return amounts.reduce((total, amount) => total + Math.round(amount * 10000), 0) / 10000;
}
