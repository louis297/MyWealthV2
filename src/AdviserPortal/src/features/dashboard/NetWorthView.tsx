import { Link } from "react-router";
import type { NetWorth } from "@/features/dashboard/dashboardApi";

export function NetWorthView({ data }: { data: NetWorth }) {
  const currencies = data.currencies;
  const byCustomer = data.breakdowns.byCustomer;
  const byAccountType = data.breakdowns.byAccountType;
  const topHoldings = data.breakdowns.topHoldings;

  return (
    <div className="space-y-4">
      {currencies.length === 0 ? <p>No balances.</p> : <CurrencyTable currencies={currencies} />}
      <section className="space-y-2">
        <h2 className="text-base font-medium">By customer</h2>
        {byCustomer.length === 0 ? <p>None.</p> : (
          <table className="border-collapse text-sm">
            <thead>
              <tr>
                <th className="border border-slate-200 px-2 py-1 text-left">Name</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Net</th>
              </tr>
            </thead>
            <tbody>
              {byCustomer.map((row) => (
                <tr key={row.customerId}>
                  <td className="border border-slate-200 px-2 py-1">
                    <Link to={`/customers/${row.customerId}`}>{row.name}</Link>
                  </td>
                  <td className="border border-slate-200 px-2 py-1">
                    {row.currencies.map((currency) => (
                      <span key={currency.currency} className="mr-3">
                        <span>{currency.currency}</span> <span>{currency.net}</span>
                      </span>
                    ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      <section className="space-y-2">
        <h2 className="text-base font-medium">By account type</h2>
        {byAccountType.length === 0 ? <p>None.</p> : (
          <table className="border-collapse text-sm">
            <thead>
              <tr>
                <th className="border border-slate-200 px-2 py-1 text-left">Type</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Net</th>
              </tr>
            </thead>
            <tbody>
              {byAccountType.map((row) => (
                <tr key={row.type}>
                  <td className="border border-slate-200 px-2 py-1">{row.type}</td>
                  <td className="border border-slate-200 px-2 py-1">
                    {row.currencies.map((currency) => (
                      <span key={currency.currency} className="mr-3">
                        <span>{currency.currency}</span> <span>{currency.net}</span>
                      </span>
                    ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      <section className="space-y-2">
        <h2 className="text-base font-medium">Top holdings</h2>
        {topHoldings.length === 0 ? <p>None.</p> : (
          <table aria-label="Top holdings" className="border-collapse text-sm">
            <thead>
              <tr>
                <th className="border border-slate-200 px-2 py-1 text-left">Symbol</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Quantity</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Market value</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Currency</th>
              </tr>
            </thead>
            <tbody>
              {topHoldings.map((row) => (
                <tr key={row.instrumentId}>
                  <td className="border border-slate-200 px-2 py-1">{row.symbol}</td>
                  <td className="border border-slate-200 px-2 py-1">{row.quantity}</td>
                  <td className="border border-slate-200 px-2 py-1">{row.marketValue}</td>
                  <td className="border border-slate-200 px-2 py-1">{row.currency}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}

function CurrencyTable({ currencies }: { currencies: NetWorth["currencies"] }) {
  return (
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
  );
}
