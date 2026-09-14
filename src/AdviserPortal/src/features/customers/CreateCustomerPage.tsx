import { type FormEvent, useState } from "react";
import { useNavigate } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useCreateCustomerMutation } from "@/features/customers/customersApi";
import { roles } from "@/features/session/roles";

export function CreateCustomerPage() {
  const me = useAppSelector((state) => state.session.currentUser);
  const isTenantAdmin = me?.role === roles.tenantAdmin;
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [adviserId, setAdviserId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [createCustomer] = useCreateCustomerMutation();
  const { data: advisers } = useGetAdvisersQuery(
    { page: 1, pageSize: 100, enabledOnly: true },
    { skip: !isTenantAdmin },
  );

  const activeAdvisers = (advisers?.items ?? []).filter((item) => item.status === "active");

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (password !== confirmPassword) {
      setError("Passwords do not match.");
      return;
    }

    const body = {
      name: name.trim(),
      email: email.trim(),
      password,
      ...(isTenantAdmin ? { adviserId } : {}),
    };

    const created = await createCustomer(body).unwrap();
    void navigate(`/customers/${created.id}`);
  }

  return (
    <div className="max-w-lg space-y-4">
      <h1 className="text-xl font-medium">New customer</h1>
      {error ? <p>{error}</p> : null}
      <form className="space-y-3" onSubmit={(event) => void onSubmit(event)}>
        <div>
          <label htmlFor="customer-name" className="block text-sm">
            Name
          </label>
          <input
            id="customer-name"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
          />
        </div>
        <div>
          <label htmlFor="customer-email" className="block text-sm">
            Email
          </label>
          <input
            id="customer-email"
            type="email"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
        </div>
        <div>
          <label htmlFor="customer-password" className="block text-sm">
            Password
          </label>
          <input
            id="customer-password"
            type="password"
            minLength={8}
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </div>
        <div>
          <label htmlFor="customer-confirm-password" className="block text-sm">
            Confirm password
          </label>
          <input
            id="customer-confirm-password"
            type="password"
            minLength={8}
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={confirmPassword}
            onChange={(event) => setConfirmPassword(event.target.value)}
            required
          />
        </div>
        {isTenantAdmin ? (
          <div>
            <label htmlFor="customer-adviser" className="block text-sm">
              Adviser
            </label>
            <select
              id="customer-adviser"
              className="mt-1 w-full border border-slate-300 px-2 py-1"
              value={adviserId}
              onChange={(event) => setAdviserId(event.target.value)}
              required
            >
              <option value="">Select an adviser</option>
              {activeAdvisers.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          </div>
        ) : null}
        <button type="submit" className="border border-slate-300 bg-white px-3 py-1 text-sm">
          Create customer
        </button>
      </form>
    </div>
  );
}
