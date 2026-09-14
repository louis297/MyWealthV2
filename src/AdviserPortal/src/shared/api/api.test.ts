import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { sessionSlice, setTokens } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const startAuthorize = vi.fn();

vi.mock("@/features/session/oidc", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/session/oidc")>();
  return {
    ...actual,
    startAuthorize: () => startAuthorize(),
  };
});

const me = {
  id: "11111111-1111-1111-1111-111111111111",
  name: "System Admin",
  email: "system-admin@localhost",
  role: "systemAdmin",
  status: "active",
  tenantId: null,
  tenantCode: null,
  adviserId: null,
  rowVersion: "AAAA",
};

function createStore() {
  return configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
}

describe("GET /users/me", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubEnv("VITE_IDENTITY_AUTHORITY", "https://identity.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("sends the access token and returns the profile", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(me), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    const result = await store.dispatch(api.endpoints.getMe.initiate());

    expect(result.data).toEqual(me);
    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("https://webapi.test/users/me");
    expect(request.headers.get("Authorization")).toBe("Bearer access-1");
  });

  it("refreshes once on 401 and retries", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock
      .mockResolvedValueOnce(new Response("", { status: 401 }))
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            authorization_endpoint: "https://identity.test/connect/authorize",
            token_endpoint: "https://identity.test/connect/token",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({ access_token: "access-2", refresh_token: "refresh-2" }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify(me), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    const result = await store.dispatch(api.endpoints.getMe.initiate());

    expect(result.data).toEqual(me);
    expect(store.getState().session.accessToken).toBe("access-2");
    expect(fetchMock.mock.calls[2][0]).toBe("https://identity.test/connect/token");
    const refreshBody = new URLSearchParams(fetchMock.mock.calls[2][1]?.body as string);
    expect(refreshBody.get("grant_type")).toBe("refresh_token");
    expect(refreshBody.get("refresh_token")).toBe("refresh-1");
    expect(startAuthorize).not.toHaveBeenCalled();
  });

  it("clears the session and re-authorizes when refresh fails", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock
      .mockResolvedValueOnce(new Response("", { status: 401 }))
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            authorization_endpoint: "https://identity.test/connect/authorize",
            token_endpoint: "https://identity.test/connect/token",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
      )
      .mockResolvedValueOnce(new Response("", { status: 400 }));

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    await store.dispatch(api.endpoints.getMe.initiate());

    expect(store.getState().session.accessToken).toBeNull();
    expect(startAuthorize).toHaveBeenCalledOnce();
  });
});

const tenant = {
  id: "22222222-2222-2222-2222-222222222222",
  name: "North Advisory",
  code: "north-advisory",
  reportingCurrency: "GBP",
  isEnabled: true,
  rowVersion: "AAAA",
  created: "2026-09-14T00:00:00+00:00",
};

describe("GET /tenants/by-code/{code}", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubEnv("VITE_IDENTITY_AUTHORITY", "https://identity.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("sends the access token and returns the tenant", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(tenant), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    const result = await store.dispatch(
      api.endpoints.getTenantByCode.initiate("north-advisory"),
    );

    expect(result.data).toEqual(tenant);
    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("https://webapi.test/tenants/by-code/north-advisory");
    expect(request.headers.get("Authorization")).toBe("Bearer access-1");
  });
});
