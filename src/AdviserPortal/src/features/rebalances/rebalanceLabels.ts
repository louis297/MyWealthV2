import type { Rebalance, RebalanceLine } from "@/features/rebalances/rebalancesApi";

/** P17: the status labels of a rebalance. */
export function rebalanceStatusLabel(status: string) {
  switch (status) {
    case "Proposed":
      return "Waiting for confirmation";
    case "Incomplete":
      return "Ended early";
    case "NoChange":
      return "Nothing to trade";
    case "Failed":
      return "Could not be planned";
    default:
      return status;
  }
}

/** P17: the status of a rebalance with what explains it: a cancel request, why it was cancelled. */
export function rebalanceStatusText(rebalance: Pick<Rebalance, "status" | "cancelRequest" | "cancelReason">) {
  if (rebalance.cancelRequest && (rebalance.status === "Selling" || rebalance.status === "Buying")) {
    return "Cancelling";
  }
  if (rebalance.status === "Cancelled") {
    switch (rebalance.cancelReason) {
      case "Superseded":
        return "Cancelled: replaced by a newer proposal";
      case "Unassigned":
        return "Cancelled: the portfolio was unassigned";
      default:
        return "Cancelled";
    }
  }
  return rebalanceStatusLabel(rebalance.status);
}

/** P19: what started a rebalance. */
export function rebalanceTriggerLabel(trigger: string) {
  switch (trigger) {
    case "Assign":
      return "Assignment";
    case "Account":
      return "Request";
    case "Portfolio":
      return "Portfolio change";
    case "FollowUp":
      return "Follow-up";
    default:
      return trigger;
  }
}

/** rebalances R3: open while Proposed, Selling or Buying. */
export function isOpenRebalance(status: string) {
  return status === "Proposed" || status === "Selling" || status === "Buying";
}

/** rebalances R3: in progress while Selling or Buying. */
export function isRebalanceInProgress(status: string) {
  return status === "Selling" || status === "Buying";
}

/** P15: "Sell 40", "Buy 59.4", or "—" for a line the plan leaves alone. */
export function tradeLabel(line: Pick<RebalanceLine, "side" | "tradeQuantity">) {
  return line.side && line.tradeQuantity !== null ? `${line.side} ${Number(line.tradeQuantity)}` : "—";
}

/** P19: the filter values of the Rebalances page; Open by default. */
export const rebalanceFilters: Record<string, { label: string; status?: string; open?: boolean }> = {
  Open: { label: "Open", open: true },
  Proposed: { label: "Waiting for confirmation", status: "Proposed" },
  Incomplete: { label: "Ended early", status: "Incomplete" },
  All: { label: "All" },
};

/** P15: what the proposal page says under the lines. */
export const proposalSentence =
  "Sales are placed first. Purchases are placed when the sales are filled and are worked out again from the cash then, so the quantities bought may differ. The broker fills at the market price.";

/** P17: the line shown while a follow-up is owed. */
export const followUpSentence = "A new proposal follows when this one ends.";

/** P14: the answer to a rebalance with nothing to trade. */
export const noChangeSentence = "Nothing to trade. The account is in line with its portfolio.";
