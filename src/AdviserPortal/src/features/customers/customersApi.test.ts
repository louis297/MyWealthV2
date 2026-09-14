import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { customersApi } from "@/features/customers/customersApi";
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

const envelope = {
  items: [
    {
      id: "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc",
      tenantId: "22222222-2222-2222-2222-222222222222",
      adviserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      name: "Jordan Lee",
      email: "jordan@north.example",
      status: "active",
      rowVersion: "AAAA",
      created: "2026-09-14T00:00:00+00:00",
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
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

describe("customers API", () => {
  beforeEach(() => {
    startAuthorize.mockReset();
    sessionStorage.clear();
    vi.stubEnv("VITE_WEBAPI_BASE_URL", "https://webapi.test");
    vi.stubGlobal("fetch", vi.fn());
  });

  it("GETs /users/customers with list filters and pageSize 20", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(envelope), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    const result = await store.dispatch(
      customersApi.endpoints.getCustomers.initiate({
        page: 2,
        search: "jordan",
        enabledOnly: true,
        adviserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      }),
    );

    expect(result.data).toEqual(envelope);
    const request = fetchMock.mock.calls[0][0] as Request;
    const url = new URL(request.url);
    expect(url.origin + url.pathname).toBe("https://webapi.test/users/customers");
    expect(url.searchParams.get("page")).toBe("2");
    expect(url.searchParams.get("pageSize")).toBe("20");
    expect(url.searchParams.get("search")).toBe("jordan");
    expect(url.searchParams.get("enabledOnly")).toBe("true");
    expect(url.searchParams.get("adviserId")).toBe("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    expect(request.headers.get("Authorization")).toBe("Bearer access-1");
  });

  it("POSTs /users/customers and returns the created id", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ id: "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc" }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    const result = await store.dispatch(
      customersApi.endpoints.createCustomer.initiate({
        name: "Jordan Lee",
        email: "jordan@north.example",
        password: "Passw0rd!",
        adviserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      }),
    );

    expect(result.data).toEqual({ id: "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc" });
    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("https://webapi.test/users/customers");
    expect(request.method).toBe("POST");
    expect(JSON.parse(await request.text())).toEqual({
      name: "Jordan Lee",
      email: "jordan@north.example",
      password: "Passw0rd!",
      adviserId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    });
  });
});
