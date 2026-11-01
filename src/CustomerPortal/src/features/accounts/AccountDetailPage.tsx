import { useParams } from "react-router";
import {
  useGetAccountQuery,
  useGetActivityQuery,
  useGetCashBalanceQuery,
  useGetHoldingsQuery,
} from "@/features/accounts/accountsApi";

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

export function AccountDetailPage() {
  const { id = "" } = useParams();
  const account = useGetAccountQuery(id);
  const cash = useGetCashBalanceQuery(id, { skip: account.isError });
  const holdings = useGetHoldingsQuery(id, { skip: account.isError });
  const activity = useGetActivityQuery(id, { skip: account.isError });

  if (isNotFound(account.error)) {
    return <p>Not found</p>;
  }

  if (account.isLoading || !account.data) {
    return <p>Loading session…</p>;
  }

  return (
    <section>
      <h1>{account.data.name}</h1>
      <p>Status {account.data.status}</p>
      <p>Type {account.data.type}</p>
      {cash.data ? <p>Cash balance {cash.data.cashBalance} {cash.data.currency}</p> : null}
      <h2>Holdings</h2>
      {holdings.data && holdings.data.items.length === 0 ? <p>No holdings.</p> : null}
      {holdings.data && holdings.data.items.length > 0 ? (
        <ul>
          {holdings.data.items.map((holding) => (
            <li key={holding.id}>{holding.symbol} {holding.quantity}</li>
          ))}
        </ul>
      ) : null}
      <h2>Activity</h2>
      {activity.data && activity.data.items.length === 0 ? <p>No activity.</p> : null}
      {activity.data && activity.data.items.length > 0 ? (
        <ul>
          {activity.data.items.map((row) => (
            <li key={row.id}>{row.type} {row.bookedAt}</li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
