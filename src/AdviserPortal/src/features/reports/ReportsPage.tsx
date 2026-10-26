import { useState } from "react";
import { useAppSelector } from "@/app/hooks";
import { AdviserBalancesGenerate } from "@/features/reports/AdviserBalancesGenerate";
import { ReportCatalog, ReportParametersUnavailable } from "@/features/reports/ReportCatalog";
import { ReportHistory } from "@/features/reports/ReportHistory";
import { ReportSchedules } from "@/features/reports/ReportSchedules";
import { roles } from "@/features/session/roles";

export function ReportsPage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const [selectedId, setSelectedId] = useState("");
  const [focusId, setFocusId] = useState<string | null>(null);
  const canGenerate = role === roles.adviser || role === roles.tenantAdmin;

  return (
    <ReportCatalog
      scope="Book"
      title="h1"
      note="These run for an adviser's book."
      emptyMessage="No reports available."
      selectedId={selectedId}
      onSelectedId={setSelectedId}
      renderSelected={(item) =>
        item.code === "adviser-balances" && canGenerate && role ? (
          <AdviserBalancesGenerate reportId={item.id} role={role} onQueued={setFocusId} />
        ) : (
          <ReportParametersUnavailable />
        )
      }
      renderActions={(item) =>
        item.code === "adviser-balances" && canGenerate && role ? (
          <>
            <ReportSchedules report={item} scope="Book" role={role} />
            <ReportHistory reportId={item.id} scope="Book" focusId={focusId} />
          </>
        ) : null
      }
    />
  );
}
