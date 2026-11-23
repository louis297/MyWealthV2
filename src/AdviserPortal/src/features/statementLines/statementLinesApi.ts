import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** deposit-matching §8.1: a bank statement line as a TenantAdmin reads it. */
export type StatementLine = {
  id: string;
  clientMoneyAccountId: string;
  currency: string;
  direction: string;
  /** interest-run R39: `Transfer` or `Interest`. */
  kind?: string;
  amount: number;
  valueDate: string;
  payerName: string | null;
  payerAccount: string | null;
  reference: string | null;
  receivedAt: string;
  status: string;
  unmatchedReason: string | null;
  /** bank-returns R11: the ids are null on a match with no Transaction. */
  match: {
    accountId: string | null;
    customerId: string | null;
    transactionId: string | null;
    method: string;
    at: string;
  } | null;
  lastAction: {
    action: string;
    note: string | null;
    /** Null when the platform acted (interest-run R40). */
    by: { id: string; name: string } | null;
    at: string;
  } | null;
  /** withdrawals R44: the line this line returns, the withdrawal of a payout line, and the bank's return reason. */
  relatedLineId?: string | null;
  withdrawalId?: string | null;
  returnReason?: string | null;
  /** trade-settlement R21: the trade settlement whose cash this line is. */
  tradeSettlementId?: string | null;
  /** migration-batches R30: the migration batch this line funds. */
  migrationBatchId?: string | null;
  /** bank-returns R11: the newest return payment of a receipt marked for return. */
  returnPayment?: { status: string; attempts: number; lastFailure: string | null } | null;
  rowVersion: string;
};

export type StatementLineListArgs = { status: string; direction?: string; kind?: string; page?: number };

export type NoteAction = "ignore" | "request-return" | "unmatch";

export const statementLinesApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getStatementLines: builder.query<PagedList<StatementLine>, StatementLineListArgs>({
      query: ({ status, direction, kind, page }) => {
        const params = new URLSearchParams();
        params.set("status", status);
        if (kind) {
          params.set("kind", kind);
        }
        if (direction) {
          params.set("direction", direction);
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", "20");
        return `/statement-lines?${params.toString()}`;
      },
      providesTags: ["StatementLine"],
    }),
    getStatementLine: builder.query<StatementLine, string>({
      query: (id) => `/statement-lines/${id}`,
      providesTags: (_result, _error, id) => [{ type: "StatementLine", id }],
    }),
    matchStatementLine: builder.mutation<
      void,
      { id: string; rowVersion: string; note: string | null } & ({ accountId: string } | { migrationBatchId: string })
    >({
      query: ({ id, ...body }) => ({ url: `/statement-lines/${id}/match`, method: "POST", body }),
      invalidatesTags: ["StatementLine", "Account", "Transaction", "MigrationBatch"],
    }),
    noteStatementLine: builder.mutation<void, { id: string; action: NoteAction; note: string; rowVersion: string }>({
      query: ({ id, action, note, rowVersion }) => ({
        url: `/statement-lines/${id}/${action}`,
        method: "POST",
        body: { note, rowVersion },
      }),
      invalidatesTags: ["StatementLine", "Account", "Transaction"],
    }),
    reopenStatementLine: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({ url: `/statement-lines/${id}/reopen`, method: "POST", body: { rowVersion } }),
      invalidatesTags: ["StatementLine"],
    }),
  }),
});

export const {
  useGetStatementLinesQuery,
  useGetStatementLineQuery,
  useMatchStatementLineMutation,
  useNoteStatementLineMutation,
  useReopenStatementLineMutation,
} = statementLinesApi;
