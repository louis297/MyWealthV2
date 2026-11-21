import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** bank-reconciliation §8.1: the client money account of a run or an overview entry. */
export type ReconciliationAccount = {
  id: string;
  currency: string;
  bankName: string;
  accountName: string;
  accountNumber: string;
  isActive: boolean;
};

export type ReconciliationTotal = { count: number; amount: number };

/** R20: a run without its items, feed and payments in flight. */
export type BankReconciliationListItem = {
  id: string;
  clientMoneyAccount: ReconciliationAccount;
  trigger: string;
  ranAt: string;
  ranBy: { id: string; name: string } | null;
  status: string;
  bankBalance: number | null;
  balanceAt: string | null;
  openingBalance: number | null;
  statementLinesTotal: number | null;
  custodialCash: number;
  difference: number | null;
  totals: { breaks: ReconciliationTotal; timing: ReconciliationTotal; known: ReconciliationTotal };
  reservedCash: number;
};

export type ReconciliationItem = {
  kind: string;
  class: string;
  amount: number;
  count: number;
  statementLine: {
    id: string;
    direction: string;
    valueDate: string;
    status: string;
    unmatchedReason: string | null;
    note: string | null;
  } | null;
  clientMoneyAccountId: string | null;
};

export type BankFeedStatus = { lastSucceededAt: string | null; lastFailedAt: string | null; lastFailure: string | null };

/** R21: a run with its items. */
export type BankReconciliation = BankReconciliationListItem & {
  paymentsInFlight: { count: number; amount: number };
  feed: BankFeedStatus;
  items: ReconciliationItem[];
};

/** R19: one client money account and its latest run. */
export type ReconciliationOverviewEntry = {
  clientMoneyAccount: ReconciliationAccount;
  bankFeed: BankFeedStatus | null;
  latest: BankReconciliationListItem | null;
};

export type BankReconciliationListArgs = { clientMoneyAccountId?: string; status?: string; page?: number };

export const bankReconciliationsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getBankReconciliationOverview: builder.query<PagedList<ReconciliationOverviewEntry>, void>({
      query: () => "/bank-reconciliations/overview",
      providesTags: ["BankReconciliation"],
    }),
    getBankReconciliations: builder.query<PagedList<BankReconciliationListItem>, BankReconciliationListArgs>({
      query: ({ clientMoneyAccountId, status, page }) => {
        const params = new URLSearchParams();
        if (clientMoneyAccountId) {
          params.set("clientMoneyAccountId", clientMoneyAccountId);
        }
        if (status) {
          params.set("status", status);
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", "20");
        return `/bank-reconciliations?${params.toString()}`;
      },
      providesTags: ["BankReconciliation"],
    }),
    getBankReconciliation: builder.query<BankReconciliation, string>({
      query: (id) => `/bank-reconciliations/${id}`,
      providesTags: (_result, _error, id) => [{ type: "BankReconciliation", id }],
    }),
    runBankReconciliation: builder.mutation<{ id: string }, { clientMoneyAccountId: string }>({
      query: (body) => ({ url: "/bank-reconciliations", method: "POST", body }),
      invalidatesTags: ["BankReconciliation"],
    }),
  }),
});

export const {
  useGetBankReconciliationOverviewQuery,
  useGetBankReconciliationsQuery,
  useGetBankReconciliationQuery,
  useRunBankReconciliationMutation,
} = bankReconciliationsApi;
