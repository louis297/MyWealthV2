import { useState } from "react";
import { useAppSelector } from "@/app/hooks";
import { CustomerTransactionsGenerate } from "@/features/reports/CustomerTransactionsGenerate";
import { ReportCatalog, ReportParametersUnavailable } from "@/features/reports/ReportCatalog";
import { ReportHistory } from "@/features/reports/ReportHistory";
import { ReportSchedules } from "@/features/reports/ReportSchedules";

export function CustomerReportsSection({ customerId }: { customerId: string }) {
  const role = useAppSelector((state) => state.session.currentUser?.role) ?? "";
  const [selectedId, setSelectedId] = useState("");
  const [focusId, setFocusId] = useState<string | null>(null);

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
      renderActions={(item) =>
        item.code === "customer-transactions" ? (
          <>
            <ReportSchedules report={item} scope="Customer" customerId={customerId} role={role} />
            <ReportHistory reportId={item.id} scope="Customer" customerId={customerId} focusId={focusId} />
          </>
        ) : null
      }
    />
  );
}
