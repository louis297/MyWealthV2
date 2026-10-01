import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { BaseQueryFn, FetchArgs, FetchBaseQueryError } from "@reduxjs/toolkit/query";
import { isSignOutInProgress, startLogin } from "@/features/session/session";
import { clearSession, type CurrentUser } from "@/features/session/sessionSlice";
import type { Tenant } from "@/shared/types/tenant";

const rawBaseQuery = fetchBaseQuery({
  baseUrl: `${typeof window === "undefined" ? "" : window.location.origin}/api`,
  credentials: "include",
  prepareHeaders: (headers, { type }) => {
    if (type === "mutation") {
      headers.set("X-MyWealth-Request", "1");
    }
    return headers;
  },
});

const baseQuery: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> = async (
  args,
  api,
  extraOptions,
) => {
  const result = await rawBaseQuery(args, api, extraOptions);
  if (result.error?.status !== 401) {
    return result;
  }

  api.dispatch(clearSession());
  if (!isSignOutInProgress()) {
    const path = `${window.location.pathname || "/"}${window.location.search || ""}`;
    startLogin(path);
  }
  return result;
};

export const api = createApi({
  reducerPath: "api",
  baseQuery,
  tagTypes: ["Me", "Tenant", "Customer", "Adviser"],
  endpoints: (builder) => ({
    getMe: builder.query<CurrentUser, void>({
      query: () => "/users/me",
      providesTags: ["Me"],
    }),
    getTenantByCode: builder.query<Tenant, string>({
      query: (code) => `/tenants/by-code/${encodeURIComponent(code)}`,
      providesTags: ["Tenant"],
    }),
  }),
});

export const { useGetMeQuery, useGetTenantByCodeQuery } = api;
