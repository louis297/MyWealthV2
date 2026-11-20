import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** withdrawals R13, R16: a nominated bank account as its Customer reads it. */
export type CustomerBankAccount = {
  id: string;
  currency: string;
  bankName: string;
  accountName: string;
  accountNumber: string;
  status: string;
};

export const bankAccountsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getMyBankAccounts: builder.query<PagedList<CustomerBankAccount>, void>({
      query: () => "/nominated-bank-accounts?status=Pending,Verified&page=1&pageSize=100",
      providesTags: ["NominatedBankAccount"],
    }),
  }),
});

export const { useGetMyBankAccountsQuery } = bankAccountsApi;
