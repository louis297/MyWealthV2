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
  }),
});

export const { useGetAccountsQuery, useGetAccountQuery, useGetAccountHoldingsQuery } = accountsApi;
