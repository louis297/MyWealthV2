/** D88: the status labels of an in-kind transfer. */
export function transferStatusLabel(status: string) {
  switch (status) {
    case "Requested":
      return "Waiting for approval";
    case "Approved":
      return "Sending to the custodian";
    case "Sent":
      return "With the custodian";
    case "Failed":
      return "Refused by the custodian";
    case "SetAside":
      return "Set aside";
    default:
      return status;
  }
}

/** D88: an open transfer is Requested, Approved or Sent. */
export const openTransferStatuses = "Requested,Approved,Sent";

/** D88: the filter values of the Transfers page. All sends no status. */
export const transferFilters: Record<string, { label: string; status?: string }> = {
  Open: { label: "Open", status: openTransferStatuses },
  Completed: { label: "Completed", status: "Completed" },
  Rejected: { label: "Rejected", status: "Rejected" },
  Cancelled: { label: "Cancelled", status: "Cancelled" },
  Failed: { label: "Failed", status: "Failed" },
  SetAside: { label: "Set aside", status: "SetAside" },
  All: { label: "All" },
};

/** D89: why the custodian refused a transfer (in-kind-transfers R35). */
export function transferFailureReasonLabel(reason: string | null) {
  switch (reason) {
    case "UnknownInstrument":
      return "The custodian does not hold this instrument";
    case "InsufficientPosition":
      return "The custodian does not hold enough";
    case "CounterpartyRefused":
      return "The other broker or registry refused";
    case "Refused":
      return "Refused";
    default:
      return reason ?? "—";
  }
}

/** D89, BO32: the label of a failed send attempt. */
export function custodianFailureLabel(failure: string | null) {
  switch (failure) {
    case "Unreachable":
      return "Custodian not reachable";
    case "Timeout":
      return "Custodian did not answer in time";
    case "Unauthorized":
      return "Custodian refused the key";
    case "Unavailable":
      return "Custodian unavailable";
    case "UnknownAccount":
      return "The custodian does not know this account";
    case "BadResponse":
      return "Custodian answer not understood";
    default:
      return failure ?? "—";
  }
}

export function ownerChangesText(changes: boolean) {
  return `Owner changes: ${changes ? "yes" : "no"}`;
}

/** D87, D89: "100 ACME.SIM from Harbour Brokers" or "… to Harbour Brokers". */
export function transferSummary(item: { direction: string; quantity: number; instrument: { symbol: string }; counterparty: { name: string } }) {
  return `${item.quantity} ${item.instrument.symbol} ${item.direction === "In" ? "from" : "to"} ${item.counterparty.name}`;
}

export const approvalSentence = "A firm administrator must approve this transfer before it is sent to the custodian.";
export const requesterSentence = "Someone else must approve this transfer.";
export const changedTransferSentence = "This transfer has changed.";
export const setAsideSentence =
  "Setting a transfer aside books nothing. Correct the account with an adjustment if the custodian has moved the shares.";
