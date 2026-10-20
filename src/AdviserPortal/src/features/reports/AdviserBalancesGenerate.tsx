import { type FormEvent, useState } from "react";
import { CheckField, SelectField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useRequestReportMutation } from "@/features/reports/reportsApi";
import { roles } from "@/features/session/roles";

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
    <div className="flex flex-col items-start gap-2">
      {queued ? <p className="text-sm text-muted-foreground">Report queued</p> : null}
      {open ? (
        <form className="flex w-full flex-col gap-3 rounded-lg bg-muted/40 p-3" onSubmit={(event) => void onSubmit(event)}>
          {needsAdviser ? (
            <SelectField
              id={`adviser-${reportId}`}
              label="Adviser"
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
            </SelectField>
          ) : null}
          <CheckField checked={includeClosedAccounts} onChange={(event) => setIncludeClosedAccounts(event.target.checked)}>
            Include closed accounts
          </CheckField>
          <Button type="submit" size="sm" disabled={isLoading || blocked}>
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
