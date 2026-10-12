import { useState } from "react";
import { useReverseTransactionMutation } from "@/features/transactions/transactionsApi";

export function canReverse(item: { type: string; originalTransactionId?: string | null }) {
  return item.type !== "Reversal" && !item.originalTransactionId;
}

export function ReverseTransactionButton({ id }: { id: string }) {
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const [reversedId, setReversedId] = useState<string | null>(null);
  const [reverseTransaction] = useReverseTransactionMutation();

  async function onReverse() {
    const created = await reverseTransaction({ id, idempotencyKey }).unwrap();
    setReversedId(created.id);
  }

  return (
    <span className="inline-flex items-center gap-2">
      <button type="button" className="border border-slate-300 px-2 py-1" onClick={() => void onReverse()}>
        Reverse
      </button>
      {reversedId ? <span>{reversedId}</span> : null}
    </span>
  );
}
