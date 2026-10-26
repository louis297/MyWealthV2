import type { ReactNode } from "react";
import { Muted, SelectField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
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
  renderActions,
}: {
  scope: ReportScope;
  title: "h1" | "h2";
  note: string;
  emptyMessage: string;
  selectedId: string;
  onSelectedId: (id: string) => void;
  renderSelected?: (item: ReportCatalogItem) => ReactNode;
  renderActions?: (item: ReportCatalogItem) => ReactNode;
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
            <option value="">Select a report</option>
            {data.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </SelectField>
          {selected ? <p className="text-sm text-muted-foreground">{selected.function}</p> : null}
          {selected && renderSelected ? renderSelected(selected) : null}
          {renderActions ? (
            <Card className="gap-0 py-0">
              <CardContent className="px-0">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Name</TableHead>
                      <TableHead>Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {data.map((item) => (
                      <TableRow key={item.id}>
                        <TableCell>{item.name}</TableCell>
                        <TableCell className="whitespace-normal align-top">
                          <div className="flex min-w-56 flex-col items-start gap-3">{renderActions(item)}</div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          ) : null}
        </>
      ) : null}
    </section>
  );
}
