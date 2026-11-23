import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** trade-settlement §8.1: a fill of a trade settlement. */
export type TradeSettlementFill = {
  id: string;
  orderId: string;
  accountId: string;
  side: string;
  quantity: number;
  price: number;
  gross: number;
  status: string;
};

/** trade-settlement §8.1: a settlement payment to the broker. */
export type TradeSettlementPayment = { status: string; attempts: number; lastFailure: string | null; createdAt: string };

/** trade-settlement R16, §8.1: a trade settlement. `fills` and `payments` are on the single read only. */
export type TradeSettlement = {
  id: string;
  clientMoneyAccount: { id: string; currency: string; accountNumber: string };
  connection: string;
  brokerSettlementId: string;
  settledDate: string;
  fillCount: number;
  netAmount: number;
  broker: { fillCount: number; netAmount: number; settlementAccount: string };
  status: string;
  failure: string | null;
  failureDetail: string | null;
  completedAt: string | null;
  statementLineId: string | null;
  setAside: { by: { id: string; name: string } | null; at: string; note: string } | null;
  payments?: TradeSettlementPayment[];
  fills?: TradeSettlementFill[];
  rowVersion: string;
};

export type TradeSettlementListArgs = { status?: string; open?: boolean; page?: number };

export const tradeSettlementsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getTradeSettlements: builder.query<PagedList<TradeSettlement>, TradeSettlementListArgs>({
      query: ({ status, open, page }) => {
        const params = new URLSearchParams();
        if (status) {
          params.set("status", status);
        }
        if (open) {
          params.set("open", "true");
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", "20");
        return `/trade-settlements?${params.toString()}`;
      },
      providesTags: ["TradeSettlement"],
    }),
    getTradeSettlement: builder.query<TradeSettlement, string>({
      query: (id) => `/trade-settlements/${id}`,
      providesTags: (_result, _error, id) => [{ type: "TradeSettlement", id }],
    }),
    payTradeSettlement: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({ url: `/trade-settlements/${id}/pay`, method: "POST", body: { rowVersion } }),
      invalidatesTags: ["TradeSettlement"],
    }),
    setAsideTradeSettlement: builder.mutation<void, { id: string; rowVersion: string; note: string }>({
      query: ({ id, rowVersion, note }) => ({ url: `/trade-settlements/${id}/set-aside`, method: "POST", body: { rowVersion, note } }),
      invalidatesTags: ["TradeSettlement"],
    }),
  }),
});

export const {
  useGetTradeSettlementsQuery,
  useGetTradeSettlementQuery,
  usePayTradeSettlementMutation,
  useSetAsideTradeSettlementMutation,
} = tradeSettlementsApi;
