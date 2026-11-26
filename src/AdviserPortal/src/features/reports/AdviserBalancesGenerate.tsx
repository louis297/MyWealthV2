import { type FormEvent, useState } from "react";
import { CheckField, Messages, Muted, SelectField, TextField } from "@/components/portal";
import { SelectItem } from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useRequestReportMutation } from "@/features/reports/reportsApi";
import { roles } from "@/features/session/roles";
import { lastFinalDay } from "@/shared/time/days";

function isStatus(error: unknown, status: number) {
  return typeof error === "object" && error !== null && "status" in error && error.status === status;
}

/** The messages of a 400, with those of `date` apart so that they are shown at the date input (D110). */
function problemMessages(error: unknown): { date: string[]; other: string[] } {
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return { date: [], other: [] };
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    const entries = Object.entries(data.errors);
    return {
      date: entries.filter(([key]) => key.toLowerCase() === "date").flatMap(([, values]) => values),
      other: entries.filter(([key]) => key.toLowerCase() !== "date").flatMap(([, values]) => values),
    };
  }
  if (data?.detail) {
    return { date: [], other: [data.detail] };
  }
  return { date: [], other: [] };
}

export function AdviserBalancesGenerate({
  reportId,
  role,
  onQueued,
}: {
  reportId: string;
  role: string;
  onQueued?: (executionId: string) => void;
}) {
  const needsAdviser = role === roles.tenantAdmin;
  const { data: advisers } = useGetAdvisersQuery(
    { page: 1, pageSize: 100, enabledOnly: true },
    { skip: !needsAdviser },
  );
  const [adviserId, setAdviserId] = useState("");
  const [includeClosedAccounts, setIncludeClosedAccounts] = useState(false);
  const [date, setDate] = useState("");
  const [dateErrors, setDateErrors] = useState<string[]>([]);
  const [messages, setMessages] = useState<string[]>([]);
  const [queued, setQueued] = useState(false);
  const [requestReport, { isLoading }] = useRequestReportMutation();
  const blocked = needsAdviser && adviserId === "";
  const dateId = `balances-date-${reportId}`;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (blocked) {
      return;
    }

    try {
      const parameters: Record<string, unknown> = needsAdviser ? { adviserId, includeClosedAccounts } : { includeClosedAccounts };
      if (date !== "") {
        parameters.date = date;
      }
      const created = await requestReport({ reportId, parameters }).unwrap();
      setMessages([]);
      setDateErrors([]);
      setQueued(true);
      onQueued?.(created.id);
    } catch (error: unknown) {
      setQueued(false);
      if (isStatus(error, 404)) {
        setMessages(["Report is not available."]);
        return;
      }
      const problems = problemMessages(error);
      setMessages(problems.other);
      setDateErrors(problems.date);
    }
  }

  if (queued) {
    return <p className="text-sm text-muted-foreground">Report queued</p>;
  }

  return (
    <form className="flex w-full flex-col gap-3 rounded-lg bg-muted/40 p-3" onSubmit={(event) => void onSubmit(event)}>
      <Messages messages={messages} />
      {needsAdviser ? (
        <SelectField
          id={`adviser-${reportId}`}
          label="Adviser"
          value={adviserId}
          onChange={(event) => setAdviserId(event.target.value)}
          required
        >
          <SelectItem value="">Select an adviser</SelectItem>
          {advisers?.items.map((adviser) => (
            <SelectItem key={adviser.id} value={adviser.id}>
              {adviser.name}
            </SelectItem>
          ))}
        </SelectField>
      ) : null}
      <TextField
        id={dateId}
        label="Balances at the end of"
        type="date"
        max={lastFinalDay()}
        value={date}
        aria-invalid={dateErrors.length > 0 ? true : undefined}
        aria-describedby={dateErrors.length > 0 ? `${dateId}-error` : undefined}
        onChange={(event) => {
          setDate(event.target.value);
          setDateErrors([]);
          if (event.target.value !== "") {
            setIncludeClosedAccounts(false);
          }
        }}
      />
      {dateErrors.length > 0 ? (
        <p id={`${dateId}-error`} className="text-sm text-destructive">
          {dateErrors.join(" ")}
        </p>
      ) : null}
      <CheckField
        checked={includeClosedAccounts}
        disabled={date !== ""}
        onChange={(event) => setIncludeClosedAccounts(event.target.checked)}
      >
        Include closed accounts
      </CheckField>
      {date !== "" ? <Muted>A report for a past date includes every account that held cash or holdings that day.</Muted> : null}
      <Button type="submit" variant="outline" size="sm" disabled={isLoading || blocked}>
        Generate now
      </Button>
    </form>
  );
}
