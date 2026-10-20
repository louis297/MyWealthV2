import { type FormEvent, useState } from "react";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useRequestReportMutation } from "@/features/reports/reportsApi";
import { roles } from "@/features/session/roles";
import { Button } from "@/components/ui/button";

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
  const [open, setOpen] = useState(false);
  const [adviserId, setAdviserId] = useState("");
  const [includeClosedAccounts, setIncludeClosedAccounts] = useState(false);
  const [queued, setQueued] = useState(false);
  const [requestReport, { isLoading }] = useRequestReportMutation();
  const blocked = needsAdviser && adviserId === "";

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (blocked) {
      return;
    }

    const created = await requestReport({
      reportId,
      parameters: needsAdviser ? { adviserId, includeClosedAccounts } : { includeClosedAccounts },
    }).unwrap();
    setOpen(false);
    setQueued(true);
    onQueued?.(created.id);
  }

  return (
    <div className="space-y-2">
      {queued ? <p>Report queued</p> : null}
      {open ? (
        <form className="space-y-2" onSubmit={(event) => void onSubmit(event)}>
          {needsAdviser ? (
            <div>
              <label htmlFor={`adviser-${reportId}`} className="block text-sm">
                Adviser
              </label>
              <select
                id={`adviser-${reportId}`}
                className="mt-1 w-full border border-slate-300 px-2 py-1"
                value={adviserId}
                onChange={(event) => setAdviserId(event.target.value)}
                required
              >
                <option value="">Select an adviser</option>
                {advisers?.items.map((adviser) => (
                  <option key={adviser.id} value={adviser.id}>
                    {adviser.name}
                  </option>
                ))}
              </select>
            </div>
          ) : null}
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={includeClosedAccounts}
              onChange={(event) => setIncludeClosedAccounts(event.target.checked)}
            />
            Include closed accounts
          </label>
          <Button
            type="submit"
            className="border border-slate-300 bg-white px-3 py-1 text-sm"
            disabled={isLoading || blocked}
          >
            Generate now
          </Button>
        </form>
      ) : (
        <Button
          className="border border-slate-300 bg-white px-3 py-1 text-sm"
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
