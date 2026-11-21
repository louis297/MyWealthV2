/** D34: the status labels of the Bank receipts pages. */
export function statusLabel(status: string) {
  return status === "ReturnRequested" ? "To be returned" : status;
}

/** D34: the reason labels of an unmatched receipt. */
export function reasonLabel(reason: string | null | undefined) {
  switch (reason) {
    case "NoReference":
      return "No valid reference";
    case "UnknownReference":
      return "Reference not found";
    case "SeveralReferences":
      return "More than one reference";
    case "CurrencyMismatch":
      return "Account in another currency";
    case "AccountClosed":
      return "Account closed";
    case "CustomerDisabled":
      return "Customer disabled";
    case "AmountPrecision":
      return "Amount has too many decimals";
    case "PaymentMismatch":
      return "Amount differs from the payment";
    default:
      return reason ?? "";
  }
}

/** D35: the label of the last action. */
export function actionLabel(action: string) {
  switch (action) {
    case "ReturnRequested":
      return "Marked to be returned";
    case "Unmatched":
      return "Match undone";
    default:
      return action;
  }
}

export function problemStatus(error: unknown): number | undefined {
  return typeof error === "object" && error !== null && "status" in error && typeof error.status === "number"
    ? error.status
    : undefined;
}

/** The field errors and other messages of a 400 (`errors`), or the detail of another problem. */
export function problemMessages(error: unknown): { fields: Record<string, string[]>; messages: string[] } {
  const data = typeof error === "object" && error !== null && "data" in error
    ? (error as { data?: { errors?: Record<string, string[]>; detail?: string; title?: string } }).data
    : undefined;
  const fields: Record<string, string[]> = {};
  const messages: string[] = [];
  for (const [key, values] of Object.entries(data?.errors ?? {})) {
    if (key) {
      fields[key.toLowerCase()] = values;
    } else {
      messages.push(...values);
    }
  }
  if (!data?.errors && data?.detail) {
    messages.push(data.detail);
  }
  return { fields, messages };
}

/** D35, D49: how a matched line was matched. */
export function methodLabel(method: string) {
  switch (method) {
    case "Manual":
      return "Matched by hand";
    case "Payment":
      return "Paid by the platform";
    case "PaymentReturn":
      return "Payment returned";
    default:
      return "Matched by reference";
  }
}
