import { formatAmount } from "@/shared/format/money";

/** D80: a 403 on the Migration batches route. */
export const forbiddenSentence = "Only a firm administrator can see migration batches.";

/** D80: the status labels of a migration batch. */
export function migrationBatchStatusLabel(status: string) {
  switch (status) {
    case "Draft":
      return "Draft";
    case "Submitted":
      return "Waiting to be posted";
    case "Posted":
      return "Posted";
    case "Cancelled":
      return "Cancelled";
    default:
      return status;
  }
}

/** D80: the status filter of the list; Open is the default, All sends nothing. */
export const migrationBatchFilters: Record<string, { label: string; status?: string }> = {
  Open: { label: "Draft and submitted", status: "Draft,Submitted" },
  Draft: { label: "Draft", status: "Draft" },
  Submitted: { label: "Waiting to be posted", status: "Submitted" },
  Posted: { label: "Posted", status: "Posted" },
  Cancelled: { label: "Cancelled", status: "Cancelled" },
  All: { label: "All" },
};

/** D82: "250,000.00 USD". */
export function moneyText(amount: number, currency: string) {
  return `${formatAmount(amount, 2)} ${currency}`;
}

/** D82: the confirmation of the post. */
export function postQuestion(accounts: number, cash: { currency: string; amount: number }[], holdings: number) {
  const cashText = cash.length === 0 ? "no cash" : cash.map((row) => moneyText(row.amount, row.currency)).join(", ");
  return `Post ${accounts} accounts: ${cashText} and ${holdings} holdings. This cannot be undone.`;
}

/** D81: one row of the CSV after it is read. */
export type CsvRow = {
  line: number;
  account: string;
  externalReference: string;
  cash: string;
  symbol: string;
  quantity: string;
  cost: string;
};

export const csvHeader = ["account", "externalReference", "cash", "symbol", "quantity", "cost"];

/** D81: reads the CSV of a batch. A header other than {@link csvHeader} is one error on line 1. */
export function readCsv(text: string): { rows: CsvRow[]; errors: string[] } {
  const lines = text.replace(/\r\n?/g, "\n").split("\n");
  const header = (lines[0] ?? "").split(",").map((cell) => cell.trim());
  if (header.join(",") !== csvHeader.join(",")) {
    return { rows: [], errors: [`Line 1: the header must be ${csvHeader.join(",")}.`] };
  }
  const rows: CsvRow[] = [];
  const errors: string[] = [];
  lines.slice(1).forEach((raw, index) => {
    const line = index + 2;
    if (raw.trim().length === 0) {
      return;
    }
    const cells = raw.split(",").map((cell) => cell.trim());
    if (cells.length !== csvHeader.length) {
      errors.push(`Line ${line}: expected ${csvHeader.length} values.`);
      return;
    }
    const [account, externalReference, cash, symbol, quantity, cost] = cells;
    rows.push({ line, account, externalReference, cash, symbol, quantity, cost });
  });
  return { rows, errors };
}
