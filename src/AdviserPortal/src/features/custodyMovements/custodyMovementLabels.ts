/** D90: why a custody movement is in suspense (in-kind-transfers R15). */
export function movementReasonLabel(reason: string | null) {
  switch (reason) {
    case "NoInstruction":
      return "No transfer was requested";
    case "UnknownInstruction":
      return "Names a transfer we do not hold";
    case "TransferNotOpen":
      return "The transfer is not open";
    case "TransferMismatch":
      return "Differs from the transfer";
    case "AccountClosed":
      return "The account is closed";
    case "InsufficientHolding":
      return "Not enough shares on the account";
    case "UnknownTrade":
      return "No such trade";
    case "TradeMismatch":
      return "Differs from the trade";
    case "UnknownCorporateAction":
      return "No such corporate action";
    case "UnsupportedKind":
      return "Kind not supported";
    default:
      return reason ?? "";
  }
}

/** D90: what the TenantAdmin does before ignoring, the row of the reason in in-kind-transfers §4.4. */
export function ignoreHint(reason: string | null) {
  switch (reason) {
    case "NoInstruction":
    case "UnknownInstruction":
      return "Finds whose they are. The bulk delivery of a migration batch's holdings needs nothing more. Otherwise an adjustment with reason Reconciliation on the customer's account, or the custodian sends them back.";
    case "TransferNotOpen":
    case "TransferMismatch":
    case "AccountClosed":
    case "InsufficientHolding":
      return "Sets the transfer aside and corrects the account with an adjustment, or has the custodian correct the movement.";
    case "UnknownTrade":
    case "TradeMismatch":
      return "Resolves it with the broker and the custodian; an adjustment when the ledger is the wrong side.";
    case "UnknownCorporateAction":
      return "Enters the corporate action by hand where the platform allows it, else adjusts the holdings with reason Corporate action.";
    case "UnsupportedKind":
      return "Reads the custodian's own statement and adjusts where needed.";
    default:
      return undefined;
  }
}

export function movementStatusLabel(status: string) {
  return status === "Unmatched" ? "In suspense" : status;
}

/** D90: the filter values of the Custody movements page. All sends no status. */
export const movementFilters: Record<string, { label: string; status?: string }> = {
  Unmatched: { label: "In suspense", status: "Unmatched" },
  Matched: { label: "Matched", status: "Matched" },
  Ignored: { label: "Ignored", status: "Ignored" },
  Received: { label: "Not processed yet", status: "Received" },
  All: { label: "All" },
};

export const movementsForbiddenSentence = "Only a firm administrator can see custody movements.";
