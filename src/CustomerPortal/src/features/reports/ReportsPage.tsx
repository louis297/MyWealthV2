import { type FormEvent, useEffect, useRef, useState } from "react";
import { useAppSelector } from "@/app/hooks";
import { CheckField, Messages, Muted, Page, PageHeading, Section, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { SelectItem } from "@/components/ui/select";
import {
  useGetExecutionQuery,
  useGetExecutionsQuery,
  useGetReportsQuery,
  useRequestReportMutation,
} from "@/features/reports/reportsApi";

const customerTransactions = "customer-transactions";

export function ReportsPage() {
  const me = useAppSelector((state) => state.session.currentUser);
  const reports = useGetReportsQuery();
  const [selectedId, setSelectedId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [includeClosedAccounts, setIncludeClosedAccounts] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);
  const [queuedId, setQueuedId] = useState<string | null>(null);
  const [historyReportId, setHistoryReportId] = useState("");
  const [page, setPage] = useState(1);
  const [requestReport, requestState] = useRequestReportMutation();
  const history = useGetExecutionsQuery({ page, reportId: historyReportId || undefined });
  const polled = useGetExecutionQuery(queuedId ?? "", { skip: !queuedId });

  const selected = reports.data?.find((item) => item.id === selectedId);
  const known = selected?.code === customerTransactions;
  const attempts = useRef(0);
  const refetchExecution = polled.refetch;

  useEffect(() => {
    attempts.current = 0;
  }, [queuedId]);

  useEffect(() => {
    if (!queuedId || !polled.data) {
      return;
    }
    if (polled.data.status === "Succeeded" || polled.data.status === "Failed" || attempts.current >= 30) {
      return;
    }
    const timer = window.setTimeout(() => {
      attempts.current += 1;
      void refetchExecution();
    }, 2000);
    return () => window.clearTimeout(timer);
  }, [queuedId, polled.data, refetchExecution]);

  async function onGenerate(event: FormEvent) {
    event.preventDefault();
    if (!me || !selected) {
      return;
    }
    if (to < from) {
      setMessages(["To must be on or after from."]);
      return;
    }
    try {
      const created = await requestReport({
        reportId: selected.id,
        parameters: {
          customerId: me.id,
          from,
          to,
          includeClosedAccounts,
        },
      }).unwrap();
      setMessages([]);
      setQueuedId(created.id);
    } catch (error: unknown) {
      const status = typeof error === "object" && error !== null && "status" in error
        ? (error as { status?: number }).status
        : undefined;
      setMessages([status === 404 ? "Not found" : "Report is not available."]);
    }
  }

  async function onDownload(id: string) {
    const response = await fetch(`/api/reports/executions/${id}/download`, { credentials: "include" });
    if (!response.ok) {
      setMessages(["Download not available."]);
      return;
    }
    const bytes = await response.arrayBuffer();
    const blob = new Blob([bytes], { type: response.headers.get("Content-Type") ?? "application/pdf" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = "report.pdf";
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }

  return (
    <Page>
      <PageHeading title="Reports" />
      {reports.isLoading ? <Muted>Loading session…</Muted> : null}
      {reports.data && reports.data.length === 0 ? <Muted>No reports available.</Muted> : null}
      {reports.data && reports.data.length > 0 ? (
        <SelectField
          id="report"
          label="Report"
          value={selectedId}
          onChange={(event) => setSelectedId(event.target.value)}
        >
          <SelectItem value="">Select a report</SelectItem>
          {reports.data.map((item) => (
            <SelectItem key={item.id} value={item.id}>{item.name}</SelectItem>
          ))}
        </SelectField>
      ) : null}
      {selected && !known ? <p>parameters not available in this version</p> : null}
      {selected && known ? (
        <form className="flex w-full max-w-sm flex-col gap-3" onSubmit={(event) => void onGenerate(event)}>
          <Messages messages={messages} />
          <TextField
            id="report-from"
            label="From"
            type="date"
            required
            value={from}
            onChange={(event) => setFrom(event.target.value)}
          />
          <TextField
            id="report-to"
            label="To"
            type="date"
            required
            value={to}
            onChange={(event) => setTo(event.target.value)}
          />
          <CheckField
            checked={includeClosedAccounts}
            onChange={(event) => setIncludeClosedAccounts(event.target.checked)}
          >
            Include closed accounts
          </CheckField>
          <Button type="submit" disabled={requestState.isLoading}>Generate</Button>
        </form>
      ) : null}
      {polled.data?.status === "Succeeded" && queuedId ? (
        <Button type="button" onClick={() => void onDownload(queuedId)}>Download</Button>
      ) : null}
      {polled.data?.status === "Failed" ? <p>Failed</p> : null}
      <Section title="History">
        <SelectField
          id="history-report"
          label="History report"
          value={historyReportId}
          onChange={(event) => {
            setHistoryReportId(event.target.value);
            setPage(1);
          }}
        >
          <SelectItem value="">All reports</SelectItem>
          {reports.data?.map((item) => (
            <SelectItem key={item.id} value={item.id}>{item.name}</SelectItem>
          ))}
        </SelectField>
        {history.data && history.data.items.length === 0 ? <Muted>No history.</Muted> : null}
        <ul className="flex flex-col gap-2">
          {history.data?.items.map((item) => (
            <li key={item.id} className="flex items-center gap-2 text-sm">
              {item.status}
              {item.status === "Succeeded" ? (
                <Button type="button" size="sm" variant="outline" onClick={() => void onDownload(item.id)}>
                  Download
                </Button>
              ) : null}
            </li>
          ))}
        </ul>
        {history.data && history.data.totalCount > history.data.pageSize ? (
          <Button type="button" variant="outline" onClick={() => setPage((current) => current + 1)}>Next</Button>
        ) : null}
      </Section>
    </Page>
  );
}
