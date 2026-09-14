import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type Customer = {
  id: string;
  tenantId: string;
  adviserId: string;
  name: string;
  email: string;
  status: string;
  rowVersion: string;
  created: string;
};

export type CustomerListArgs = {
  page?: number;
  search?: string;
  enabledOnly?: boolean;
  adviserId?: string;
};

export type CreateCustomerBody = {
  name: string;
  email: string;
  password: string;
  adviserId?: string;
};

export const customersApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getCustomers: builder.query<PagedList<Customer>, CustomerListArgs | void>({
      query: (args) => {
        const params = new URLSearchParams();
        params.set("page", String(args?.page ?? 1));
        params.set("pageSize", "20");
        if (args?.search) {
          params.set("search", args.search);
        }
        if (args?.enabledOnly) {
          params.set("enabledOnly", "true");
        }
        if (args?.adviserId) {
          params.set("adviserId", args.adviserId);
        }
        return `/users/customers?${params.toString()}`;
      },
      providesTags: ["Customer"],
    }),
    getCustomer: builder.query<Customer, string>({
      query: (id) => `/users/customers/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Customer", id }],
    }),
    createCustomer: builder.mutation<{ id: string }, CreateCustomerBody>({
      query: (body) => ({
        url: "/users/customers",
        method: "POST",
        body,
      }),
      invalidatesTags: ["Customer"],
    }),
    updateCustomer: builder.mutation<
      void,
      { id: string; name: string; rowVersion: string; adviserId?: string }
    >({
      query: ({ id, ...body }) => ({
        url: `/users/customers/${id}`,
        method: "PUT",
        body,
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Customer", id: arg.id }, "Customer"],
    }),
    disableCustomer: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({
        url: `/users/customers/${id}/disable`,
        method: "POST",
        body: { rowVersion },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Customer", id: arg.id }, "Customer"],
    }),
    enableCustomer: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({
        url: `/users/customers/${id}/enable`,
        method: "POST",
        body: { rowVersion },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Customer", id: arg.id }, "Customer"],
    }),
  }),
});

export const {
  useGetCustomersQuery,
  useGetCustomerQuery,
  useCreateCustomerMutation,
  useUpdateCustomerMutation,
  useDisableCustomerMutation,
  useEnableCustomerMutation,
} = customersApi;
