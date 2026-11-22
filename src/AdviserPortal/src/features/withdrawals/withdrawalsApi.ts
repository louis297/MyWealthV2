import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** withdrawals R25: a withdrawal item. The payee number is masked. */
export type Withdrawal = {
  id: string;
  accountId: string;
  customerId: string;
  nominatedBankAccount: { id: string; bankName: string; accountName: string; accountNumber: string };
  amount: number;
  currency: string;
  reference: string;
  memo: string | null;
  status: string;
  requestedBy: { id: string; name: string };
  requestedAt: string;
  decision: { action: string; by: { id: string; name: string }; at: string; note: string | null } | null;
  payment: {
    status: string;
    attempts: number;
    lastAttemptAt: string | null;
    lastFailure: string | null;
    sentAt: string | null;
  } | null;
  payoutTransactionId: string | null;
  returnTransactionId: string | null;
  failureReason: string | null;
  returnReason: string | null;
  /** account-closure R28: the closure that created the withdrawal, else null. */
  accountClosureId?: string | null;
  rowVersion: string;
};

export type WithdrawalListArgs = { status?: string; accountId?: string; page?: number };

export type RequestWithdrawalBody = {
  accountId: string;
  nominatedBankAccountId: string;
  amount: number;
  reference: string | null;
  memo: string | null;
};

export type WithdrawalAction = "approve" | "reject" | "cancel";

export const withdrawalsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getWithdrawals: builder.query<PagedList<Withdrawal>, WithdrawalListArgs>({
      query: ({ status, accountId, page }) => {
        const params = new URLSearchParams();
        if (status) {
          params.set("status", status);
        }
        if (accountId) {
          params.set("accountId", accountId);
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", "20");
        return `/withdrawals?${params.toString()}`;
      },
      providesTags: ["Withdrawal"],
    }),
    getWithdrawal: builder.query<Withdrawal, string>({
      query: (id) => `/withdrawals/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Withdrawal", id }],
    }),
    requestWithdrawal: builder.mutation<{ id: string }, { body: RequestWithdrawalBody; idempotencyKey: string }>({
      query: ({ body, idempotencyKey }) => ({
        url: "/withdrawals",
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["Withdrawal", "Account"],
    }),
    actOnWithdrawal: builder.mutation<void, { id: string; action: WithdrawalAction; note: string | null; rowVersion: string }>({
      query: ({ id, action, note, rowVersion }) => ({
        url: `/withdrawals/${id}/${action}`,
        method: "POST",
        body: { note, rowVersion },
      }),
      invalidatesTags: ["Withdrawal", "Account"],
    }),
  }),
});

export const {
  useGetWithdrawalsQuery,
  useGetWithdrawalQuery,
  useRequestWithdrawalMutation,
  useActOnWithdrawalMutation,
} = withdrawalsApi;
