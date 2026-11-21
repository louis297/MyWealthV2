import { type FormEvent, useState } from "react";
import { Messages, SelectField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { SelectItem } from "@/components/ui/select";
import { useGetBankReconciliationOverviewQuery } from "@/features/bankReconciliations/bankReconciliationsApi";
import { useRequestReportMutation } from "@/features/reports/reportsApi";

/** D52: the client money accounts of the overview (D51), as the picker of the bank reconciliation report. */
export function ClientMoneyAccountField({
  id,
  value,
  onChange,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
}) {
  const { data } = useGetBankReconciliationOverviewQuery();
  return (
    <SelectField id={id} label="Client money account" value={value} onChange={(event) => onChange(event.target.value)} required>
      <SelectItem value="">Select a client money account</SelectItem>
      {(data?.items ?? []).map(({ clientMoneyAccount: account }) => (
        <SelectItem key={account.id} value={account.id}>
          {`${account.currency} · ${account.accountNumber}${account.isActive ? "" : " (inactive)"}`}
        </SelectItem>
      ))}
    </SelectField>
  );
}

/** D52: generate the bank reconciliation report for one client money account; no reconciliationId is sent. */
export function BankReconciliationGenerate({
  reportId,
  onQueued,
}: {
  reportId: string;
  onQueued?: (executionId: string) => void;
}) {
  const [clientMoneyAccountId, setClientMoneyAccountId] = useState("");
  const [messages, setMessages] = useState<string[]>([]);
  const [queued, setQueued] = useState(false);
  const [requestReport, { isLoading }] = useRequestReportMutation();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (clientMoneyAccountId === "") {
      return;
    }

    try {
      const created = await requestReport({ reportId, parameters: { clientMoneyAccountId } }).unwrap();
      setMessages([]);
      setQueued(true);
      onQueued?.(created.id);
    } catch (error: unknown) {
      setQueued(false);
      const status = typeof error === "object" && error !== null && "status" in error ? error.status : undefined;
      setMessages([status === 404 ? "Report is not available." : "The report could not be queued."]);
    }
  }

  if (queued) {
    return <p className="text-sm text-muted-foreground">Report queued</p>;
  }

  return (
    <form className="flex w-full flex-col gap-3 rounded-lg bg-muted/40 p-3" onSubmit={(event) => void onSubmit(event)}>
      <Messages messages={messages} />
      <ClientMoneyAccountField id={`client-money-account-${reportId}`} value={clientMoneyAccountId} onChange={setClientMoneyAccountId} />
      <Button type="submit" variant="outline" size="sm" disabled={isLoading || clientMoneyAccountId === ""}>
        Generate now
      </Button>
    </form>
  );
}
