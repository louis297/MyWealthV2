/**
 * The portal's copy of the manual entry matrix (custody-modes R5, adviser-portal-dashboard D18). The server
 * still decides: when it refuses a type, the form shows the sentence of the 400.
 */
const refusedOnCustodial: ReadonlySet<string> = new Set([
  // D39 (deposit-matching R35): deposits come from the bank statement.
  "TransferIn",
  // D44 (withdrawals R45): money leaves as a withdrawal to a nominated bank account.
  "TransferOut",
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
 * D44 (withdrawals R46): on a Custodial account a CloseOut writes off only a remainder smaller than one minor unit.
 * The portal does not offer CloseOut on an External account.
 */
export function offersCloseOut(
  account: { custodyMode: string; status: string; cashBalance: number },
  decimalPlaces: number | undefined,
): boolean {
  return isCustodial(account)
    && account.status === "Open"
    && account.cashBalance > 0
    && account.cashBalance < minorUnit(decimalPlaces);
}

/** D44 (withdrawals R47): a Custodial account of another Customer is not a transfer destination. */
export const anotherCustomer = "Another customer";
