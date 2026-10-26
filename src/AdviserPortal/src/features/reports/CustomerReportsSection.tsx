import { useState } from "react";
import { useAppSelector } from "@/app/hooks";
import { CustomerTransactionsGenerate } from "@/features/reports/CustomerTransactionsGenerate";
import { ReportCatalog, ReportParametersUnavailable } from "@/features/reports/ReportCatalog";
import { ReportHistory } from "@/features/reports/ReportHistory";
import { ReportSchedules } from "@/features/reports/ReportSchedules";
import { useGetReportsQuery } from "@/features/reports/reportsApi";

export function CustomerReportsSection({ customerId }: { customerId: string }) {
  const role = useAppSelector((state) => state.session.currentUser?.role) ?? "";
  const [selectedId, setSelectedId] = useState("");
  const [focusId, setFocusId] = useState<string | null>(null);
  const { data: reports = [] } = useGetReportsQuery({ scope: "Customer" });
  const selected = reports.find((item) => item.id === selectedId) ?? null;

  return (
    <ReportCatalog
      scope="Customer"
      title="h2"
      note="These run for this customer."
      emptyMessage="No reports available for this customer."
      selectedId={selectedId}
      onSelectedId={setSelectedId}
      renderSelected={(item) =>
        item.code === "customer-transactions" ? (
          <CustomerTransactionsGenerate reportId={item.id} customerId={customerId} onQueued={setFocusId} />
        ) : (
          <ReportParametersUnavailable />
        )
      }
    >
      <ReportSchedules
        reports={reports}
        scope="Customer"
        selected={selected}
        customerId={customerId}
        role={role}
      />
      <ReportHistory scope="Customer" focusId={focusId} />
    </ReportCatalog>
  );
}
