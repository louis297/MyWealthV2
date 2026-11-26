import { Muted, Page, PageHeading } from "@/components/portal";
import { useGetNetWorthQuery } from "@/features/dashboard/dashboardApi";
import { FundsUnderManagementChart } from "@/features/dashboard/FundsUnderManagementChart";
import { NetWorthView } from "@/features/dashboard/NetWorthView";

export function DashboardPage() {
  const { data, isLoading } = useGetNetWorthQuery();

  return (
    <Page>
      <PageHeading title="Dashboard" />
      {isLoading ? <Muted>Loading…</Muted> : null}
      {data ? <NetWorthView data={data} chart={<FundsUnderManagementChart />} /> : null}
    </Page>
  );
}
