import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type Account = {
  id: string;
  name: string;
  type: string;
  currency: string;
  cashBalance: number;
  status: string;
};

export type Holding = {
  id: string;
  symbol: string;
  name: string;
  quantity: number;
};

export type Activity = {
  id: string;
  type: string;
  bookedAt: string;
  memo: string | null;
};

export const accountsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getAccounts: builder.query<PagedList<Account>, void>({
      query: () => "/accounts?page=1&pageSize=20",
      providesTags: ["Account"],
    }),
    getAccount: builder.query<Account, string>({
      query: (id) => `/accounts/${id}`,
      providesTags: ["Account"],
    }),
    getCashBalance: builder.query<{ cashBalance: number; currency: string }, string>({
      query: (id) => `/accounts/${id}/cash-balance`,
    }),
    getHoldings: builder.query<{ items: Holding[] }, string>({
      query: (id) => `/accounts/${id}/holdings`,
    }),
    getActivity: builder.query<PagedList<Activity>, string>({
      query: (id) => `/transactions?accountId=${encodeURIComponent(id)}&page=1&pageSize=20`,
      providesTags: ["Transaction"],
    }),
  }),
});

export const {
  useGetAccountsQuery,
  useGetAccountQuery,
  useGetCashBalanceQuery,
  useGetHoldingsQuery,
  useGetActivityQuery,
} = accountsApi;
