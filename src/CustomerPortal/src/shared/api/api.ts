import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { BaseQueryFn, FetchArgs, FetchBaseQueryError } from "@reduxjs/toolkit/query";
import { isSignOutInProgress, startLogin } from "@/features/session/session";
import { clearSession, type CurrentUser } from "@/features/session/sessionSlice";

const rawBaseQuery = fetchBaseQuery({
  baseUrl: `${typeof window === "undefined" ? "" : window.location.origin}/api`,
  credentials: "include",
  prepareHeaders: (headers, { type, arg }) => {
    // The BFF rejects POST, PUT, PATCH and DELETE without this header, including
    // POST queries such as the FX quote, so decide by method as well as by type.
    if (type === "mutation" || isMutatingMethod(arg)) {
      headers.set("X-MyWealth-Request", "1");
    }
    return headers;
  },
});

function isMutatingMethod(arg: string | FetchArgs): boolean {
  const method = typeof arg === "string" ? "GET" : (arg.method ?? "GET").toUpperCase();
  return method !== "GET" && method !== "HEAD";
}

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
  tagTypes: ["Me", "NetWorth", "Account", "Transaction", "Report"],
  endpoints: (builder) => ({
    getMe: builder.query<CurrentUser, void>({
      query: () => "/users/me",
      providesTags: ["Me"],
    }),
  }),
});

export const { useGetMeQuery } = api;
