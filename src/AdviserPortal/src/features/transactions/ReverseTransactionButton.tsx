import { useState } from "react";
import { Messages } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { useReverseTransactionMutation } from "@/features/transactions/transactionsApi";

export function canReverse(item: { type: string; reversedByTransactionId?: string | null }) {
  return item.type !== "Reversal" && !item.reversedByTransactionId;
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

export function ReverseTransactionButton({ id }: { id: string }) {
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const [reversedId, setReversedId] = useState<string | null>(null);
  const [messages, setMessages] = useState<string[]>([]);
  const [reverseTransaction] = useReverseTransactionMutation();

  async function onReverse() {
    try {
      const created = await reverseTransaction({ id, idempotencyKey }).unwrap();
      setMessages([]);
      setReversedId(created.id);
    } catch (error: unknown) {
      setMessages(problemMessages(error));
    }
  }

  return (
    <span className="inline-flex flex-wrap items-center gap-2">
      <Button type="button" variant="outline" size="sm" onClick={() => void onReverse()}>
        Reverse
      </Button>
      {reversedId ? <span>{reversedId}</span> : null}
      <Messages messages={messages} />
    </span>
  );
}
