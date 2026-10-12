import { api } from "@/shared/api/api";

export type NetWorthCurrency = {
  currency: string;
  assets: {
    cash: number;
    holdings: number;
    total: number;
  };
  liabilities: {
    total: number;
  };
  net: number;
};

export type NetWorth = {
  asOf: string;
  currencies: NetWorthCurrency[];
  breakdowns: {
    byCustomer: {
      customerId: string;
      name: string;
      currencies: NetWorthCurrency[];
    }[];
    byAccountType: {
      type: string;
      currencies: NetWorthCurrency[];
    }[];
    topHoldings: {
      instrumentId: string;
      symbol: string;
      quantity: number;
      marketValue: number;
      currency: string;
    }[];
  };
};

export const dashboardApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getNetWorth: builder.query<NetWorth, void>({
      query: () => "/net-worth",
      providesTags: ["NetWorth"],
    }),
    getCustomerNetWorth: builder.query<NetWorth, string>({
      query: (id) => `/users/customers/${id}/net-worth`,
      providesTags: (_result, _error, id) => [{ type: "NetWorth", id }],
    }),
  }),
});

export const { useGetNetWorthQuery, useGetCustomerNetWorthQuery } = dashboardApi;
