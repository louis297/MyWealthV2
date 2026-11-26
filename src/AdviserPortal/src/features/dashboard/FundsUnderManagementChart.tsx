import { useState } from "react";
import { Muted, Section } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useGetFundsUnderManagementQuery } from "@/features/dashboard/dashboardApi";
import { incompleteReason } from "@/features/dashboard/valuationText";
import { ValueChart } from "@/shared/components/ValueChart";
import { addDays, lastFinalDay } from "@/shared/time/days";

const ranges = [
  { label: "30 days", days: 30 },
  { label: "90 days", days: 90 },
  { label: "1 year", days: 365 },
] as const;

/** D107: funds under management of the caller's scope, per day, in the reporting currency. */
export function FundsUnderManagementChart() {
  const [days, setDays] = useState<number>(90);
  const from = addDays(lastFinalDay(), -(days - 1));
  const series = useGetFundsUnderManagementQuery(from);
  const currencies = useGetCurrenciesQuery();
  const data = series.data;
  const places = currencies.data?.find((currency) => currency.code === data?.reportingCurrency)?.decimalPlaces;
  const first = data?.days.findIndex((day) => day.accounts > 0) ?? -1;
  const points =
    data && first >= 0
      ? data.days.slice(first).map((day) => ({ date: day.date, value: day.total, incomplete: incompleteReason(day) }))
      : [];

  return (
    <Section
      title="Funds under management over time"
      actions={
        <div className="flex gap-1">
          {ranges.map((range) => (
            <Button
              key={range.days}
              type="button"
              size="sm"
              variant={range.days === days ? "default" : "outline"}
              aria-pressed={range.days === days}
              onClick={() => setDays(range.days)}
            >
              {range.label}
            </Button>
          ))}
        </div>
      }
    >
      {series.isLoading ? <Muted>Loading…</Muted> : null}
      {data ? (
        <>
          <Muted>{`In ${data.reportingCurrency}, at each day's exchange rate. Up to ${data.to}; a day is added once its prices are final.`}</Muted>
          {points.length === 0 ? (
            <Muted>No funds under management in this period.</Muted>
          ) : (
            <ValueChart
              title="Funds under management over time"
              currency={data.reportingCurrency}
              decimalPlaces={places}
              points={points}
            />
          )}
        </>
      ) : null}
    </Section>
  );
}
