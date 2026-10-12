import { Link, useParams } from "react-router";
import { useGetAccountHoldingsQuery, useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetTransactionsQuery } from "@/features/transactions/transactionsApi";

const securityActions = ["Buy", "Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip"] as const;

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

function canPostSecurity(type: string, status: string) {
  return status === "Open" && (type === "Brokerage" || type === "Other");
}

export function AccountDetailPage() {
  const { id } = useParams();
  const account = useGetAccountQuery(id ?? "", { skip: !id });
  const holdings = useGetAccountHoldingsQuery(id ?? "", { skip: !id || !account.data });
  const transactions = useGetTransactionsQuery(
    { accountId: id },
    { skip: !id || !account.data },
  );

  if (isNotFound(account.error)) {
    return <p>Not found</p>;
  }

  if (!account.data || !holdings.data) {
    return <p>Loading…</p>;
  }

  const data = account.data;
  const items = holdings.data.items;

  return (
    <div className="max-w-3xl space-y-4">
      <h1 className="text-xl font-medium">{data.name}</h1>
      {canPostSecurity(data.type, data.status) ? (
        <nav aria-label="Transaction actions" className="flex flex-wrap gap-3 text-sm">
          {securityActions.map((action) => (
            <Link key={action} to={`/accounts/${data.id}/transactions/new?type=${action}`}>
              {action}
            </Link>
          ))}
        </nav>
      ) : null}
      <dl className="space-y-2 text-sm">
        <div>
          <dt className="text-slate-500">Type</dt>
          <dd>{data.type}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Currency</dt>
          <dd>{data.currency}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Status</dt>
          <dd>{data.status}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Cash balance</dt>
          <dd>{data.cashBalance}</dd>
        </div>
      </dl>
      <section className="space-y-2">
        <h2 className="text-base font-medium">Holdings</h2>
        {items.length === 0 ? <p>No holdings.</p> : (
          <table aria-label="Holdings" className="border-collapse text-sm">
            <thead>
              <tr>
                <th className="border border-slate-200 px-2 py-1 text-left">Symbol</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Name</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Quantity</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Cost</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Cost currency</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td className="border border-slate-200 px-2 py-1">{item.symbol}</td>
                  <td className="border border-slate-200 px-2 py-1">{item.name}</td>
                  <td className="border border-slate-200 px-2 py-1">{item.quantity}</td>
                  <td className="border border-slate-200 px-2 py-1">{item.cost}</td>
                  <td className="border border-slate-200 px-2 py-1">{item.costCurrency}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      <section className="space-y-2">
        <h2 className="text-base font-medium">Activity</h2>
        {(transactions.data?.items.length ?? 0) === 0 ? <p>No activity.</p> : (
          <table aria-label="Activity" className="border-collapse text-sm">
            <thead>
              <tr>
                <th className="border border-slate-200 px-2 py-1 text-left">Transaction</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Type</th>
                <th className="border border-slate-200 px-2 py-1 text-left">Booked at</th>
              </tr>
            </thead>
            <tbody>
              {transactions.data?.items.map((item) => (
                <tr key={item.id}>
                  <td className="border border-slate-200 px-2 py-1">
                    <Link to={`/transactions/${item.id}`}>{item.id}</Link>
                  </td>
                  <td className="border border-slate-200 px-2 py-1">{item.type}</td>
                  <td className="border border-slate-200 px-2 py-1">{item.bookedAt}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}
