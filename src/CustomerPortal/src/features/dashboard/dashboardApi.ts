import { api } from "@/shared/api/api";

export type NetWorthCurrency = {
  currency: string;
  assets: { cash: number; holdings: number; total: number };
  liabilities: { total: number };
  net: number;
};

/** One currency of an FX fold (valuation-history R12). Only a `Folded` line has a `value`. */
export type FoldLine = {
  currency: string;
  total: number;
  status: "Folded" | "NoRate" | "StaleRate";
  fx: { rate: number; date: string; source: string } | null;
  value: number | null;
};

/** The per-currency net of now in the Tenant's reporting currency (valuation-history R14). */
export type FoldedTotal = { currency: string; total: number; complete: boolean; currencies: FoldLine[] };

/** One day of the series of what the platform holds for the Customer (valuation-history R12). */
export type FundsDay = {
  date: string;
  currencies: FoldLine[];
  total: number;
  complete: boolean;
  accounts: number;
  incompleteAccounts: number;
  missingAccounts: number;
};

export type FundsUnderManagement = { reportingCurrency: string; from: string; to: string; days: FundsDay[] };

export type NetWorth = {
  asOf: string;
  currencies: NetWorthCurrency[];
  folded?: FoldedTotal;
  breakdowns: {
    byCustomer: { customerId: string; name: string; currencies: NetWorthCurrency[] }[];
    byAccountType: { type: string; currencies: NetWorthCurrency[] }[];
    /** One entry per custody mode with an Open account, Custodial first (custody-mode-grouping R4). */
    byCustodyMode: { custodyMode: string; currencies: NetWorthCurrency[] }[];
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
    /** C45: the self scope, `from` only; `to` is the last final day. */
    getFundsUnderManagement: builder.query<FundsUnderManagement, string>({
      query: (from) => `/net-worth/funds-under-management?from=${from}`,
      providesTags: ["NetWorth"],
    }),
    getCustomerNetWorth: builder.query<NetWorth, string>({
      query: (id) => `/users/customers/${id}/net-worth`,
      providesTags: ["NetWorth"],
    }),
  }),
});

export const { useGetCustomerNetWorthQuery, useGetFundsUnderManagementQuery } = dashboardApi;
