import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { dashboardApi } from "@/features/dashboard/dashboardApi";
import { sessionSlice } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";

const netWorth = {
  asOf: "2026-10-12T16:00:00.000Z",
  currencies: [
    {
      currency: "NZD",
      assets: { cash: 1000, holdings: 5000, total: 6000 },
      liabilities: { total: 0 },
      net: 6000,
    },
    {
      currency: "USD",
      assets: { cash: 20, holdings: 0, total: 20 },
      liabilities: { total: 5 },
      net: 15,
    },
  ],
  breakdowns: {
    byCustomer: [],
    byAccountType: [],
    topHoldings: [],
  },
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

describe("dashboard API", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/dashboard",
      search: "",
      href: "http://localhost:3000/dashboard",
      assign: vi.fn(),
    });
  });

  it("GETs /net-worth with credentials and no Authorization", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(netWorth), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const store = createStore();
    const result = await store.dispatch(dashboardApi.endpoints.getNetWorth.initiate());

    expect(result.data).toEqual(netWorth);
    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.url).toBe("http://localhost:3000/api/net-worth");
    expect(request.method).toBe("GET");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
  });
});
