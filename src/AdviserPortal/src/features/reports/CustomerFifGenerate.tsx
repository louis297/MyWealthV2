import { type FormEvent, useState } from "react";
import { Messages, SelectField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { SelectItem } from "@/components/ui/select";
import { useRequestReportMutation } from "@/features/reports/reportsApi";
import { reportableTaxYears, taxYearLabel } from "@/shared/time/taxYears";

function isStatus(error: unknown, status: number) {
  return typeof error === "object" && error !== null && "status" in error && error.status === status;
}

function problemMessages(error: unknown): string[] {
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return [];
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    return Object.values(data.errors).flat();
  }
  if (data?.detail) {
    return [data.detail];
  }
  return [];
}

/** The tax year form of the Customer page (D114, fif-report R27; D115, income-summary R34): one field, the tax year. */
export function CustomerFifGenerate({
  reportId,
  customerId,
  onQueued,
}: {
  reportId: string;
  customerId: string;
  onQueued?: (executionId: string) => void;
}) {
  const [years] = useState(() => reportableTaxYears());
  const [taxYear, setTaxYear] = useState(years.length > 0 ? String(years[0]) : "");
  const [messages, setMessages] = useState<string[]>([]);
  const [queued, setQueued] = useState(false);
  const [requestReport, { isLoading }] = useRequestReportMutation();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    try {
      const created = await requestReport({
        reportId,
        parameters: { customerId, taxYear: Number(taxYear) },
      }).unwrap();
      setMessages([]);
      setQueued(true);
      onQueued?.(created.id);
    } catch (error: unknown) {
      setQueued(false);
      if (isStatus(error, 404)) {
        setMessages(["Report is not available."]);
        return;
      }
      setMessages(problemMessages(error));
    }
  }

  if (queued) {
    return <p className="text-sm text-muted-foreground">Report queued</p>;
  }

  return (
    <form className="flex w-full flex-col gap-3 rounded-lg bg-muted/40 p-3" onSubmit={(event) => void onSubmit(event)}>
      <Messages messages={messages} />
      <SelectField
        id={`tax-year-${reportId}`}
        label="Tax year"
        value={taxYear}
        onChange={(event) => setTaxYear(event.target.value)}
        required
      >
        {years.map((year) => (
          <SelectItem key={year} value={String(year)}>
            {taxYearLabel(year)}
          </SelectItem>
        ))}
      </SelectField>
      <Button type="submit" size="sm" disabled={isLoading || taxYear === ""}>
        Generate now
      </Button>
    </form>
  );
}
