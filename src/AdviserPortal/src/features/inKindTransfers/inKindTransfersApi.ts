import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

type PersonRef = { id: string; name: string };

/** in-kind-transfers R28, §8.1: an in-kind transfer item. */
export type InKindTransfer = {
  id: string;
  accountId: string;
  customerId: string;
  direction: string;
  instrument: { id: string; symbol: string; name: string };
  quantity: number;
  currency: string;
  cost: number | null;
  acquiredOn: string | null;
  changesBeneficialOwner: boolean;
  counterparty: { name: string; account: string; holderName: string };
  memo: string | null;
  status: string;
  requestedBy: PersonRef;
  requestedAt: string;
  decision: { action: string; by: PersonRef; at: string; note: string | null } | null;
  send: {
    attempts: number;
    lastAttemptAt: string | null;
    lastFailure: string | null;
    acceptedAt: string | null;
    custodianTransferId: string | null;
  } | null;
  failureReason: string | null;
  setAside: { by: PersonRef; at: string; note: string } | null;
  transactionId: string | null;
  completedAt: string | null;
  rowVersion: string;
};

export type InKindTransferListArgs = { status?: string; accountId?: string; page?: number; pageSize?: number };

/** in-kind-transfers §8.1: a transfer out sends no cost and no acquiredOn. */
export type RequestInKindTransferBody = {
  accountId: string;
  direction: "In" | "Out";
  instrumentId: string;
  quantity: number;
  cost?: number;
  acquiredOn?: string | null;
  changesBeneficialOwner: boolean;
  counterparty: { name: string; account: string; holderName: string };
  memo: string | null;
};

export type InKindTransferAction = "approve" | "reject" | "cancel" | "set-aside";

export const inKindTransfersApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getInKindTransfers: builder.query<PagedList<InKindTransfer>, InKindTransferListArgs>({
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
        return `/in-kind-transfers?${params.toString()}`;
      },
      providesTags: ["InKindTransfer"],
    }),
    getInKindTransfer: builder.query<InKindTransfer, string>({
      query: (id) => `/in-kind-transfers/${id}`,
      providesTags: (_result, _error, id) => [{ type: "InKindTransfer", id }],
    }),
    requestInKindTransfer: builder.mutation<{ id: string }, { body: RequestInKindTransferBody; idempotencyKey: string }>({
      query: ({ body, idempotencyKey }) => ({
        url: "/in-kind-transfers",
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["InKindTransfer", "Account"],
    }),
    actOnInKindTransfer: builder.mutation<void, { id: string; action: InKindTransferAction; note: string | null; rowVersion: string }>({
      query: ({ id, action, note, rowVersion }) => ({
        url: `/in-kind-transfers/${id}/${action}`,
        method: "POST",
        body: { note, rowVersion },
      }),
      invalidatesTags: ["InKindTransfer", "Account", "Transaction"],
    }),
  }),
});

export const {
  useGetInKindTransfersQuery,
  useGetInKindTransferQuery,
  useRequestInKindTransferMutation,
  useActOnInKindTransferMutation,
} = inKindTransfersApi;
