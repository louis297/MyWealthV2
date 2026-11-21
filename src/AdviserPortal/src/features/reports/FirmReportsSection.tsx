import { useState } from "react";
import { BankReconciliationGenerate } from "@/features/reports/BankReconciliationGenerate";
import { ReportCatalog, ReportParametersUnavailable } from "@/features/reports/ReportCatalog";
import { ReportHistory } from "@/features/reports/ReportHistory";
import { ReportSchedules } from "@/features/reports/ReportSchedules";
import { useGetReportsQuery } from "@/features/reports/reportsApi";

/** D52: the Tenant-scoped reports of a TenantAdmin, under "Firm reports". Shown only when the firm has one. */
export function FirmReportsSection({ role }: { role: string }) {
  const [selectedId, setSelectedId] = useState("");
  const [focusId, setFocusId] = useState<string | null>(null);
  const { data = [] } = useGetReportsQuery({ scope: "Tenant" });
  const reports = data.filter((item) => item.scope === "Tenant");
  const selected = reports.find((item) => item.id === selectedId) ?? null;

  if (reports.length === 0) {
    return null;
  }

  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-base font-medium">Firm reports</h2>
      <ReportCatalog
        scope="Tenant"
        label="Firm report"
        emptyMessage="No firm reports available."
        selectedId={selectedId}
        onSelectedId={setSelectedId}
        renderSelected={(item) =>
          item.code === "bank-reconciliation" ? (
            <BankReconciliationGenerate reportId={item.id} onQueued={setFocusId} />
          ) : (
            <ReportParametersUnavailable />
          )
        }
      />
      <ReportSchedules reports={reports} scope="Tenant" selected={selected} role={role} />
      <ReportHistory scope="Tenant" focusId={focusId} />
    </section>
  );
}
