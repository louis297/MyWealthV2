import { ReportCatalog } from "@/features/reports/ReportCatalog";

export function ReportsPage() {
  return (
    <ReportCatalog
      scope="Book"
      title="h1"
      note="These run for an adviser's book."
      emptyMessage="No reports available."
    />
  );
}
