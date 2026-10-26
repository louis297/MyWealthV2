import type { ReactNode } from "react";
import { Muted, SelectField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { SelectItem } from "@/components/ui/select";
import {
  useGetReportsQuery,
  type ReportCatalogItem,
  type ReportScope,
} from "@/features/reports/reportsApi";

export function ReportParametersUnavailable() {
  return (
    <div className="flex flex-col items-start gap-2">
      <p className="text-sm">parameters not available in this version</p>
      <Button type="button" size="sm" disabled>
        Generate now
      </Button>
    </div>
  );
}

export function ReportCatalog({
  scope,
  title,
  note,
  emptyMessage,
  selectedId,
  onSelectedId,
  renderSelected,
  children,
}: {
  scope: ReportScope;
  title: "h1" | "h2";
  note: string;
  emptyMessage: string;
  selectedId: string;
  onSelectedId: (id: string) => void;
  renderSelected?: (item: ReportCatalogItem) => ReactNode;
  children?: ReactNode;
}) {
  const { data, isLoading, isError, refetch } = useGetReportsQuery({ scope });
  const Title = title;
  const titleClass = title === "h1" ? "text-lg font-semibold tracking-tight" : "text-base font-medium";
  const selected = data?.find((item) => item.id === selectedId) ?? null;

  return (
    <section className="flex flex-col gap-3">
      <div className="space-y-1">
        <Title className={titleClass}>Reports</Title>
        <p className="text-sm text-muted-foreground">{note}</p>
      </div>
      {isLoading ? <Muted>Loading reports…</Muted> : null}
      {isError ? (
        <div className="flex flex-col items-start gap-2 text-sm">
          <p>Reports could not be loaded.</p>
          <Button type="button" variant="outline" size="sm" onClick={() => void refetch()}>
            Retry
          </Button>
        </div>
      ) : null}
      {!isLoading && !isError && data && data.length === 0 ? <Muted>{emptyMessage}</Muted> : null}
      {data && data.length > 0 ? (
        <>
          <SelectField
            id={`report-${scope}`}
            label="Report"
            value={selectedId}
            onChange={(event) => onSelectedId(event.target.value)}
          >
            <SelectItem value="">Select a report</SelectItem>
            {data.map((item) => (
              <SelectItem key={item.id} value={item.id}>
                {item.name}
              </SelectItem>
            ))}
          </SelectField>
          {selected ? <p className="text-sm text-muted-foreground">{selected.function}</p> : null}
          {selected && renderSelected ? renderSelected(selected) : null}
        </>
      ) : null}
      {children}
    </section>
  );
}
