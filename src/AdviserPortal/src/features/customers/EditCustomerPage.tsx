import { type FormEvent, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useGetCustomerQuery, useUpdateCustomerMutation } from "@/features/customers/customersApi";
import { roles } from "@/features/session/roles";

function isConflict(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 409;
}

export function EditCustomerPage() {
  const { id } = useParams();
  const me = useAppSelector((state) => state.session.currentUser);
  const isTenantAdmin = me?.role === roles.tenantAdmin;
  const navigate = useNavigate();
  const { data, error, isLoading, refetch } = useGetCustomerQuery(id ?? "", { skip: !id });
  const { data: advisers } = useGetAdvisersQuery(
    { page: 1, pageSize: 100, enabledOnly: true },
    { skip: !isTenantAdmin },
  );
  const [updateCustomer] = useUpdateCustomerMutation();
  const [name, setName] = useState("");
  const [adviserId, setAdviserId] = useState("");
  const [conflict, setConflict] = useState(false);

  useEffect(() => {
    if (data) {
      setName(data.name);
      setAdviserId(data.adviserId);
    }
  }, [data]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!data) {
      return;
    }

    try {
      await updateCustomer({
        id: data.id,
        name: name.trim(),
        rowVersion: data.rowVersion,
        ...(isTenantAdmin ? { adviserId } : {}),
      }).unwrap();
      void navigate(`/customers/${data.id}`);
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
      <h1 className="text-xl font-medium">Edit customer</h1>
      {conflict ? <p>This row changed. Reload and try again.</p> : null}
      <form className="space-y-3" onSubmit={(event) => void onSubmit(event)}>
        <div>
          <label htmlFor="edit-customer-name" className="block text-sm">
            Name
          </label>
          <input
            id="edit-customer-name"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
          />
        </div>
        {isTenantAdmin ? (
          <div>
            <label htmlFor="edit-customer-adviser" className="block text-sm">
              Adviser
            </label>
            <select
              id="edit-customer-adviser"
              className="mt-1 w-full border border-slate-300 px-2 py-1"
              value={adviserId}
              onChange={(event) => setAdviserId(event.target.value)}
              required
            >
              {(advisers?.items ?? [])
                .filter((item) => item.status === "active" || item.id === data.adviserId)
                .map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
            </select>
          </div>
        ) : null}
        <button type="submit" className="border border-slate-300 bg-white px-3 py-1 text-sm">
          Save
        </button>
      </form>
    </div>
  );
}
