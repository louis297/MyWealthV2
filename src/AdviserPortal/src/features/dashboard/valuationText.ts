import type { FoldLine, FundsDay } from "@/features/dashboard/dashboardApi";

/** "no exchange rate" or "exchange rate too old": why a currency was not folded (D107, D108). */
export function notFoldedReason(line: FoldLine): string {
  return line.status === "StaleRate" ? "exchange rate too old" : "no exchange rate";
}

/** D107: the parts of an incomplete day, or null for a complete one. */
export function incompleteReason(day: FundsDay): string | null {
  if (day.complete) {
    return null;
  }

  const parts: string[] = [];
  if (day.incompleteAccounts > 0) {
    parts.push(`${day.incompleteAccounts} accounts not fully valued`);
  }
  if (day.missingAccounts > 0) {
    parts.push(`${day.missingAccounts} accounts not valued yet`);
  }
  for (const line of day.currencies.filter((currency) => currency.status !== "Folded")) {
    parts.push(`${line.currency} not converted (${notFoldedReason(line)})`);
  }
  return `Incomplete: ${parts.join(", ")}`;
}
