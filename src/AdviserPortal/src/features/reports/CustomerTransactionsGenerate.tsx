import { type FormEvent, useState } from "react";
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
}: {
  reportId: string;
  customerId: string;
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
      await requestReport({
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
    <div className="space-y-2">
      {queued ? <p>Report queued</p> : null}
      {open ? (
        <form className="space-y-2" onSubmit={(event) => void onSubmit(event)}>
          {messages.map((message) => (
            <p key={message}>{message}</p>
          ))}
          <div>
            <label htmlFor={`from-${reportId}`} className="block text-sm">
              From
            </label>
            <input
              id={`from-${reportId}`}
              type="date"
              className="mt-1 border border-slate-300 px-2 py-1"
              value={from}
              onChange={(event) => setFrom(event.target.value)}
              required
            />
          </div>
          <div>
            <label htmlFor={`to-${reportId}`} className="block text-sm">
              To
            </label>
            <input
              id={`to-${reportId}`}
              type="date"
              className="mt-1 border border-slate-300 px-2 py-1"
              value={to}
              onChange={(event) => setTo(event.target.value)}
              required
            />
          </div>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={includeClosedAccounts}
              onChange={(event) => setIncludeClosedAccounts(event.target.checked)}
            />
            Include closed accounts
          </label>
          <button type="submit" className="border border-slate-300 bg-white px-3 py-1 text-sm" disabled={isLoading}>
            Generate now
          </button>
        </form>
      ) : (
        <button
          type="button"
          className="border border-slate-300 bg-white px-3 py-1 text-sm"
          onClick={() => {
            setQueued(false);
            setOpen(true);
          }}
        >
          Generate now
        </button>
      )}
    </div>
  );
}
