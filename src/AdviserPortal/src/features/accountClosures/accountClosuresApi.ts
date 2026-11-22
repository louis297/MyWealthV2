import { api } from "@/shared/api/api";

/** account-closure R2: the request body; the Account is the path's. */
export type RequestAccountClosureBody = {
  nominatedBankAccountId: string | null;
  memo: string;
};

export const accountClosuresApi = api.injectEndpoints({
  endpoints: (builder) => ({
    requestAccountClosure: builder.mutation<
      { id: string },
      { accountId: string; body: RequestAccountClosureBody; idempotencyKey: string }
    >({
      query: ({ accountId, body, idempotencyKey }) => ({
        url: `/accounts/${accountId}/closures`,
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Account", id: arg.accountId }, "Account", "Withdrawal"],
    }),
  }),
});

export const { useRequestAccountClosureMutation } = accountClosuresApi;
