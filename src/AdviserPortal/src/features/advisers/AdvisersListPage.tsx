import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { Pager } from "@/shared/components/Pager";

export function AdvisersListPage() {
  const [params, setParams] = useSearchParams();
  const page = Number(params.get("page") || "1") || 1;
  const search = params.get("search") ?? "";
  const enabledOnly = params.get("enabledOnly") === "true";
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

  const { data } = useGetAdvisersQuery({
    page,
    search: search || undefined,
    enabledOnly: enabledOnly || undefined,
  });

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
        <h1 className="text-xl font-medium">Advisers</h1>
        <Link to="/advisers/new" className="border border-slate-300 bg-white px-3 py-1 text-sm">
          New adviser
        </Link>
      </div>
      <div className="flex flex-wrap items-end gap-3 text-sm">
        <div>
          <label htmlFor="advisers-search" className="block">
            Search
          </label>
          <input
            id="advisers-search"
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
      </div>
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-slate-200">
            <th className="py-2">Name</th>
            <th>Email</th>
            <th>Status</th>
            <th>Created</th>
          </tr>
        </thead>
        <tbody>
          {(data?.items ?? []).map((item) => (
            <tr key={item.id} className="border-b border-slate-100">
              <td className="py-2">
                <Link to={`/advisers/${item.id}`}>{item.name}</Link>
              </td>
              <td>{item.email}</td>
              <td>{item.status}</td>
              <td>{item.created.slice(0, 10)}</td>
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
