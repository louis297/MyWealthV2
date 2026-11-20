/**
 * The portal's copy of the manual entry matrix (custody-modes R5, adviser-portal-dashboard D18). The server
 * still decides: when it refuses a type, the form shows the sentence of the 400.
 */
const refusedOnCustodial: ReadonlySet<string> = new Set([
  // D39 (deposit-matching R35): deposits come from the bank statement.
  "TransferIn",
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
