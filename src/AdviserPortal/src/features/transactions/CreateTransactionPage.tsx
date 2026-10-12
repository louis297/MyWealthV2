import { type FormEvent, useState } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router";
import { useGetAccountHoldingsQuery, useGetAccountQuery } from "@/features/accounts/accountsApi";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { useCreateTransactionMutation } from "@/features/transactions/transactionsApi";

const types = ["Buy", "Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip"] as const;

function initialType(value: string | null) {
  if (value !== null && types.includes(value as (typeof types)[number])) {
    return value;
  }
  return "Buy";
}

function problemMessages(error: unknown): string[] {
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return [];
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    return Object.values(data.errors).flat();
  }
  if (data?.detail) {
    return [data.detail];
  }
  return [];
}

export function CreateTransactionPage() {
  const { id: accountId } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const [type, setType] = useState(() => initialType(params.get("type")));
  const [instrumentId, setInstrumentId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [cost, setCost] = useState("");
  const [bookedAt, setBookedAt] = useState("");
  const [messages, setMessages] = useState<string[]>([]);
  const account = useGetAccountQuery(accountId ?? "", { skip: !accountId });
  const holdings = useGetAccountHoldingsQuery(accountId ?? "", { skip: !accountId });
  const instruments = useGetInstrumentsQuery();
  const [createTransaction] = useCreateTransactionMutation();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!accountId) {
      return;
    }

    try {
      await createTransaction({
        idempotencyKey,
        body: {
          accountId,
          type,
          bookedAt,
          memo: null,
          reference: null,
          security: [
            {
              instrumentId,
              quantity: Number(quantity),
              ...(type === "StockIn" ? { cost: Number(cost) } : {}),
            },
          ],
        },
      }).unwrap();
      void navigate(`/accounts/${accountId}`);
    } catch (error: unknown) {
      setMessages(problemMessages(error));
    }
  }

  const offerSecurity = !account.data
    || (account.data.status === "Open"
      && (account.data.type === "Brokerage" || account.data.type === "Other"));
  const offeredTypes = offerSecurity ? types : [];

  return (
    <div className="max-w-lg space-y-4">
      <h1 className="text-xl font-medium">New transaction</h1>
      {account.data ? (
        <p>
          Cash balance <span>{account.data.cashBalance}</span>
        </p>
      ) : null}
      {(holdings.data?.items ?? []).map((item) => (
        <p key={item.id}>
          {item.symbol} quantity <span>{item.quantity}</span>
        </p>
      ))}
      {messages.map((message) => (
        <p key={message}>{message}</p>
      ))}
      <form className="space-y-3" onSubmit={(event) => void onSubmit(event)}>
        <div>
          <label htmlFor="transaction-type" className="block text-sm">
            Type
          </label>
          <select
            id="transaction-type"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={type}
            onChange={(event) => setType(event.target.value)}
          >
            {offeredTypes.map((item) => (
              <option key={item} value={item}>
                {item}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="transaction-instrument" className="block text-sm">
            Instrument
          </label>
          <select
            id="transaction-instrument"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={instrumentId}
            onChange={(event) => setInstrumentId(event.target.value)}
            required
          >
            <option value="">Select an instrument</option>
            {(instruments.data?.items ?? []).map((item) => (
              <option key={item.id} value={item.id}>
                {item.symbol} {item.name}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="transaction-quantity" className="block text-sm">
            {type === "Split" || type === "Bonus" || type === "Scrip" ? "New total quantity" : "Quantity"}
          </label>
          <input
            id="transaction-quantity"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={quantity}
            onChange={(event) => setQuantity(event.target.value)}
            required
          />
        </div>
        {type === "StockIn" ? (
          <div>
            <label htmlFor="transaction-cost" className="block text-sm">
              Cost
            </label>
            <input
              id="transaction-cost"
              className="mt-1 w-full border border-slate-300 px-2 py-1"
              value={cost}
              onChange={(event) => setCost(event.target.value)}
              required
            />
          </div>
        ) : null}
        <div>
          <label htmlFor="transaction-booked-at" className="block text-sm">
            Booked at
          </label>
          <input
            id="transaction-booked-at"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={bookedAt}
            onChange={(event) => setBookedAt(event.target.value)}
            required
          />
        </div>
        <button type="submit" className="border border-slate-300 px-3 py-1 text-sm">
          Post transaction
        </button>
      </form>
    </div>
  );
}
