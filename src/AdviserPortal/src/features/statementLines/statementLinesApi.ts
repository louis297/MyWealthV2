import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** deposit-matching §8.1: a bank statement line as a TenantAdmin reads it. */
export type StatementLine = {
  id: string;
  clientMoneyAccountId: string;
  currency: string;
  direction: string;
  amount: number;
  valueDate: string;
  payerName: string | null;
  payerAccount: string | null;
  reference: string | null;
  receivedAt: string;
  status: string;
  unmatchedReason: string | null;
  match: {
    accountId: string;
    customerId: string;
    transactionId: string;
    method: string;
    at: string;
  } | null;
  lastAction: {
    action: string;
    note: string | null;
    by: { id: string; name: string };
    at: string;
  } | null;
  /** withdrawals R44: the line this line returns, the withdrawal of a payout line, and the bank's return reason. */
  relatedLineId?: string | null;
  withdrawalId?: string | null;
  returnReason?: string | null;
  rowVersion: string;
};

export type StatementLineListArgs = { status: string; direction?: string; page?: number };

export type NoteAction = "ignore" | "request-return" | "unmatch";

export const statementLinesApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getStatementLines: builder.query<PagedList<StatementLine>, StatementLineListArgs>({
      query: ({ status, direction, page }) => {
        const params = new URLSearchParams();
        params.set("status", status);
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
    matchStatementLine: builder.mutation<void, { id: string; accountId: string; rowVersion: string; note: string | null }>({
      query: ({ id, ...body }) => ({ url: `/statement-lines/${id}/match`, method: "POST", body }),
      invalidatesTags: ["StatementLine", "Account", "Transaction"],
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
