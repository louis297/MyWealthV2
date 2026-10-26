import { useEffect, useState } from "react";
import { Muted, SelectField } from "@/components/portal";
import { SelectItem } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  useGetReportExecutionsQuery,
  useGetReportsQuery,
  type ReportExecution,
  type ReportParameters,
  type ReportScope,
} from "@/features/reports/reportsApi";
import { Pager } from "@/shared/components/Pager";

function summary(parameters: ReportParameters): string | null {
  if (typeof parameters.from === "string" && typeof parameters.to === "string") {
    return `${parameters.from} to ${parameters.to}`;
  }
  return null;
}

const pollLimit = 30;

export function ReportHistory({
  scope,
  focusId,
}: {
  scope: ReportScope;
  focusId?: string | null;
}) {
  const [page, setPage] = useState(1);
  const [reportId, setReportId] = useState("");
  const [downloadError, setDownloadError] = useState(false);
  const [overrides, setOverrides] = useState<Record<string, ReportExecution>>({});
  const catalog = useGetReportsQuery({ scope });
  const history = useGetReportExecutionsQuery({ scope, page, reportId: reportId || undefined });
  const rows = (history.data?.items ?? []).map((item) => overrides[item.id] ?? item);

  function reportName(id: string) {
    return catalog.data?.find((item) => item.id === id)?.name ?? id;
  }

  useEffect(() => {
    if (!focusId) {
      return;
    }
    let polls = 0;
    let stopped = false;
    let timer = 0;
    const tick = () => {
      if (stopped) {
        return;
      }
      polls += 1;
      if (polls > pollLimit) {
        return;
      }
      void fetch(`/api/reports/executions/${focusId}`, { credentials: "include" })
        .then(async (response) => {
          if (!response.ok || stopped) {
            return;
          }
          const execution = (await response.json()) as ReportExecution;
          if (stopped) {
            return;
          }
          setOverrides((current) => ({ ...current, [execution.id]: execution }));
          if (execution.status === "Succeeded" || execution.status === "Failed") {
            return;
          }
          timer = window.setTimeout(tick, 2000);
        });
    };
    timer = window.setTimeout(tick, 2000);
    return () => {
      stopped = true;
      window.clearTimeout(timer);
    };
  }, [focusId]);

  async function onDownload(id: string) {
    setDownloadError(false);
    const response = await fetch(`/api/reports/executions/${id}/download`, { credentials: "include" });
    if (!response.ok) {
      setDownloadError(true);
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
  }

  return (
    <section className="flex w-full flex-col gap-2 text-sm">
      <h2 className="text-base font-medium">History</h2>
      <SelectField
        id={`history-report-${scope}`}
        label="History report"
        value={reportId}
        onChange={(event) => {
          setReportId(event.target.value);
          setPage(1);
        }}
      >
        <SelectItem value="">All</SelectItem>
        {catalog.data?.map((item) => (
          <SelectItem key={item.id} value={item.id}>
            {item.name}
          </SelectItem>
        ))}
      </SelectField>
      {history.isLoading ? <Muted>Loading history…</Muted> : null}
      {history.isError ? (
        <div className="flex flex-col items-start gap-2">
          <p>History could not be loaded.</p>
          <Button type="button" variant="outline" size="sm" onClick={() => void history.refetch()}>
            Retry
          </Button>
        </div>
      ) : null}
      {!history.isLoading && !history.isError && rows.length === 0 ? <Muted>No runs yet.</Muted> : null}
      {downloadError ? <p>Download not available.</p> : null}
      {rows.map((item) => {
        const range = summary(item.parameters);
        return (
          <div key={item.id} className="flex flex-wrap items-center gap-2 rounded-md border bg-card px-2 py-1.5">
            <span>{reportName(item.reportId)}</span>
            <Badge variant={item.status === "Failed" ? "outline" : "secondary"}>
              <span>{item.status}</span>
            </Badge>
            <span>{item.triggerKind}</span>
            <span>{item.created.slice(0, 10)}</span>
            {range ? <span>{range}</span> : null}
            {item.asOf ? <span>{item.asOf}</span> : null}
            {item.status === "Succeeded" ? (
              <Button type="button" variant="outline" size="sm" onClick={() => void onDownload(item.id)}>
                Download
              </Button>
            ) : null}
          </div>
        );
      })}
      {history.data ? (
        <Pager page={page} pageSize={20} totalCount={history.data.totalCount} onPage={setPage} />
      ) : null}
    </section>
  );
}
