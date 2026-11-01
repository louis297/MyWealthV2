import { type FormEvent, useEffect, useRef, useState } from "react";
import { useAppSelector } from "@/app/hooks";
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
    }
  }

  return (
    <section>
      <h1>Reports</h1>
      {reports.isLoading ? <p>Loading session…</p> : null}
      {reports.data && reports.data.length === 0 ? <p>No reports available.</p> : null}
      {reports.data && reports.data.length > 0 ? (
        <label>
          Report
          <select aria-label="Report" value={selectedId} onChange={(event) => setSelectedId(event.target.value)}>
            <option value="">Select a report</option>
            {reports.data.map((item) => (
              <option key={item.id} value={item.id}>{item.name}</option>
            ))}
          </select>
        </label>
      ) : null}
      {selected && !known ? <p>parameters not available in this version</p> : null}
      {selected && known ? (
        <form onSubmit={(event) => void onGenerate(event)}>
          {messages.map((message) => <p key={message}>{message}</p>)}
          <label>
            From
            <input aria-label="From" type="date" required value={from} onChange={(event) => setFrom(event.target.value)} />
          </label>
          <label>
            To
            <input aria-label="To" type="date" required value={to} onChange={(event) => setTo(event.target.value)} />
          </label>
          <label>
            <input
              type="checkbox"
              checked={includeClosedAccounts}
              onChange={(event) => setIncludeClosedAccounts(event.target.checked)}
            />
            Include closed accounts
          </label>
          <button type="submit" disabled={requestState.isLoading}>Generate</button>
        </form>
      ) : null}
      {polled.data?.status === "Succeeded" && queuedId ? (
        <button type="button" onClick={() => void onDownload(queuedId)}>Download</button>
      ) : null}
      {polled.data?.status === "Failed" ? <p>Failed</p> : null}
      <h2>History</h2>
      <label>
        History report
        <select
          aria-label="History report"
          value={historyReportId}
          onChange={(event) => {
            setHistoryReportId(event.target.value);
            setPage(1);
          }}
        >
          <option value="">All reports</option>
          {reports.data?.map((item) => (
            <option key={item.id} value={item.id}>{item.name}</option>
          ))}
        </select>
      </label>
      {history.data && history.data.items.length === 0 ? <p>No history.</p> : null}
      <ul>
        {history.data?.items.map((item) => (
          <li key={item.id}>
            {item.status}
            {item.status === "Succeeded" ? (
              <button type="button" onClick={() => void onDownload(item.id)}>Download</button>
            ) : null}
          </li>
        ))}
      </ul>
      {history.data && history.data.totalCount > history.data.pageSize ? (
        <button type="button" onClick={() => setPage((current) => current + 1)}>Next</button>
      ) : null}
    </section>
  );
}
