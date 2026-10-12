import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type Account = {
  id: string;
  tenantId: string;
  customerId: string;
  name: string;
  type: string;
  currency: string;
  cashBalance: number;
  status: string;
  isActive: boolean;
  rowVersion: string;
};

export type AccountListArgs = {
  page?: number;
};

export const accountsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getAccounts: builder.query<PagedList<Account>, AccountListArgs | void>({
      query: (args) => {
        const params = new URLSearchParams();
        params.set("page", String(args?.page ?? 1));
        params.set("pageSize", "20");
        return `/accounts?${params.toString()}`;
      },
      providesTags: ["Account"],
    }),
  }),
});

export const { useGetAccountsQuery } = accountsApi;
