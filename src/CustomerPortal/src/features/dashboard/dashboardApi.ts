import { api } from "@/shared/api/api";

export type NetWorthCurrency = {
  currency: string;
  assets: { cash: number; holdings: number; total: number };
  liabilities: { total: number };
  net: number;
};

export type NetWorth = {
  asOf: string;
  currencies: NetWorthCurrency[];
  breakdowns: {
    byCustomer: { customerId: string; name: string; currencies: NetWorthCurrency[] }[];
    byAccountType: { type: string; currencies: NetWorthCurrency[] }[];
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
    getCustomerNetWorth: builder.query<NetWorth, string>({
      query: (id) => `/users/customers/${id}/net-worth`,
      providesTags: ["NetWorth"],
    }),
  }),
});

export const { useGetCustomerNetWorthQuery } = dashboardApi;
