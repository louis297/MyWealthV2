import { rejectReasonLabel } from "@/features/withdrawals/withdrawalLabels";
import { formatAmount } from "@/shared/format/money";

/** D76: the status labels of a trade settlement. */
export function tradeSettlementStatusLabel(status: string) {
  switch (status) {
    case "AwaitingPayment":
      return "Paying the broker";
    case "AwaitingReceipt":
      return "Waiting for the broker's payment";
    case "PaymentFailed":
      return "Payment failed";
    case "Disputed":
      return "Figures differ";
    case "SetAside":
      return "Set aside";
    default:
      return status;
  }
}

/** D76: the filter values of the Trade settlements page; Open reads `open=true`, All sends nothing. */
export const tradeSettlementFilters: Record<string, { label: string; status?: string; open?: boolean }> = {
  Open: { label: "Open", open: true },
  AwaitingPayment: { label: "Paying the broker", status: "AwaitingPayment" },
  AwaitingReceipt: { label: "Waiting for the broker's payment", status: "AwaitingReceipt" },
  PaymentFailed: { label: "Payment failed", status: "PaymentFailed" },
  Disputed: { label: "Figures differ", status: "Disputed" },
  Completed: { label: "Completed", status: "Completed" },
  SetAside: { label: "Set aside", status: "SetAside" },
  All: { label: "All" },
};

/** D76: "Pay broker 1,001.50" for a net below 0, "Receive 2,340.00" above it. */
export function netLabel(netAmount: number, places = 2) {
  if (netAmount === 0) {
    return "Nothing to pay";
  }
  return netAmount < 0
    ? `Pay broker ${formatAmount(-netAmount, places)}`
    : `Receive ${formatAmount(netAmount, places)}`;
}

/** D76: why a settlement payment failed. */
export function failureLabel(failure: string | null, detail: string | null, currency: string) {
  switch (failure) {
    case "PaymentRejected":
      return `The bank refused the payment: ${rejectReasonLabel(detail)}`;
    case "PaymentReturned":
      return `The bank returned the payment: ${detail ?? "no reason given"}`;
    case "NoBrokerAccount":
      return `The broker connection has no bank account in ${currency}`;
    default:
      return failure ?? "";
  }
}

/** D46: the status labels of a payment the platform asks the bank to make. */
export function paymentStatusLabel(status: string) {
  switch (status) {
    case "Pending":
      return "Waiting to send";
    case "Accepted":
      return "Sent to the bank";
    case "Rejected":
      return "Refused by the bank";
    default:
      return status;
  }
}

export const setAsideSentence =
  "The platform stops waiting for this settlement. Its trades are then shown in the bank reconciliation as not settled through the trust account.";
