import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** in-kind-transfers R19: a custody movement item. */
export type CustodyMovement = {
  id: string;
  safekeepingAccountId: string;
  direction: string;
  kind: string | null;
  symbol: string;
  currency: string;
  instrument: { id: string; symbol: string; name: string } | null;
  quantity: number;
  settlementDate: string;
  instructionId: string | null;
  tradeReference: string | null;
  corporateActionReference: string | null;
  counterpartyName: string | null;
  counterpartyAccount: string | null;
  reference: string | null;
  receivedAt: string;
  status: string;
  unmatchedReason: string | null;
  method: string | null;
  accountId: string | null;
  transactionId: string | null;
  inKindTransferId: string | null;
  orderId: string | null;
  fillId: string | null;
  corporateActionId: string | null;
  lastAction: { action: string; by: { id: string; name: string }; at: string; note: string | null } | null;
  rowVersion: string;
};

export type CustodyMovementListArgs = { status?: string; instrumentId?: string; page?: number; pageSize?: number };

export const custodyMovementsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getCustodyMovements: builder.query<PagedList<CustodyMovement>, CustodyMovementListArgs>({
      query: ({ status, instrumentId, page, pageSize }) => {
        const params = new URLSearchParams();
        if (status) {
          params.set("status", status);
        }
        if (instrumentId) {
          params.set("instrumentId", instrumentId);
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", String(pageSize ?? 20));
        return `/custody-movements?${params.toString()}`;
      },
      providesTags: ["CustodyMovement"],
    }),
    actOnCustodyMovement: builder.mutation<void, { id: string; action: "ignore" | "reopen"; note: string | null; rowVersion: string }>({
      query: ({ id, action, note, rowVersion }) => ({
        url: `/custody-movements/${id}/${action}`,
        method: "POST",
        body: { note, rowVersion },
      }),
      invalidatesTags: ["CustodyMovement", "CustodianReconciliation"],
    }),
  }),
});

export const { useGetCustodyMovementsQuery, useActOnCustodyMovementMutation } = custodyMovementsApi;
