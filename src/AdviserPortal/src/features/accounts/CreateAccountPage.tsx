import { type FormEvent, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useCreateAccountMutation } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";

const accountTypes = ["Bank", "Cash", "Brokerage", "Other"] as const;

export function CreateAccountPage() {
  const { id: customerId } = useParams();
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [type, setType] = useState<string>(accountTypes[0]);
  const [currency, setCurrency] = useState("");
  const [createAccount] = useCreateAccountMutation();
  const { data: currencies } = useGetCurrenciesQuery();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!customerId) {
      return;
    }

    const created = await createAccount({
      customerId,
      name: name.trim(),
      type,
      currency,
    }).unwrap();
    void navigate(`/accounts/${created.id}`);
  }

  return (
    <div className="max-w-lg space-y-4">
      <h1 className="text-xl font-medium">New account</h1>
      <form className="space-y-3" onSubmit={(event) => void onSubmit(event)}>
        <div>
          <label htmlFor="account-name" className="block text-sm">
            Name
          </label>
          <input
            id="account-name"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
          />
        </div>
        <div>
          <label htmlFor="account-type" className="block text-sm">
            Type
          </label>
          <select
            id="account-type"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={type}
            onChange={(event) => setType(event.target.value)}
            required
          >
            {accountTypes.map((item) => (
              <option key={item} value={item}>
                {item}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="account-currency" className="block text-sm">
            Currency
          </label>
          <select
            id="account-currency"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={currency}
            onChange={(event) => setCurrency(event.target.value)}
            required
          >
            <option value="">Select a currency</option>
            {(currencies ?? []).map((item) => (
              <option key={item.code} value={item.code}>
                {item.code}
              </option>
            ))}
          </select>
        </div>
        <button type="submit" className="border border-slate-300 bg-white px-3 py-1 text-sm">
          Create account
        </button>
      </form>
    </div>
  );
}
