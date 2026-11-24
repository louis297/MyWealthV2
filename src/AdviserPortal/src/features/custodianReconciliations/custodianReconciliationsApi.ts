import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** in-kind-transfers R59: the safekeeping account of a run or an overview entry. */
export type ReconciledSafekeepingAccount = {
  id: string;
  currency: string;
  custodianName: string;
  accountName: string;
  accountNumber: string;
  isActive: boolean;
};

export type CustodianFeedState = { lastSucceededAt: string | null; lastFailedAt: string | null; lastFailure: string | null };

/** R59: a run without its lines. */
export type CustodianReconciliationListItem = {
  id: string;
  safekeepingAccount: ReconciledSafekeepingAccount;
  trigger: string;
  ranAt: string;
  ranBy: { id: string; name: string } | null;
  status: string;
  positionsAt: string | null;
  breakCount: number;
  timingCount: number;
  agreedCount: number;
  transfersInFlight: number;
  fillsNotDelivered: number;
};

export type CustodianReconciliationLine = {
  instrument: { id: string; symbol: string; name: string } | null;
  symbol: string;
  custodianQuantity: number;
  platformQuantity: number;
  difference: number;
  notProcessed: number;
  unsettledTrades: number;
  suspense: number;
  unbookedFills: number;
  unexplained: number;
  class: string;
};

export type CustodianReconciliation = CustodianReconciliationListItem & {
  feed: CustodianFeedState;
  lines: CustodianReconciliationLine[];
};

export type CustodianReconciliationOverviewEntry = {
  safekeepingAccount: ReconciledSafekeepingAccount;
  custodianFeed: (CustodianFeedState & { positionsAt?: string | null }) | null;
  latest: CustodianReconciliationListItem | null;
};

export const custodianReconciliationsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getCustodianReconciliationOverview: builder.query<PagedList<CustodianReconciliationOverviewEntry>, void>({
      query: () => "/custodian-reconciliations/overview",
      providesTags: ["CustodianReconciliation"],
    }),
    getCustodianReconciliations: builder.query<PagedList<CustodianReconciliationListItem>, { safekeepingAccountId?: string; page?: number }>({
      query: ({ safekeepingAccountId, page }) => {
        const params = new URLSearchParams();
        if (safekeepingAccountId) {
          params.set("safekeepingAccountId", safekeepingAccountId);
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", "20");
        return `/custodian-reconciliations?${params.toString()}`;
      },
      providesTags: ["CustodianReconciliation"],
    }),
    getCustodianReconciliation: builder.query<CustodianReconciliation, string>({
      query: (id) => `/custodian-reconciliations/${id}`,
      providesTags: (_result, _error, id) => [{ type: "CustodianReconciliation", id }],
    }),
    runCustodianReconciliation: builder.mutation<{ id: string }, { safekeepingAccountId: string }>({
      query: (body) => ({ url: "/custodian-reconciliations", method: "POST", body }),
      invalidatesTags: ["CustodianReconciliation"],
    }),
  }),
});

export const {
  useGetCustodianReconciliationOverviewQuery,
  useGetCustodianReconciliationsQuery,
  useGetCustodianReconciliationQuery,
  useRunCustodianReconciliationMutation,
} = custodianReconciliationsApi;
