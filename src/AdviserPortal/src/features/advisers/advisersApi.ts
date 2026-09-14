import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type Adviser = {
  id: string;
  tenantId: string;
  name: string;
  email: string;
  status: string;
  rowVersion: string;
  created: string;
};

export type AdviserListArgs = {
  page?: number;
  pageSize?: number;
  search?: string;
  enabledOnly?: boolean;
};

export const advisersApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getAdvisers: builder.query<PagedList<Adviser>, AdviserListArgs | void>({
      query: (args) => {
        const params = new URLSearchParams();
        params.set("page", String(args?.page ?? 1));
        params.set("pageSize", String(args?.pageSize ?? 20));
        if (args?.search) {
          params.set("search", args.search);
        }
        if (args?.enabledOnly) {
          params.set("enabledOnly", "true");
        }
        return `/users/advisers?${params.toString()}`;
      },
      providesTags: ["Adviser"],
    }),
  }),
});

export const { useGetAdvisersQuery } = advisersApi;
