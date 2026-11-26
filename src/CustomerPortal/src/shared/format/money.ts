/** "1,250.5 NZD": the value as sent, grouped. The Customer Portal has no currency catalog (bff-customer-portal R20). */
export function formatMoney(value: number, currency: string, decimalPlaces?: number): string {
  return `${formatAmount(value, decimalPlaces)} ${currency}`;
}

/** "1,250.5": the number part of `formatMoney`. Unknown places → the value as sent, grouped. */
export function formatAmount(value: number, decimalPlaces?: number): string {
  return new Intl.NumberFormat("en-US", {
    minimumFractionDigits: decimalPlaces ?? 0,
    maximumFractionDigits: 10,
  }).format(value);
}
