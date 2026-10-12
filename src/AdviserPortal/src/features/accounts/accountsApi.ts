import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type Account = {
  id: string;
  tenantId: string;
  customerId: string;
  name: string;
  type: string;
  currency: string;
  cashBalance: number;
  status: string;
  isActive: boolean;
  rowVersion: string;
};

export type AccountListArgs = {
  page?: number;
  customerId?: string;
};

export type CreateAccountBody = {
  customerId: string;
  name: string;
  type: string;
  currency: string;
};

export type AccountHolding = {
  id: string;
  accountId: string;
  instrumentId: string;
  symbol: string;
  name: string;
  quantity: number;
  cost: number;
  costCurrency: string;
};

export const accountsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getAccounts: builder.query<PagedList<Account>, AccountListArgs | void>({
      query: (args) => {
        const params = new URLSearchParams();
        params.set("page", String(args?.page ?? 1));
        params.set("pageSize", "20");
        if (args?.customerId) {
          params.set("customerId", args.customerId);
        }
        return `/accounts?${params.toString()}`;
      },
      providesTags: ["Account"],
    }),
    getAccount: builder.query<Account, string>({
      query: (id) => `/accounts/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Account", id }],
    }),
    getAccountHoldings: builder.query<{ items: AccountHolding[] }, string>({
      query: (id) => `/accounts/${id}/holdings`,
      providesTags: (_result, _error, id) => [{ type: "Account", id }],
    }),
    createAccount: builder.mutation<{ id: string }, CreateAccountBody>({
      query: (body) => ({
        url: "/accounts",
        method: "POST",
        body,
      }),
      invalidatesTags: ["Account"],
    }),
    closeAccount: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({
        url: `/accounts/${id}/close`,
        method: "POST",
        body: { rowVersion },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Account", id: arg.id }, "Account"],
    }),
    reopenAccount: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({
        url: `/accounts/${id}/reopen`,
        method: "POST",
        body: { rowVersion },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Account", id: arg.id }, "Account"],
    }),
  }),
});

export const {
  useGetAccountsQuery,
  useGetAccountQuery,
  useGetAccountHoldingsQuery,
  useCreateAccountMutation,
  useCloseAccountMutation,
  useReopenAccountMutation,
} = accountsApi;
