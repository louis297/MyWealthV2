/** D64: the label of a corporate action kind. */
export function kindLabel(kind: string) {
  switch (kind) {
    case "Split":
      return "Share split";
    case "Consolidation":
      return "Share consolidation";
    case "Bonus":
      return "Bonus issue";
    case "Dividend":
      return "Dividend";
    default:
      return kind;
  }
}

/** D64: "2 for 1"; D94: a dividend reads "0.72 USD per share". */
export function ratioText(item: { ratioNew: number | null; ratioHeld: number | null; dividend?: { amountPerShare: number; currency: string } | null }) {
  if (item.dividend) {
    return `${perShare(item.dividend.amountPerShare)} ${item.dividend.currency} per share`;
  }
  return `${item.ratioNew} for ${item.ratioHeld}`;
}

/** An amount per share with at least two decimals: "1.00", "0.2575". */
export function perShare(value: number) {
  return new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 8, useGrouping: false }).format(value);
}

/** D94: the status of a dividend, which waits for its payment once it went ex. */
export function dividendStatusLabel(status: string, paidAt: string | null | undefined) {
  switch (status) {
    case "Effective":
      return paidAt ? "Being paid" : "Waiting for payment";
    case "Applied":
      return "Paid";
    default:
      return statusLabel(status);
  }
}

/** D95: the status of the reinvestment election. */
export function electionLabel(status: string, reason: string | null) {
  switch (status) {
    case "Pending":
      return "To be sent to the custodian";
    case "Accepted":
      return "Accepted by the custodian";
    case "Rejected":
      return `Rejected: ${reason ?? ""}`;
    case "Lapsed":
      return "Not used";
    default:
      return "No reinvestment";
  }
}

/** D95: who the company is, for its tax. */
export function taxSourceLabel(source: string) {
  return source === "NewZealand" ? "New Zealand company" : "Foreign company";
}

export const dividendExDateHelp =
  "The customers who hold the shares when the first price from this date arrives are entitled. They are paid when you match the custodian's payment.";
export const payInCashSentence = "No new shares arrived for this dividend. Every customer is paid in cash.";

/** D65: the ratio as a sentence. */
export function ratioSentence(kind: string, ratioNew: number, ratioHeld: number) {
  return kind === "Bonus"
    ? `${ratioNew} new shares for every ${ratioHeld} held.`
    : `Every ${ratioHeld} shares become ${ratioNew}.`;
}

/** D64: where a corporate action came from. */
export function originLabel(origin: string) {
  return origin === "Feed" ? "Market data" : origin === "Manual" ? "Entered by hand" : origin;
}

/** D64: the status of a corporate action. */
export function statusLabel(status: string) {
  switch (status) {
    case "Announced":
      return "Announced";
    case "Effective":
      return "Being applied";
    case "Applied":
      return "Applied";
    case "Cancelled":
      return "Cancelled";
    default:
      return status;
  }
}

/** D65: the status of an entitlement, with the reason of one not applied. */
export function entitlementLabel(status: string, reason: string | null) {
  if (status === "Pending") {
    return "Waiting";
  }
  if (status === "Posted") {
    return "Done";
  }
  if (reason === "NoHolding") {
    return "No longer held";
  }
  if (reason === "TooSmall") {
    return "Holding too small to change";
  }
  if (reason === "NothingToPay") {
    return "Nothing to pay";
  }
  return status;
}

/** D65: "The platform" stands for a step the platform took. */
export function personName(person: { name: string } | null) {
  return person?.name ?? "The platform";
}

export const exDateHelp = "The change is applied when the first price from this date arrives.";
export const pastExDateWarning = "This date has passed. The change is applied to today's holdings. Check trades booked since then.";
export const applyNowConfirmation = "Apply this to every holding now? Use this only when no price arrives for the instrument.";
export const conflictMessage = "Someone else changed this corporate action.";
export const adviserScopeNote = "Only your customers' accounts are listed.";
export const fromMarketData = "From market data";

/** The platform's UTC date, `yyyy-MM-dd`. */
export function utcToday() {
  return new Date().toISOString().slice(0, 10);
}
