import { Link, useSearchParams } from "react-router";
import {
  canReverse,
  ReverseTransactionButton,
} from "@/features/transactions/ReverseTransactionButton";
import {
  useGetTransactionsQuery,
  type TransactionCash,
  type TransactionSecurity,
} from "@/features/transactions/transactionsApi";
import { Pager } from "@/shared/components/Pager";

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

function cashText(cash: TransactionCash | null | undefined) {
  if (!cash) {
    return "";
  }
  return `${cash.amount} ${cash.currency}`;
}

function securityText(security: TransactionSecurity[] | null | undefined) {
  const leg = security?.[0];
  if (!leg) {
    return "";
  }
  return `${leg.quantity} ${leg.cost} ${leg.costCurrency}`;
}

export function TransactionsListPage() {
  const [params, setParams] = useSearchParams();
  const page = Number(params.get("page") || "1") || 1;
  const accountId = params.get("accountId") ?? "";
  const customerId = params.get("customerId") ?? "";
  const { data, error } = useGetTransactionsQuery({
    page,
    accountId: accountId || undefined,
    customerId: customerId || undefined,
  });
  const notFound = isNotFound(error);

  function updateParam(key: string, value: string | null) {
    const updated = new URLSearchParams(params);
    if (value) {
      updated.set(key, value);
    } else {
      updated.delete(key);
    }
    if (key !== "page") {
      updated.set("page", "1");
    }
    setParams(updated);
  }

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-medium">Transactions</h1>
      <div className="flex flex-wrap items-end gap-3 text-sm">
        <div>
          <label htmlFor="transactions-account" className="block">
            Account
          </label>
          <input
            id="transactions-account"
            className="mt-1 border border-slate-300 px-2 py-1"
            value={accountId}
            onChange={(event) => updateParam("accountId", event.target.value || null)}
          />
        </div>
        <div>
          <label htmlFor="transactions-customer" className="block">
            Customer
          </label>
          <input
            id="transactions-customer"
            className="mt-1 border border-slate-300 px-2 py-1"
            value={customerId}
            onChange={(event) => updateParam("customerId", event.target.value || null)}
          />
        </div>
      </div>
      {notFound ? <p>Not found</p> : (
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200">
              <th className="py-2">Type</th>
              <th>Booked at</th>
              <th>Account</th>
              <th>Customer</th>
              <th>Cash</th>
              <th>Security</th>
              <th>Memo</th>
              <th>Reverse</th>
            </tr>
          </thead>
          <tbody>
            {(data?.items ?? []).map((item) => (
              <tr key={item.id} className="border-b border-slate-100">
                <td className="py-2">
                  <Link to={`/transactions/${item.id}`}>{item.type}</Link>
                </td>
                <td>{item.bookedAt}</td>
                <td>
                  <Link to={`/accounts/${item.accountId}`}>{item.accountId}</Link>
                </td>
                <td>
                  <Link to={`/customers/${item.customerId}`}>{item.customerId}</Link>
                </td>
                <td>{cashText(item.cash)}</td>
                <td>{securityText(item.security)}</td>
                <td>{item.memo ?? ""}</td>
                <td>{canReverse(item) ? <ReverseTransactionButton id={item.id} /> : null}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <Pager
        page={page}
        pageSize={20}
        totalCount={data?.totalCount ?? 0}
        onPage={(next) => updateParam("page", String(next))}
      />
    </div>
  );
}
