import { useAppSelector } from "@/app/hooks";
import { AdviserBalancesGenerate } from "@/features/reports/AdviserBalancesGenerate";
import { ReportCatalog } from "@/features/reports/ReportCatalog";
import { roles } from "@/features/session/roles";

export function ReportsPage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);

  return (
    <ReportCatalog
      scope="Book"
      title="h1"
      note="These run for an adviser's book."
      emptyMessage="No reports available."
      renderActions={(item) =>
        item.code === "adviser-balances" && role === roles.adviser ? (
          <AdviserBalancesGenerate reportId={item.id} />
        ) : null
      }
    />
  );
}
