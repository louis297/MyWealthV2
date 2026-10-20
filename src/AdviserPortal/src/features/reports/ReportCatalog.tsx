import type { ReactNode } from "react";
import { Muted } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  useGetReportsQuery,
  type ReportCatalogItem,
  type ReportScope,
} from "@/features/reports/reportsApi";

export function ReportCatalog({
  scope,
  title,
  note,
  emptyMessage,
  renderActions,
}: {
  scope: ReportScope;
  title: "h1" | "h2";
  note: string;
  emptyMessage: string;
  renderActions?: (item: ReportCatalogItem) => ReactNode;
}) {
  const { data, isLoading, isError, refetch } = useGetReportsQuery({ scope });
  const Title = title;
  const titleClass = title === "h1" ? "text-lg font-semibold tracking-tight" : "text-base font-medium";

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
        <Card className="gap-0 py-0">
          <CardContent className="px-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Function</TableHead>
                  {renderActions ? <TableHead>Actions</TableHead> : null}
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell>{item.name}</TableCell>
                    <TableCell className="max-w-md truncate" title={item.function}>
                      {item.function}
                    </TableCell>
                    {renderActions ? (
                      <TableCell className="whitespace-normal align-top">
                        <div className="flex min-w-56 flex-col items-start gap-3">{renderActions(item)}</div>
                      </TableCell>
                    ) : null}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      ) : null}
    </section>
  );
}
