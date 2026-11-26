import { useState } from "react";
import { Muted, Section } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { useGetFundsUnderManagementQuery } from "@/features/dashboard/dashboardApi";
import { ValueChart } from "@/shared/components/ValueChart";
import { addDays, lastFinalDay } from "@/shared/time/days";

const ranges = [
  { label: "90 days", days: 90 },
  { label: "1 year", days: 365 },
] as const;

/** C45: what the platform holds for the Customer, per day, in the reporting currency. Hidden with no Custodial
 * account in the range. */
export function HeldOnPlatformChart() {
  const [days, setDays] = useState<number>(90);
  const series = useGetFundsUnderManagementQuery(addDays(lastFinalDay(), -(days - 1)));
  const data = series.data;
  const first = data?.days.findIndex((day) => day.accounts > 0) ?? -1;
  if (!data || first < 0) {
    return null;
  }

  const points = data.days.slice(first).map((day) => ({
    date: day.date,
    value: day.total,
    incomplete: day.complete ? null : "Some holdings could not be valued on this day.",
  }));

  return (
    <Section
      title="Held on platform over time"
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
      <Muted>{`In ${data.reportingCurrency}, at each day's exchange rate. The latest two days are added once their prices are final.`}</Muted>
      <ValueChart title="Held on platform over time" currency={data.reportingCurrency} points={points} />
    </Section>
  );
}
