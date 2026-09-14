import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { BaseQueryFn, FetchArgs, FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { RootState } from "@/app/store";
import { refreshTokens, startAuthorize } from "@/features/session/oidc";
import { clearSession, setTokens, type CurrentUser } from "@/features/session/sessionSlice";
import type { Tenant } from "@/shared/types/tenant";

const rawBaseQuery = fetchBaseQuery({
  prepareHeaders: (headers, { getState }) => {
    const accessToken = (getState() as RootState).session.accessToken;
    if (accessToken) {
      headers.set("Authorization", `Bearer ${accessToken}`);
    }
    return headers;
  },
});

const baseQuery: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> = async (
  args,
  api,
  extraOptions,
) => {
  const baseUrl = (import.meta.env.VITE_WEBAPI_BASE_URL ?? "").replace(/\/$/, "");
  const adjusted: string | FetchArgs =
    typeof args === "string" ? `${baseUrl}${args}` : { ...args, url: `${baseUrl}${args.url}` };

  let result = await rawBaseQuery(adjusted, api, extraOptions);
  if (result.error?.status !== 401) {
    return result;
  }

  const refreshToken = (api.getState() as RootState).session.refreshToken;
  if (refreshToken) {
    const tokens = await refreshTokens(refreshToken);
    if (tokens) {
      api.dispatch(setTokens(tokens));
      return rawBaseQuery(adjusted, api, extraOptions);
    }
  }

  api.dispatch(clearSession());
  await startAuthorize();
  return result;
};

export const api = createApi({
  reducerPath: "api",
  baseQuery,
  endpoints: (builder) => ({
    getMe: builder.query<CurrentUser, void>({
      query: () => "/users/me",
    }),
    getTenantByCode: builder.query<Tenant, string>({
      query: (code) => `/tenants/${encodeURIComponent(code)}`,
    }),
  }),
});

export const { useGetMeQuery, useGetTenantByCodeQuery } = api;
