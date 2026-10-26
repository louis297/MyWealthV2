import { useState } from "react";
import { useAppSelector } from "@/app/hooks";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { AdviserBalancesGenerate } from "@/features/reports/AdviserBalancesGenerate";
import { ReportCatalog, ReportParametersUnavailable } from "@/features/reports/ReportCatalog";
import { ReportHistory } from "@/features/reports/ReportHistory";
import { ReportSchedules } from "@/features/reports/ReportSchedules";
import { useGetReportsQuery } from "@/features/reports/reportsApi";
import { roles } from "@/features/session/roles";

export function ReportsPage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);
  const [selectedId, setSelectedId] = useState("");
  const [focusId, setFocusId] = useState<string | null>(null);
  const { data: reports = [] } = useGetReportsQuery({ scope: "Book" });
  const selected = reports.find((item) => item.id === selectedId) ?? null;
  const canGenerate = role === roles.adviser || role === roles.tenantAdmin;

  return (
    <section className="flex flex-col gap-3">
      <div className="space-y-1">
        <h1 className="text-lg font-semibold tracking-tight">Reports</h1>
        <p className="text-sm text-muted-foreground">These run for an adviser's book.</p>
      </div>
      <Tabs defaultValue="reports">
        <TabsList>
          <TabsTrigger value="reports">Reports</TabsTrigger>
          <TabsTrigger value="schedules">Schedules</TabsTrigger>
          <TabsTrigger value="history">History</TabsTrigger>
        </TabsList>
        <TabsContent value="reports">
          <ReportCatalog
            scope="Book"
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
          />
        </TabsContent>
        <TabsContent value="schedules">
          <ReportSchedules reports={reports} scope="Book" selected={selected} role={role ?? ""} />
        </TabsContent>
        <TabsContent value="history">
          <ReportHistory scope="Book" focusId={focusId} />
        </TabsContent>
      </Tabs>
    </section>
  );
}
