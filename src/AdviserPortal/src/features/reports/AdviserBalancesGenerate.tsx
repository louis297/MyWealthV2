import { type FormEvent, useState } from "react";
import { useRequestReportMutation } from "@/features/reports/reportsApi";

export function AdviserBalancesGenerate({ reportId }: { reportId: string }) {
  const [open, setOpen] = useState(false);
  const [includeClosedAccounts, setIncludeClosedAccounts] = useState(false);
  const [queued, setQueued] = useState(false);
  const [requestReport, { isLoading }] = useRequestReportMutation();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    await requestReport({
      reportId,
      parameters: { includeClosedAccounts },
    }).unwrap();
    setOpen(false);
    setQueued(true);
  }

  return (
    <div className="space-y-2">
      {queued ? <p>Report queued</p> : null}
      {open ? (
        <form className="space-y-2" onSubmit={(event) => void onSubmit(event)}>
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
