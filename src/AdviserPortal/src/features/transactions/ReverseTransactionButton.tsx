import { useState } from "react";
import { Messages } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { roles } from "@/features/session/roles";
import { useReverseTransactionMutation } from "@/features/transactions/transactionsApi";

/**
 * D27: Reverse is offered on a posted item that is not a Reversal, is not reversed, and has a hand-reversible
 * source (`Manual` or `Rebalance`). The portal does not read the custody mode for it (transaction-source §9).
 * D31: an `Adjustment` is reversed only by a TenantAdmin (adjustments R17).
 */
export function canReverse(
  item: {
    type: string;
    reversedByTransactionId?: string | null;
    source?: { kind: string } | null;
  },
  role: string | null | undefined,
) {
  const handReversible = item.source?.kind === "Manual" || item.source?.kind === "Rebalance";
  if (item.type === "Adjustment" && role !== roles.tenantAdmin) {
    return false;
  }
  return handReversible && item.type !== "Reversal" && !item.reversedByTransactionId;
}

/** Adjustments §9: the label of an adjustment reason. */
export function adjustmentReasonLabel(reason: string) {
  switch (reason) {
    case "EntryError":
      return "Entry error";
    case "CorporateAction":
      return "Corporate action";
    case "ReconciliationBreak":
      return "Reconciliation";
    case "Other":
      return "Other";
    default:
      return reason;
  }
}

/** D31: the sentence for a 403 from reverse, which only an adjustment returns. */
export const adjustmentReverseForbidden = "Only a firm administrator can reverse an adjustment.";

/** D57: the type of a transaction; the remainder write-off of an account closure reads as rounding. */
export function transactionTypeLabel(item: { type: string; source?: { kind: string } | null }) {
  return item.type === "CloseOut" && item.source?.kind === "AccountClosure" ? "Close out (rounding)" : item.type;
}

/** D26: the "Source" line on `/transactions/:id`. The source id is not shown. */
export function sourceLabel(kind: string | undefined) {
  switch (kind) {
    case "Manual":
      return "Entered by hand";
    case "Rebalance":
      return "Rebalance";
    case "Seed":
      return "Test seed";
    case "BankFeed":
      return "Bank feed";
    case "Payment":
      return "Withdrawal";
    case "AccountClosure":
      return "Account closure";
    default:
      return kind;
  }
}

function problemMessages(error: unknown): string[] {
  if (typeof error === "object" && error !== null && "status" in error && error.status === 403) {
    return [adjustmentReverseForbidden];
  }
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
