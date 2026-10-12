import { useGetNetWorthQuery } from "@/features/dashboard/dashboardApi";
import { NetWorthView } from "@/features/dashboard/NetWorthView";

export function DashboardPage() {
  const { data, isLoading } = useGetNetWorthQuery();

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-medium">Dashboard</h1>
      {isLoading ? <p>Loading…</p> : null}
      {data ? <NetWorthView data={data} /> : null}
    </div>
  );
}
