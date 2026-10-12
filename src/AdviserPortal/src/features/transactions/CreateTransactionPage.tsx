import { type FormEvent, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useGetInstrumentsQuery } from "@/features/instruments/instrumentsApi";
import { useCreateTransactionMutation } from "@/features/transactions/transactionsApi";

const types = ["Buy", "Sell", "StockIn", "StockOut", "Split", "Bonus", "Scrip"] as const;

export function CreateTransactionPage() {
  const { id: accountId } = useParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const [type, setType] = useState("Buy");
  const [instrumentId, setInstrumentId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [bookedAt, setBookedAt] = useState("");
  const instruments = useGetInstrumentsQuery();
  const [createTransaction] = useCreateTransactionMutation();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!accountId) {
      return;
    }

    await createTransaction({
      idempotencyKey,
      body: {
        accountId,
        type,
        bookedAt,
        memo: null,
        reference: null,
        security: [{ instrumentId, quantity: Number(quantity) }],
      },
    }).unwrap();
    void navigate(`/accounts/${accountId}`);
  }

  return (
    <div className="max-w-lg space-y-4">
      <h1 className="text-xl font-medium">New transaction</h1>
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
            {types.map((item) => (
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
            Quantity
          </label>
          <input
            id="transaction-quantity"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={quantity}
            onChange={(event) => setQuantity(event.target.value)}
            required
          />
        </div>
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
