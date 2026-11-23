import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** orders R15: a fill of an order. */
export type OrderFill = {
  id: string;
  quantity: number;
  price: number;
  tradeDate: string;
  settlementDate: string;
  brokerReference: string | null;
  status: string;
  holdReason: string | null;
  transactionId: string | null;
  postedAt: string | null;
  settledAt: string | null;
  /** trade-settlement R34. */
  brokerSettlementId?: string | null;
  tradeSettlementId?: string | null;
  break?: FillBreak | null;
};

/** trade-settlement R34: the broker took the fill back. */
export type FillBreak = {
  at: string;
  reason: string | null;
  outcome: string;
  holdReason: string | null;
  reversalTransactionId: string | null;
  resolvedBy: { id: string; name: string } | null;
  resolvedAt: string | null;
  note: string | null;
};

/** orders R15: an order item. `fills` is on the single read only. */
export type Order = {
  id: string;
  accountId: string;
  customerId: string;
  instrument: { id: string; symbol: string; name: string };
  side: string;
  type: string;
  timeInForce: string;
  quantity: number;
  filledQuantity: number;
  openQuantity: number;
  currency: string;
  referencePrice: number;
  referencePriceDate: string;
  priceLimit: number;
  reservedCash: number;
  reservedQuantity: number;
  averagePrice: number | null;
  status: string;
  memo: string | null;
  placedBy: { id: string; name: string };
  placedAt: string;
  send: { attempts: number; lastAttemptAt: string | null; lastFailure: string | null; acceptedAt: string | null } | null;
  rejectReason: string | null;
  cancelRequest: { by: { id: string; name: string } | null; at: string; note: string | null } | null;
  closedAt: string | null;
  closeReason: string | null;
  /** rebalances R27: the rebalance that placed the order; null on a person's order. */
  rebalanceId?: string | null;
  fills?: OrderFill[];
  rowVersion: string;
};

/** orders R17: what an order would cost or bring, before it is placed. */
export type OrderPreview = {
  currency: string;
  referencePrice: number;
  referencePriceDate: string;
  priceLimit: number;
  estimatedGross: number;
  estimatedFee: number;
  maximumGross: number;
  maximumFee: number;
  maximumCost: number | null;
  availableCash: number;
  availableQuantity: number | null;
};

export type OrderBody = { accountId: string; instrumentId: string; side: string; quantity: number; memo?: string | null };

export type OrderListArgs = { status?: string; accountId?: string; pendingFill?: boolean; page?: number };

export const ordersApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getOrders: builder.query<PagedList<Order>, OrderListArgs>({
      query: ({ status, accountId, pendingFill, page }) => {
        const params = new URLSearchParams();
        if (status) {
          params.set("status", status);
        }
        if (accountId) {
          params.set("accountId", accountId);
        }
        if (pendingFill) {
          params.set("pendingFill", "true");
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", "20");
        return `/orders?${params.toString()}`;
      },
      providesTags: ["Order"],
    }),
    getOrder: builder.query<Order, string>({
      query: (id) => `/orders/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Order", id }],
    }),
    previewOrder: builder.query<OrderPreview, OrderBody>({
      query: (body) => ({ url: "/orders/preview", method: "POST", body }),
    }),
    placeOrder: builder.mutation<{ id: string }, { body: OrderBody; idempotencyKey: string }>({
      query: ({ body, idempotencyKey }) => ({
        url: "/orders",
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["Order", "Account"],
    }),
    cancelOrder: builder.mutation<void, { id: string; note: string | null; rowVersion: string }>({
      query: ({ id, note, rowVersion }) => ({
        url: `/orders/${id}/cancel`,
        method: "POST",
        body: { note, rowVersion },
      }),
      invalidatesTags: ["Order", "Account"],
    }),
    resolveBrokenFill: builder.mutation<void, { id: string; fillId: string; note: string; rowVersion: string }>({
      query: ({ id, fillId, note, rowVersion }) => ({
        url: `/orders/${id}/fills/${fillId}/resolve-break`,
        method: "POST",
        body: { note, rowVersion },
      }),
      invalidatesTags: ["Order", "Account"],
    }),
  }),
});

export const {
  useGetOrdersQuery,
  useGetOrderQuery,
  useLazyPreviewOrderQuery,
  usePlaceOrderMutation,
  useCancelOrderMutation,
  useResolveBrokenFillMutation,
} = ordersApi;
