import { type FormEvent, useState } from "react";
import { CheckField, Messages, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { useRequestReportMutation } from "@/features/reports/reportsApi";

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

export function CustomerTransactionsGenerate({
  reportId,
  customerId,
  onQueued,
}: {
  reportId: string;
  customerId: string;
  onQueued?: (executionId: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [includeClosedAccounts, setIncludeClosedAccounts] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);
  const [queued, setQueued] = useState(false);
  const [requestReport, { isLoading }] = useRequestReportMutation();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (to < from) {
      setMessages(["To must be on or after from."]);
      return;
    }

    try {
      const created = await requestReport({
        reportId,
        parameters: {
          customerId,
          from,
          to,
          includeClosedAccounts,
        },
      }).unwrap();
      setMessages([]);
      setOpen(false);
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

  return (
    <div className="flex flex-col items-start gap-2">
      {queued ? <p className="text-sm text-muted-foreground">Report queued</p> : null}
      {open ? (
        <form className="flex w-full flex-col gap-3 rounded-lg bg-muted/40 p-3" onSubmit={(event) => void onSubmit(event)}>
          <Messages messages={messages} />
          <TextField
            id={`from-${reportId}`}
            label="From"
            type="date"
            value={from}
            onChange={(event) => setFrom(event.target.value)}
            required
          />
          <TextField
            id={`to-${reportId}`}
            label="To"
            type="date"
            value={to}
            onChange={(event) => setTo(event.target.value)}
            required
          />
          <CheckField checked={includeClosedAccounts} onChange={(event) => setIncludeClosedAccounts(event.target.checked)}>
            Include closed accounts
          </CheckField>
          <Button type="submit" size="sm" disabled={isLoading}>
            Generate now
          </Button>
        </form>
      ) : (
        <Button
          type="button"
          size="sm"
          onClick={() => {
            setQueued(false);
            setOpen(true);
          }}
        >
          Generate now
        </Button>
      )}
    </div>
  );
}
