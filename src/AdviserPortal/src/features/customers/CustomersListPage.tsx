import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useGetCustomersQuery } from "@/features/customers/customersApi";
import { roles } from "@/features/session/roles";
import { Pager } from "@/shared/components/Pager";

function createdDate(value: string) {
  return value.slice(0, 10);
}

export function CustomersListPage() {
  const me = useAppSelector((state) => state.session.currentUser);
  const [params, setParams] = useSearchParams();
  const page = Number(params.get("page") || "1") || 1;
  const search = params.get("search") ?? "";
  const enabledOnly = params.get("enabledOnly") === "true";
  const adviserId = params.get("adviserId") ?? "";
  const isTenantAdmin = me?.role === roles.tenantAdmin;
  const [searchInput, setSearchInput] = useState(search);

  useEffect(() => {
    setSearchInput(search);
  }, [search]);

  useEffect(() => {
    const handle = window.setTimeout(() => {
      const next = searchInput.trim();
      if (next === search) {
        return;
      }
      const updated = new URLSearchParams(params);
      if (next) {
        updated.set("search", next);
      } else {
        updated.delete("search");
      }
      updated.set("page", "1");
      setParams(updated);
    }, 300);
    return () => window.clearTimeout(handle);
  }, [searchInput, search, params, setParams]);

  const { data } = useGetCustomersQuery({
    page,
    search: search || undefined,
    enabledOnly: enabledOnly || undefined,
    adviserId: isTenantAdmin && adviserId ? adviserId : undefined,
  });

  const { data: advisers } = useGetAdvisersQuery(
    { page: 1, pageSize: 100 },
    { skip: !isTenantAdmin },
  );

  const adviserNames = new Map((advisers?.items ?? []).map((item) => [item.id, item.name]));

  function adviserName(id: string) {
    if (!isTenantAdmin) {
      return me?.name ?? "Me";
    }
    return adviserNames.get(id) ?? "";
  }

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
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-xl font-medium">Customers</h1>
        <Link to="/customers/new" className="border border-slate-300 bg-white px-3 py-1 text-sm">
          New customer
        </Link>
      </div>
      <div className="flex flex-wrap items-end gap-3 text-sm">
        <div>
          <label htmlFor="customers-search" className="block">
            Search
          </label>
          <input
            id="customers-search"
            className="mt-1 border border-slate-300 px-2 py-1"
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
          />
        </div>
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={enabledOnly}
            onChange={(event) => updateParam("enabledOnly", event.target.checked ? "true" : null)}
          />
          Active only
        </label>
        {isTenantAdmin ? (
          <div>
            <label htmlFor="customers-adviser" className="block">
              Adviser
            </label>
            <select
              id="customers-adviser"
              className="mt-1 border border-slate-300 px-2 py-1"
              value={adviserId}
              onChange={(event) => updateParam("adviserId", event.target.value || null)}
            >
              <option value="">All</option>
              {(advisers?.items ?? []).map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          </div>
        ) : null}
      </div>
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-slate-200">
            <th className="py-2">Name</th>
            <th>Email</th>
            <th>Status</th>
            <th>Adviser</th>
            <th>Created</th>
          </tr>
        </thead>
        <tbody>
          {(data?.items ?? []).map((item) => (
            <tr key={item.id} className="border-b border-slate-100">
              <td className="py-2">
                <Link to={`/customers/${item.id}`}>{item.name}</Link>
              </td>
              <td>{item.email}</td>
              <td>{item.status}</td>
              <td>{adviserName(item.adviserId)}</td>
              <td>{createdDate(item.created)}</td>
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
