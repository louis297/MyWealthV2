import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

type Person = { id: string; name: string } | null;

export type MigrationMoney = { currency: string; amount: number };
export type MigrationQuantity = { instrumentId: string; quantity: number };

/** migration-batches R5, R8: the four totals of a batch. */
export type MigrationTotals = {
  accountCount: number;
  cash: MigrationMoney[];
  quantity: MigrationQuantity[];
  cost: MigrationMoney[];
};

/** migration-batches §8.1: a statement line that funds the batch. */
export type MigrationFundingLine = {
  statementLineId: string;
  currency: string;
  amount: number;
  valueDate: string;
  payerName: string | null;
  reference: string | null;
};

export type MigrationInstrument = { id: string; symbol: string; name: string; quoteCurrency: string };

/** migration-batches R29: one entry with its Account and Instruments expanded. */
export type MigrationEntry = {
  account: { id: string; name: string; depositReference: string | null; currency: string; customer: { id: string; name: string } };
  externalReference: string | null;
  cash: number | null;
  holdings: { instrument: MigrationInstrument; quantity: number; cost: number }[];
  openingTransactionId: string | null;
  openingHoldingsTransactionId: string | null;
};

export type MigrationIssue = {
  code: string;
  accountId: string | null;
  instrumentId: string | null;
  currency: string | null;
  total: string | null;
  message: string;
};

/** migration-batches R28: a batch on the list. */
export type MigrationBatchSummary = {
  id: string;
  name: string;
  previousProvider: string;
  asOfDate: string;
  status: string;
  entryCount: number;
  holdingCount: number;
  createdBy: Person;
  createdAt: string;
  submittedBy: Person;
  submittedAt: string | null;
  postedBy: Person;
  postedAt: string | null;
};

/** migration-batches R29: the item of one batch. */
export type MigrationBatch = MigrationBatchSummary & {
  controlTotals: MigrationTotals;
  computedTotals: MigrationTotals;
  fundingTotals: MigrationMoney[];
  funding: MigrationFundingLine[];
  entries: MigrationEntry[];
  validation: { isValid: boolean; issues: MigrationIssue[] } | null;
  returned: { by: Person; at: string; note: string } | null;
  cancelled: { by: Person; at: string; note: string } | null;
  rowVersion: string;
};

/** migration-batches §8.1: the body of create and replace. */
export type MigrationBatchBody = {
  name: string;
  previousProvider: string;
  asOfDate: string;
  controlTotals: MigrationTotals;
  entries: {
    accountId: string;
    externalReference: string | null;
    cash: number | null;
    holdings: { instrumentId: string; quantity: number; cost: number }[];
  }[];
};

export type MigrationBatchAction = "submit" | "return" | "cancel" | "post";

export const migrationBatchesApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getMigrationBatches: builder.query<PagedList<MigrationBatchSummary>, { status?: string; page?: number }>({
      query: ({ status, page }) => {
        const params = new URLSearchParams();
        if (status) {
          params.set("status", status);
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", "20");
        return `/migration-batches?${params.toString()}`;
      },
      providesTags: ["MigrationBatch"],
    }),
    getMigrationBatch: builder.query<MigrationBatch, string>({
      query: (id) => `/migration-batches/${id}`,
      providesTags: (_result, _error, id) => [{ type: "MigrationBatch", id }],
    }),
    createMigrationBatch: builder.mutation<{ id: string }, MigrationBatchBody>({
      query: (body) => ({ url: "/migration-batches", method: "POST", body }),
      invalidatesTags: ["MigrationBatch"],
    }),
    replaceMigrationBatch: builder.mutation<void, { id: string; rowVersion: string; body: MigrationBatchBody }>({
      query: ({ id, rowVersion, body }) => ({ url: `/migration-batches/${id}`, method: "PUT", body: { ...body, rowVersion } }),
      invalidatesTags: ["MigrationBatch"],
    }),
    actOnMigrationBatch: builder.mutation<void, { id: string; action: MigrationBatchAction; rowVersion: string; note?: string }>({
      query: ({ id, action, rowVersion, note }) => ({
        url: `/migration-batches/${id}/${action}`,
        method: "POST",
        body: note === undefined ? { rowVersion } : { rowVersion, note },
      }),
      invalidatesTags: ["MigrationBatch", "StatementLine", "Transaction", "Account"],
    }),
  }),
});

export const {
  useGetMigrationBatchesQuery,
  useGetMigrationBatchQuery,
  useCreateMigrationBatchMutation,
  useReplaceMigrationBatchMutation,
  useActOnMigrationBatchMutation,
} = migrationBatchesApi;
