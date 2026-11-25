import { formatMoney } from "@/shared/format/money";

/** adjustment-review D103: the status labels of an adjustment request. */
export function adjustmentStatusLabel(status: string) {
  return status === "Requested" ? "Waiting for approval" : status;
}

/** D103: the filter values of the Adjustments page. All sends no status. */
export const adjustmentFilters: Record<string, { label: string; status?: string }> = {
  Requested: { label: "Waiting for approval", status: "Requested" },
  Approved: { label: "Approved", status: "Approved" },
  Rejected: { label: "Rejected", status: "Rejected" },
  Cancelled: { label: "Cancelled", status: "Cancelled" },
  All: { label: "All" },
};

/** A signed number as entered: "+10", "−0.60". */
function signed(value: number, places?: number) {
  const text = places === undefined ? String(Math.abs(value)) : Math.abs(value).toFixed(places);
  if (value > 0) {
    return `+${text}`;
  }
  if (value < 0) {
    return `−${text}`;
  }
  return text;
}

/** D103: "AAPL quantity +10, cost +250.00 USD", or "—" with no holding part. */
export function holdingChangeText(
  security: { instrument: { symbol: string }; quantityChange: number; costChange: number; costCurrency: string } | null,
  places = 2,
) {
  if (!security) {
    return "—";
  }
  return `${security.instrument.symbol} quantity ${signed(security.quantityChange)}, cost ${signed(security.costChange, places)} ${security.costCurrency}`;
}

/** D103: the cash part in the Account currency, or "—". */
export function cashChangeText(amount: number | null, currency: string, places?: number) {
  return amount === null ? "—" : formatMoney(amount, currency, places);
}

export const approvalSentence = "Another firm administrator must approve this adjustment before it is posted.";
export const requesterSentence = "Someone else must approve this adjustment.";
export const changedAdjustmentSentence = "This adjustment has changed.";
export const adjustmentsForbiddenSentence = "Only a firm administrator can see adjustments.";
export const customerSeesMemoSentence = "The customer sees this memo";
