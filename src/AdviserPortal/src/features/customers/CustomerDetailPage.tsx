import { useState } from "react";
import { Link, useParams } from "react-router";
import {
  useDisableCustomerMutation,
  useEnableCustomerMutation,
  useGetCustomerQuery,
} from "@/features/customers/customersApi";

function isConflict(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 409;
}

export function CustomerDetailPage() {
  const { id } = useParams();
  const { data, error, isLoading, refetch } = useGetCustomerQuery(id ?? "", { skip: !id });
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
      if (isConflict(reason)) {
        setConflict(true);
        void refetch();
      }
    }
  }

  if (error && "status" in error && error.status === 404) {
    return <p>Not found</p>;
  }

  if (isLoading || !data) {
    return <p>Loading…</p>;
  }

  return (
    <div className="max-w-lg space-y-4">
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
