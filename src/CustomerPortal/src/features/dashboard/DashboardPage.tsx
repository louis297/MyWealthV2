import { useAppSelector } from "@/app/hooks";
import { useGetCustomerNetWorthQuery } from "@/features/dashboard/dashboardApi";

export function DashboardPage() {
  const id = useAppSelector((state) => state.session.currentUser?.id);
  const { data, isLoading, isError } = useGetCustomerNetWorthQuery(id ?? "", { skip: !id });

  if (!id || isLoading) {
    return <p>Loading session…</p>;
  }

  if (isError || !data) {
    return <p>Net worth is not available.</p>;
  }

  return (
    <section>
      <h1>Dashboard</h1>
      {data.currencies.length === 0 ? <p>No balances.</p> : (
        <table>
          <caption>Per currency</caption>
          <thead>
            <tr>
              <th>Currency</th>
              <th>Cash</th>
              <th>Holdings</th>
              <th>Assets</th>
              <th>Liabilities</th>
              <th>Net</th>
            </tr>
          </thead>
          <tbody>
            {data.currencies.map((row) => (
              <tr key={row.currency}>
                <td>{row.currency}</td>
                <td>{row.assets.cash}</td>
                <td>{row.assets.holdings}</td>
                <td>{row.assets.total}</td>
                <td>{row.liabilities.total}</td>
                <td>{row.net}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <h2>By account type</h2>
      {data.breakdowns.byAccountType.length === 0 ? <p>No balances.</p> : (
        <table>
          <thead>
            <tr>
              <th>Type</th>
              <th>Net</th>
            </tr>
          </thead>
          <tbody>
            {data.breakdowns.byAccountType.map((row) => (
              <tr key={row.type}>
                <td>{row.type}</td>
                <td>{row.currencies.map((currency) => `${currency.currency} ${currency.net}`).join(", ")}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <h2>Top holdings</h2>
      {data.breakdowns.topHoldings.length === 0 ? <p>No balances.</p> : (
        <ul>
          {data.breakdowns.topHoldings.map((holding) => (
            <li key={holding.instrumentId}>{holding.symbol} {holding.marketValue} {holding.currency}</li>
          ))}
        </ul>
      )}
    </section>
  );
}
