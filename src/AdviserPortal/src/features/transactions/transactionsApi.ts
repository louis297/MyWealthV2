import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type TransactionCash = {
  amount: number;
  currency: string;
};

export type TransactionSecurity = {
  instrumentId: string;
  quantity: number;
  cost: number;
  costCurrency: string;
};

export type Transaction = {
  id: string;
  tenantId: string;
  accountId: string;
  customerId: string;
  type: string;
  cash?: TransactionCash | null;
  bookedAt: string;
  memo?: string | null;
  reference?: string | null;
  originalTransactionId?: string | null;
  rowVersion: string;
  security?: TransactionSecurity[] | null;
};

export type TransactionListArgs = {
  page?: number;
  accountId?: string;
  customerId?: string;
};

export type CreateTransactionBody = {
  accountId: string;
  type: string;
  bookedAt: string;
  memo: string | null;
  reference: string | null;
  security: { instrumentId: string; quantity: number; cost?: number }[];
};

export const transactionsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getTransactions: builder.query<PagedList<Transaction>, TransactionListArgs | void>({
      query: (args) => {
        const params = new URLSearchParams();
        params.set("page", String(args?.page ?? 1));
        params.set("pageSize", "20");
        if (args?.accountId) {
          params.set("accountId", args.accountId);
        }
        if (args?.customerId) {
          params.set("customerId", args.customerId);
        }
        return `/transactions?${params.toString()}`;
      },
      providesTags: ["Transaction"],
    }),
    createTransaction: builder.mutation<
      { id: string },
      { body: CreateTransactionBody; idempotencyKey: string }
    >({
      query: ({ body, idempotencyKey }) => ({
        url: "/transactions",
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["Transaction", "Account"],
    }),
  }),
});

export const { useGetTransactionsQuery, useCreateTransactionMutation } = transactionsApi;
