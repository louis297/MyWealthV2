import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

type InstrumentRef = { id: string; symbol: string; name: string };
type PersonRef = { id: string; name: string };

/** rebalances R9, §8.1: a line of a proposal. A line the plan leaves alone has the six trade fields null. */
export type RebalanceLine = {
  instrument: InstrumentRef;
  quantity: number;
  weight: number;
  targetWeight: number | null;
  side: string | null;
  tradeQuantity: number | null;
  referencePrice: number | null;
  referencePriceDate: string | null;
  estimatedGross: number | null;
  estimatedFee: number | null;
  currency: string;
};

/** rebalances §8.1: an order a rebalance placed, with its wave. */
export type RebalanceOrder = {
  id: string;
  wave: number;
  side: string;
  instrument: InstrumentRef;
  quantity: number;
  filledQuantity: number;
  status: string;
};

/** rebalances §8.1: a rebalance. `targets`, `lines` and `orders` are on the single read only. */
export type Rebalance = {
  id: string;
  accountId: string;
  customerId: string;
  portfolio: { id: string; name: string };
  trigger: string;
  status: string;
  baseCurrency: string;
  totalValue: number | null;
  cash: number | null;
  minimumTrade: number;
  cashResidualPercent: number;
  targets?: { instrument: InstrumentRef; targetWeight: number }[];
  lines?: RebalanceLine[];
  orders?: RebalanceOrder[];
  waves: number;
  failure: { code: string; message: string } | null;
  proposedBy: PersonRef | null;
  proposedAt: string;
  expiresAt: string;
  confirmedBy: PersonRef | null;
  confirmedAt: string | null;
  cancelRequest: { by: PersonRef | null; at: string; note: string | null } | null;
  cancelReason: string | null;
  followUp: boolean;
  endedAt: string | null;
  rowVersion: string;
};

/** rebalances R10: the answer of a route that proposes. */
export type RebalanceProposal = { status: string; rebalanceId: string };

export type RebalanceListArgs = { status?: string; open?: boolean; portfolioId?: string; accountId?: string; page?: number; pageSize?: number };

export const rebalancesApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getRebalances: builder.query<PagedList<Rebalance>, RebalanceListArgs>({
      query: ({ status, open, portfolioId, accountId, page, pageSize }) => {
        const params = new URLSearchParams();
        if (status) {
          params.set("status", status);
        }
        if (open) {
          params.set("open", "true");
        }
        if (portfolioId) {
          params.set("portfolioId", portfolioId);
        }
        if (accountId) {
          params.set("accountId", accountId);
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", String(pageSize ?? 20));
        return `/rebalances?${params.toString()}`;
      },
      providesTags: ["Rebalance"],
    }),
    getRebalance: builder.query<Rebalance, string>({
      query: (id) => `/rebalances/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Rebalance", id }],
    }),
    confirmRebalance: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({ url: `/rebalances/${id}/confirm`, method: "POST", body: { rowVersion } }),
      invalidatesTags: ["Rebalance", "Order", "Account"],
    }),
    cancelRebalance: builder.mutation<void, { id: string; rowVersion: string; note: string | null }>({
      query: ({ id, rowVersion, note }) => ({ url: `/rebalances/${id}/cancel`, method: "POST", body: { rowVersion, note } }),
      invalidatesTags: ["Rebalance", "Order", "Account"],
    }),
  }),
});

export const {
  useGetRebalancesQuery,
  useLazyGetRebalancesQuery,
  useGetRebalanceQuery,
  useConfirmRebalanceMutation,
  useCancelRebalanceMutation,
} = rebalancesApi;
