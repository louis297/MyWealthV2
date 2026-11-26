import { Muted } from "@/components/portal";
import type { FoldedTotal } from "@/features/dashboard/dashboardApi";
import { formatAmount } from "@/shared/format/money";

/** C46: the per-currency net in the reporting currency at today's rates, and the currencies it leaves out. Not shown
 * when the only currency is the reporting currency. */
export function FoldedTotalLine({ folded }: { folded: FoldedTotal }) {
  const lines = folded.currencies;
  if (lines.length === 0 || (lines.length === 1 && lines[0].currency === folded.currency)) {
    return null;
  }

  const left = lines.filter((line) => line.status !== "Folded");
  return (
    <div className="flex flex-col gap-1 text-sm">
      <p>{`Total in ${folded.currency}: ${formatAmount(folded.total)} at today's exchange rates`}</p>
      {left.length === 0 ? null : (
        <Muted>{`Not counted: ${left
          .map((line) => `${line.currency} (${line.status === "StaleRate" ? "exchange rate too old" : "no exchange rate"})`)
          .join(", ")}`}</Muted>
      )}
    </div>
  );
}
