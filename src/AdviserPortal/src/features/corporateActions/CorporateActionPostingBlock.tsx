import type { TransactionCorporateAction } from "@/features/transactions/transactionsApi";

/** D68: the event behind an entitlement posting. */
export function CorporateActionPostingBlock({ detail }: { detail: TransactionCorporateAction }) {
  void detail;
  return null;
}
