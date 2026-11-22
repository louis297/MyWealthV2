import type { Order } from "@/features/orders/ordersApi";

/** D71: the status labels of an order. */
export function orderStatusLabel(status: string) {
  switch (status) {
    case "Placed":
      return "Sending to the broker";
    case "Working":
      return "At the broker";
    case "PartlyFilled":
      return "Partly filled";
    case "Rejected":
      return "Refused by the broker";
    default:
      return status;
  }
}

const openStatuses = ["Placed", "Working", "PartlyFilled"];

/** orders R5: an order the broker can still fill. */
export function isOpenOrder(order: Pick<Order, "status">) {
  return openStatuses.includes(order.status);
}

/** D73: Cancel is offered on an open order that has no cancel request yet. */
export function canCancelOrder(order: Pick<Order, "status" | "cancelRequest">) {
  return isOpenOrder(order) && !order.cancelRequest;
}

/** D71: the filter values of the Orders page. All sends no status; Needs attention reads `pendingFill=true`. */
export const orderFilters: Record<string, { label: string; status?: string; pendingFill?: boolean }> = {
  Open: { label: "Open", status: openStatuses.join(",") },
  Filled: { label: "Filled", status: "Filled" },
  Cancelled: { label: "Cancelled", status: "Cancelled" },
  Rejected: { label: "Rejected", status: "Rejected" },
  Expired: { label: "Expired", status: "Expired" },
  NeedsAttention: { label: "Needs attention", pendingFill: true },
  All: { label: "All" },
};

/** D72: why the broker refused an order (orders R21). */
export function orderRejectReasonLabel(reason: string | null) {
  switch (reason) {
    case "UnknownInstrument":
      return "The broker does not trade this instrument";
    case "InvalidOrder":
      return "The broker could not read the order";
    case "MarketClosed":
      return "The market is closed";
    case "Refused":
      return "Refused";
    default:
      return reason ?? "—";
  }
}

/** D72: why a fill waits to be booked (orders R36). */
export function fillHoldReasonLabel(reason: string | null) {
  switch (reason) {
    case "InsufficientCash":
      return "Not enough cash";
    case "InsufficientHolding":
      return "Not enough shares";
    case "AccountClosed":
      return "The account is closed";
    case "NoFxRate":
      return "No exchange rate";
    default:
      return reason ?? "—";
  }
}

/** D72: the label of a failed send attempt, as BO18 shows a feed failure. */
export function brokerFailureLabel(failure: string | null) {
  switch (failure) {
    case "Unreachable":
      return "Broker not reachable";
    case "Timeout":
      return "Broker did not answer in time";
    case "Unauthorized":
      return "Broker refused the key";
    case "Unavailable":
      return "Broker unavailable";
    case "BadResponse":
      return "Broker answer not understood";
    default:
      return failure ?? "—";
  }
}

const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** "25 Nov" from a `yyyy-MM-dd` date. */
export function shortDay(date: string) {
  const [, month, day] = date.slice(0, 10).split("-").map(Number);
  return month >= 1 && month <= 12 && day ? `${day} ${months[month - 1]}` : date;
}

/** D72, D74: "Settles 25 Nov" until the fill has settled, then "Settled 25 Nov". */
export function settlementLabel(settlementDate: string, settledAt: string | null | undefined) {
  return `${settledAt ? "Settled" : "Settles"} ${shortDay(settlementDate)}`;
}
