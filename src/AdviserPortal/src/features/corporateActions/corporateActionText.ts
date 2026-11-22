/** D64: the label of a corporate action kind. */
export function kindLabel(kind: string) {
  switch (kind) {
    case "Split":
      return "Share split";
    case "Consolidation":
      return "Share consolidation";
    case "Bonus":
      return "Bonus issue";
    default:
      return kind;
  }
}

/** D64: "2 for 1". */
export function ratioText(item: { ratioNew: number; ratioHeld: number }) {
  return `${item.ratioNew} for ${item.ratioHeld}`;
}

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
