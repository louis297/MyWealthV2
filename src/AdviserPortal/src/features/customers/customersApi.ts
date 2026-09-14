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
    createCustomer: builder.mutation<{ id: string }, CreateCustomerBody>({
      query: (body) => ({
        url: "/users/customers",
        method: "POST",
        body,
      }),
      invalidatesTags: ["Customer"],
    }),
  }),
});

export const { useGetCustomersQuery, useCreateCustomerMutation } = customersApi;
