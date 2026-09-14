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
    getAdviser: builder.query<Adviser, string>({
      query: (id) => `/users/advisers/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Adviser", id }],
    }),
    createAdviser: builder.mutation<{ id: string }, { name: string; email: string; password: string }>({
      query: (body) => ({
        url: "/users/advisers",
        method: "POST",
        body,
      }),
      invalidatesTags: ["Adviser"],
    }),
    updateAdviser: builder.mutation<void, { id: string; name: string; rowVersion: string }>({
      query: ({ id, ...body }) => ({
        url: `/users/advisers/${id}`,
        method: "PUT",
        body,
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Adviser", id: arg.id }, "Adviser"],
    }),
    disableAdviser: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({
        url: `/users/advisers/${id}/disable`,
        method: "POST",
        body: { rowVersion },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Adviser", id: arg.id }, "Adviser"],
    }),
    enableAdviser: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({
        url: `/users/advisers/${id}/enable`,
        method: "POST",
        body: { rowVersion },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Adviser", id: arg.id }, "Adviser"],
    }),
  }),
});

export const {
  useGetAdvisersQuery,
  useGetAdviserQuery,
  useCreateAdviserMutation,
  useUpdateAdviserMutation,
  useDisableAdviserMutation,
  useEnableAdviserMutation,
} = advisersApi;
