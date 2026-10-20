import { useEffect, useState } from "react";
import {
  useGetReportExecutionsQuery,
  type ReportExecution,
  type ReportParameters,
  type ReportScope,
} from "@/features/reports/reportsApi";
import { Pager } from "@/shared/components/Pager";
import { Button } from "@/components/ui/button";

function summary(parameters: ReportParameters): string | null {
  if (typeof parameters.from === "string" && typeof parameters.to === "string") {
    return `${parameters.from} to ${parameters.to}`;
  }
  return null;
}

const pollLimit = 30;

export function ReportHistory({
  reportId,
  scope,
  customerId,
  focusId,
}: {
  reportId: string;
  scope: ReportScope;
  customerId?: string;
  focusId?: string | null;
}) {
  const [open, setOpen] = useState(false);
  const [page, setPage] = useState(1);
  const [downloadError, setDownloadError] = useState(false);
  const [overrides, setOverrides] = useState<Record<string, ReportExecution>>({});
  const shown = open || Boolean(focusId);
  const history = useGetReportExecutionsQuery({ scope, page }, { skip: !shown });
  const rows = (history.data?.items ?? []).filter((item) => {
    if (item.reportId !== reportId) {
      return false;
    }
    if (customerId && item.parameters.customerId !== customerId) {
      return false;
    }
    return true;
  }).map((item) => overrides[item.id] ?? item);

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
    <div className="space-y-2">
      <Button className="border border-slate-300 bg-white px-3 py-1 text-sm" onClick={() => setOpen((value) => !value)}>
        History
      </Button>
      {shown ? (
        <div className="space-y-2 text-sm">
          {history.isLoading ? <p>Loading history…</p> : null}
          {history.isError ? (
            <div className="space-y-2">
              <p>History could not be loaded.</p>
              <Button onClick={() => void history.refetch()}>
                Retry
              </Button>
            </div>
          ) : null}
          {!history.isLoading && !history.isError && rows.length === 0 ? <p>No runs yet.</p> : null}
          {downloadError ? <p>Download not available.</p> : null}
          {rows.map((item) => {
            const range = summary(item.parameters);
            return (
              <p key={item.id}>
                <span>{item.status}</span>
                <span>{item.triggerKind}</span>
                <span>{item.created.slice(0, 10)}</span>
                {range ? <span>{range}</span> : null}
                {item.asOf ? <span>{item.asOf}</span> : null}
                {item.status === "Succeeded" ? (
                  <Button onClick={() => void onDownload(item.id)}>
                    Download
                  </Button>
                ) : null}
              </p>
            );
          })}
          {history.data ? (
            <Pager page={page} pageSize={20} totalCount={history.data.totalCount} onPage={setPage} />
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
