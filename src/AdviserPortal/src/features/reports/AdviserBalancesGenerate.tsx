import { type FormEvent, useState } from "react";
import { CheckField, Messages, SelectField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useRequestReportMutation } from "@/features/reports/reportsApi";
import { roles } from "@/features/session/roles";

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
  const [messages, setMessages] = useState<string[]>([]);
  const [queued, setQueued] = useState(false);
  const [requestReport, { isLoading }] = useRequestReportMutation();
  const blocked = needsAdviser && adviserId === "";

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (blocked) {
      return;
    }

    try {
      const created = await requestReport({
        reportId,
        parameters: needsAdviser ? { adviserId, includeClosedAccounts } : { includeClosedAccounts },
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
      <Button type="submit" variant="outline" size="sm" disabled={isLoading || blocked}>
        Generate now
      </Button>
    </form>
  );
}
