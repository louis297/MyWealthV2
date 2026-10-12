import { Link, useSearchParams } from "react-router";
import { useGetAccountsQuery } from "@/features/accounts/accountsApi";
import { Pager } from "@/shared/components/Pager";

export function AccountsListPage() {
  const [params, setParams] = useSearchParams();
  const page = Number(params.get("page") || "1") || 1;
  const { data } = useGetAccountsQuery({ page });

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
      <h1 className="text-xl font-medium">Accounts</h1>
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
          {(data?.items ?? []).map((item) => (
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
        page={page}
        pageSize={20}
        totalCount={data?.totalCount ?? 0}
        onPage={(next) => updateParam("page", String(next))}
      />
    </div>
  );
}
