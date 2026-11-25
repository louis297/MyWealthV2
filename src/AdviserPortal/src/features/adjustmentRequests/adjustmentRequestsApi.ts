import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

type PersonRef = { id: string; name: string };

/** adjustment-review R19, §8.1: an adjustment request item. */
export type AdjustmentRequest = {
  id: string;
  accountId: string;
  customerId: string;
  reason: string;
  memo: string;
  reference: string | null;
  amount: number | null;
  currency: string;
  security: {
    instrument: { id: string; symbol: string; name: string };
    quantityChange: number;
    costChange: number;
    costCurrency: string;
  } | null;
  status: string;
  requestedBy: PersonRef;
  requestedAt: string;
  decision: { action: string; by: PersonRef; at: string; note: string | null } | null;
  transactionId: string | null;
  rowVersion: string;
};

export type AdjustmentRequestListArgs = { status?: string; accountId?: string; page?: number; pageSize?: number };

/**
 * Adjustments §8.1, adjustment-review R3: one Custodial account, a reason, a memo, and a signed cash `amount`, a
 * one-instrument holding change, or both. No `bookedAt`: the approval books it.
 */
export type RequestAdjustmentBody = {
  accountId: string;
  reason: string;
  memo: string;
  reference: string | null;
  amount?: number;
  security?: { instrumentId: string; quantityChange: number; costChange: number };
};

export type AdjustmentRequestAction = "approve" | "reject" | "cancel";

export const adjustmentRequestsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getAdjustmentRequests: builder.query<PagedList<AdjustmentRequest>, AdjustmentRequestListArgs>({
      query: ({ status, accountId, page, pageSize }) => {
        const params = new URLSearchParams();
        if (status) {
          params.set("status", status);
        }
        if (accountId) {
          params.set("accountId", accountId);
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", String(pageSize ?? 20));
        return `/adjustment-requests?${params.toString()}`;
      },
      providesTags: ["AdjustmentRequest"],
    }),
    getAdjustmentRequest: builder.query<AdjustmentRequest, string>({
      query: (id) => `/adjustment-requests/${id}`,
      providesTags: (_result, _error, id) => [{ type: "AdjustmentRequest", id }],
    }),
    requestAdjustment: builder.mutation<{ id: string }, { body: RequestAdjustmentBody; idempotencyKey: string }>({
      query: ({ body, idempotencyKey }) => ({
        url: "/adjustment-requests",
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["AdjustmentRequest"],
    }),
    actOnAdjustmentRequest: builder.mutation<void, { id: string; action: AdjustmentRequestAction; note: string | null; rowVersion: string }>({
      query: ({ id, action, note, rowVersion }) => ({
        url: `/adjustment-requests/${id}/${action}`,
        method: "POST",
        body: { note, rowVersion },
      }),
      invalidatesTags: ["AdjustmentRequest", "Account", "Transaction"],
    }),
  }),
});

export const {
  useGetAdjustmentRequestsQuery,
  useGetAdjustmentRequestQuery,
  useRequestAdjustmentMutation,
  useActOnAdjustmentRequestMutation,
} = adjustmentRequestsApi;
