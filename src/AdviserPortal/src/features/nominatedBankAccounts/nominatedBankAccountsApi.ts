import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

type PersonEvent = { by: { id: string; name: string }; at: string; note?: string | null };

/** withdrawals §8.1: a Customer's nominated bank account. The number is full here (R3). */
export type NominatedBankAccount = {
  id: string;
  customerId: string;
  currency: string;
  bankName: string;
  accountName: string;
  accountNumber: string;
  status: string;
  added: PersonEvent;
  review: PersonEvent | null;
  removal: PersonEvent | null;
  rowVersion: string;
};

export type NominatedBankAccountListArgs = { customerId?: string; status?: string; currency?: string; page?: number };

export type AddNominatedBankAccountBody = {
  customerId: string;
  currency: string;
  bankName: string;
  accountName: string;
  accountNumber: string;
};

export type NominatedBankAccountAction = "verify" | "reject" | "remove";

export const nominatedBankAccountsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getNominatedBankAccounts: builder.query<PagedList<NominatedBankAccount>, NominatedBankAccountListArgs>({
      query: ({ customerId, status, currency, page }) => {
        const params = new URLSearchParams();
        if (customerId) {
          params.set("customerId", customerId);
        }
        if (status) {
          params.set("status", status);
        }
        if (currency) {
          params.set("currency", currency);
        }
        params.set("page", String(page ?? 1));
        params.set("pageSize", "100");
        return `/nominated-bank-accounts?${params.toString()}`;
      },
      providesTags: ["NominatedBankAccount"],
    }),
    addNominatedBankAccount: builder.mutation<{ id: string }, AddNominatedBankAccountBody>({
      query: (body) => ({ url: "/nominated-bank-accounts", method: "POST", body }),
      invalidatesTags: ["NominatedBankAccount"],
    }),
    actOnNominatedBankAccount: builder.mutation<
      void,
      { id: string; action: NominatedBankAccountAction; note: string; rowVersion: string }
    >({
      query: ({ id, action, note, rowVersion }) => ({
        url: `/nominated-bank-accounts/${id}/${action}`,
        method: "POST",
        body: { note, rowVersion },
      }),
      invalidatesTags: ["NominatedBankAccount"],
    }),
  }),
});

export const {
  useGetNominatedBankAccountsQuery,
  useAddNominatedBankAccountMutation,
  useActOnNominatedBankAccountMutation,
} = nominatedBankAccountsApi;

/** D40, D42: the status labels of a nominated bank account. */
export function bankAccountStatusLabel(status: string) {
  switch (status) {
    case "Pending":
      return "Waiting for check";
    default:
      return status;
  }
}
