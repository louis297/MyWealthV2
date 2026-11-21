import type { BankReconciliationListItem } from "@/features/bankReconciliations/bankReconciliationsApi";

/** D51: the label of a run's status, or of an account without a run. */
export function runStatusLabel(run: Pick<BankReconciliationListItem, "status" | "totals"> | null) {
  if (!run) {
    return "Not run yet";
  }
  switch (run.status) {
    case "Breaks":
      return `${run.totals.breaks.count} breaks`;
    case "NoBankBalance":
      return "No bank balance yet";
    default:
      return run.status;
  }
}

/** D51: the label of each item kind. */
export function kindLabel(kind: string) {
  switch (kind) {
    case "Unexplained":
      return "Not explained";
    case "StatementDifference":
      return "Bank balance differs from the statement lines";
    case "Suspense":
      return "In suspense";
    case "NotProcessed":
      return "Not processed yet";
    case "ReturnInProgress":
      return "Being returned to the payer";
    case "OpeningBalance":
      return "Balance before the first statement line";
    case "Trades":
      return "Trades not settled through the trust account";
    case "FxConversions":
      return "FX conversions not settled through the trust account";
    case "OtherLedgerCash":
      return "Other cash entered on the platform";
    case "OtherClientMoneyAccount":
      return "Cash from another client money account";
    default:
      return kind;
  }
}

/** D51: the item groups, in the order of R13. */
export const itemClasses = [
  { value: "Break", heading: "Breaks", total: "breaks" },
  { value: "Timing", heading: "Timing", total: "timing" },
  { value: "Known", heading: "Known differences", total: "known" },
] as const;

export const unexplainedSentence = "This should not happen. Report it to the platform team.";

export const inFlightSentence = "Payments sent to the bank appear on both sides when the statement shows them.";
