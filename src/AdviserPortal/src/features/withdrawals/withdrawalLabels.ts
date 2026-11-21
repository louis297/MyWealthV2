/** D46: the status labels of a withdrawal. */
export function withdrawalStatusLabel(status: string) {
  switch (status) {
    case "Requested":
      return "Waiting for approval";
    case "Approved":
      return "Approved, sending";
    case "Sent":
      return "Sent to bank";
    case "Failed":
      return "Bank refused";
    case "Returned":
      return "Returned by bank";
    default:
      return status;
  }
}

/** D46: the filter values of the Withdrawals page. All sends no status. */
export const withdrawalFilters: Record<string, { label: string; status?: string }> = {
  Requested: { label: "Waiting for approval", status: "Requested" },
  Approved: { label: "Approved", status: "Approved" },
  Sent: { label: "Sent", status: "Sent" },
  Paid: { label: "Paid", status: "Paid" },
  Rejected: { label: "Rejected", status: "Rejected" },
  Cancelled: { label: "Cancelled", status: "Cancelled" },
  Failed: { label: "Failed", status: "Failed" },
  Returned: { label: "Returned", status: "Returned" },
  All: { label: "All" },
};

/** D47: the label of a failed send attempt, as BO18 shows a bank feed failure. */
export function paymentFailureLabel(failure: string | null) {
  switch (failure) {
    case "Unreachable":
      return "Bank not reachable";
    case "Timeout":
      return "Bank did not answer in time";
    case "Unauthorized":
      return "Bank refused the key";
    case "Unavailable":
      return "Bank unavailable";
    case "UnknownAccount":
      return "Bank does not know this account";
    case "BadResponse":
      return "Bank answer not understood";
    default:
      return failure ?? "—";
  }
}

/** D47: why the bank refused a payment (withdrawals R34). */
export function rejectReasonLabel(reason: string | null) {
  switch (reason) {
    case "InvalidAccount":
      return "Account not valid";
    case "AccountClosed":
      return "Account closed";
    case "InsufficientFunds":
      return "Insufficient funds";
    case "Refused":
      return "Refused by the bank";
    default:
      return reason ?? "—";
  }
}

/** The payee of a withdrawal as one line: bank and masked number. */
export function payeeLabel(payee: { bankName: string; accountNumber: string }) {
  return `${payee.bankName} ${payee.accountNumber}`;
}
