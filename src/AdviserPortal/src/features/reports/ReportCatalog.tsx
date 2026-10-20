import type { ReactNode } from "react";
import {
  useGetReportsQuery,
  type ReportCatalogItem,
  type ReportScope,
} from "@/features/reports/reportsApi";
import { Button } from "@/components/ui/button";

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
  const titleClass = title === "h1" ? "text-xl font-medium" : "text-base font-medium";

  return (
    <section className="space-y-2">
      <Title className={titleClass}>Reports</Title>
      <p className="text-sm text-slate-600">{note}</p>
      {isLoading ? <p>Loading reports…</p> : null}
      {isError ? (
        <div className="space-y-2 text-sm">
          <p>Reports could not be loaded.</p>
          <Button onClick={() => void refetch()}>
            Retry
          </Button>
        </div>
      ) : null}
      {!isLoading && !isError && data && data.length === 0 ? <p>{emptyMessage}</p> : null}
      {data && data.length > 0 ? (
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200">
              <th className="py-2">Name</th>
              <th>Function</th>
              {renderActions ? <th>Actions</th> : null}
            </tr>
          </thead>
          <tbody>
            {data.map((item) => (
              <tr key={item.id} className="border-b border-slate-100">
                <td className="py-2">{item.name}</td>
                <td className="max-w-md truncate" title={item.function}>
                  {item.function}
                </td>
                {renderActions ? <td className="py-2">{renderActions(item)}</td> : null}
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </section>
  );
}
