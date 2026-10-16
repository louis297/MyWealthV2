import { useState } from "react";
import { Link, useParams } from "react-router";
import { useGetAccountsQuery } from "@/features/accounts/accountsApi";
import { CustomerReportsSection } from "@/features/reports/CustomerReportsSection";
import {
  useDisableCustomerMutation,
  useEnableCustomerMutation,
  useGetCustomerQuery,
} from "@/features/customers/customersApi";
import { Pager } from "@/shared/components/Pager";

function isStatus(error: unknown, status: number) {
  return typeof error === "object" && error !== null && "status" in error && error.status === status;
}

export function CustomerDetailPage() {
  const { id } = useParams();
  const [accountsPage, setAccountsPage] = useState(1);
  const { data, error, isLoading, refetch } = useGetCustomerQuery(id ?? "", { skip: !id });
  const accounts = useGetAccountsQuery(
    { page: accountsPage, customerId: id },
    { skip: !id },
  );
  const [disableCustomer] = useDisableCustomerMutation();
  const [enableCustomer] = useEnableCustomerMutation();
  const [conflict, setConflict] = useState(false);

  async function runStatusChange(action: "disable" | "enable") {
    if (!data) {
      return;
    }
    if (action === "disable" && !window.confirm("Disable this customer?")) {
      return;
    }

    try {
      const body = { id: data.id, rowVersion: data.rowVersion };
      if (action === "disable") {
        await disableCustomer(body).unwrap();
      } else {
        await enableCustomer(body).unwrap();
      }
      setConflict(false);
    } catch (reason: unknown) {
      if (isStatus(reason, 409)) {
        setConflict(true);
        void refetch();
      }
    }
  }

  if (isStatus(error, 404)) {
    return <p>Not found</p>;
  }

  if (isLoading || !data) {
    return <p>Loading…</p>;
  }

  return (
    <div className="space-y-4">
      <h1 className="text-xl font-medium">{data.name}</h1>
      {conflict ? <p>This row changed. Reload and try again.</p> : null}
      <dl className="space-y-2 text-sm">
        <div>
          <dt className="text-slate-500">Email</dt>
          <dd>{data.email}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Status</dt>
          <dd>{data.status}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Created</dt>
          <dd>{data.created.slice(0, 10)}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Id</dt>
          <dd>{data.id}</dd>
        </div>
      </dl>
      {accounts.data ? (
        <section className="space-y-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="text-base font-medium">Accounts</h2>
            <Link
              to={`/customers/${data.id}/accounts/new`}
              className="border border-slate-300 bg-white px-3 py-1 text-sm"
            >
              New account
            </Link>
          </div>
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-slate-200">
                <th className="py-2">Name</th>
                <th>Type</th>
                <th>Currency</th>
                <th>Cash balance</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {accounts.data.items.map((item) => (
                <tr key={item.id} className="border-b border-slate-100">
                  <td className="py-2">
                    <Link to={`/accounts/${item.id}`}>{item.name}</Link>
                  </td>
                  <td>{item.type}</td>
                  <td>{item.currency}</td>
                  <td>{item.cashBalance}</td>
                  <td>{item.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pager
            page={accountsPage}
            pageSize={20}
            totalCount={accounts.data.totalCount}
            onPage={setAccountsPage}
          />
        </section>
      ) : null}
      <CustomerReportsSection customerId={data.id} />
      <div className="flex gap-2 text-sm">
        <Link to={`/customers/${data.id}/edit`} className="border border-slate-300 bg-white px-3 py-1">
          Edit
        </Link>
        {data.status === "disabled" ? (
          <button type="button" onClick={() => void runStatusChange("enable")}>
            Enable
          </button>
        ) : (
          <button type="button" onClick={() => void runStatusChange("disable")}>
            Disable
          </button>
        )}
      </div>
    </div>
  );
}
