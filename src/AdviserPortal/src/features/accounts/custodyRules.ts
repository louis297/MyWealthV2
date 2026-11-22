/**
 * The portal's copy of the manual entry matrix (custody-modes R5, adviser-portal-dashboard D18). The server
 * still decides: when it refuses a type, the form shows the sentence of the 400.
 */
const refusedOnCustodial: ReadonlySet<string> = new Set([
  // D39 (deposit-matching R35): deposits come from the bank statement.
  "TransferIn",
  // D44 (withdrawals R45): money leaves as a withdrawal to a nominated bank account.
  "TransferOut",
  // D53 (account-closure R23): the account closure writes off a remainder.
  "CloseOut",
  "Interest",
  "Opening",
  "OpeningHoldings",
  "StockIn",
  "StockOut",
  "Split",
  "Bonus",
  "Scrip",
]);

export function isCustodial(account: { custodyMode: string } | undefined | null): boolean {
  return account?.custodyMode === "Custodial";
}

export function allowsManualEntry(custodyMode: string, type: string): boolean {
  return custodyMode !== "Custodial" || !refusedOnCustodial.has(type);
}

/** D19: shown where the "Booked at" input is on an External account. */
export const bookedWhenPosted = "Booked when posted.";

/** D20: a transfer or conversion needs both Accounts in the same custody mode (custody-modes R8). */
export const differentCustodyMode = "Different custody mode";

/** One minor unit of a currency: 0.01 for two decimals, 1 for none. */
export function minorUnit(decimalPlaces: number | undefined) {
  return 10 ** -(decimalPlaces ?? 2);
}

/**
 * Account closure R7: the payout (the cash balance rounded down to the minor unit) and the remainder below it.
 * Ledger amounts have at most 4 decimals, so the split is done in units of 0.0001 to avoid binary float drift.
 */
export function closureAmounts(cashBalance: number, decimalPlaces: number | undefined) {
  const places = Math.min(decimalPlaces ?? 2, 4);
  const units = Math.round(cashBalance * 10000);
  const step = 10 ** (4 - places);
  const payoutUnits = Math.floor(units / step) * step;
  return { payout: payoutUnits / 10000, remainder: (units - payoutUnits) / 10000 };
}

/** D44 (withdrawals R47): a Custodial account of another Customer is not a transfer destination. */
export const anotherCustomer = "Another customer";
