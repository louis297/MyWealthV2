import { useGetNetWorthQuery } from "@/features/dashboard/dashboardApi";

export function DashboardPage() {
  const { data, isLoading } = useGetNetWorthQuery();
  const currencies = data?.currencies ?? [];

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-medium">Dashboard</h1>
      {isLoading ? <p>Loading…</p> : null}
      {data && currencies.length === 0 ? <p>No balances.</p> : null}
      {currencies.length > 0 ? (
        <table className="border-collapse text-sm">
          <thead>
            <tr>
              <th className="border border-slate-200 px-2 py-1 text-left">Currency</th>
              <th className="border border-slate-200 px-2 py-1 text-left">Cash</th>
              <th className="border border-slate-200 px-2 py-1 text-left">Holdings</th>
              <th className="border border-slate-200 px-2 py-1 text-left">Assets</th>
              <th className="border border-slate-200 px-2 py-1 text-left">Liabilities</th>
              <th className="border border-slate-200 px-2 py-1 text-left">Net</th>
            </tr>
          </thead>
          <tbody>
            {currencies.map((row) => (
              <tr key={row.currency}>
                <th className="border border-slate-200 px-2 py-1 text-left">{row.currency}</th>
                <td className="border border-slate-200 px-2 py-1">{row.assets.cash}</td>
                <td className="border border-slate-200 px-2 py-1">{row.assets.holdings}</td>
                <td className="border border-slate-200 px-2 py-1">{row.assets.total}</td>
                <td className="border border-slate-200 px-2 py-1">{row.liabilities.total}</td>
                <td className="border border-slate-200 px-2 py-1">{row.net}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}
