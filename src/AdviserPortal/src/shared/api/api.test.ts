import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { customersApi } from "@/features/customers/customersApi";
import { sessionSlice, setTokens } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

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

function requestOf(call: unknown): Request {
  return call as Request;
}

describe("BFF API client", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/customers/abc",
      search: "",
      href: "http://localhost:3000/customers/abc",
      assign: vi.fn(),
    });
  });

  it("loads the profile from /api with credentials and no Authorization", async () => {
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
    const request = requestOf(fetchMock.mock.calls[0][0]);
    expect(request.url).toBe("http://localhost:3000/api/users/me");
    expect(request.credentials).toBe("include");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.headers.get("X-MyWealth-Request")).toBeNull();
    expect(sessionStorage.getItem("adviser-portal.accessToken")).toBeNull();
    expect(sessionStorage.getItem("adviser-portal.refreshToken")).toBeNull();
  });

  it("sends X-MyWealth-Request on a mutation and does not call identity", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ id: "cust" }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));
    await store.dispatch(
      customersApi.endpoints.createCustomer.initiate({
        name: "Ada",
        email: "ada@firm.example",
        password: "Password1!",
      }),
    );

    const request = requestOf(fetchMock.mock.calls[0][0]);
    expect(request.url).toBe("http://localhost:3000/api/users/customers");
    expect(request.method).toBe("POST");
    expect(request.credentials).toBe("include");
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(fetchMock.mock.calls.some((call) => String(requestOf(call[0]).url).includes("/connect/"))).toBe(
      false,
    );
  });

  it("on 401 sends the browser to /bff/login and does not refresh", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(new Response("", { status: 401 }));
    const assign = vi.fn();
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/customers/abc",
      search: "",
      href: "http://localhost:3000/customers/abc",
      assign,
    });

    const store = createStore();
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));
    await store.dispatch(api.endpoints.getMe.initiate());

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(assign).toHaveBeenCalledWith("/bff/login?returnUrl=%2Fcustomers%2Fabc");
    expect(sessionStorage.getItem("adviser-portal.accessToken")).toBeNull();
  });
});
