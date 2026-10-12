import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { DashboardPage } from "@/features/dashboard/DashboardPage";
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

const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";

const withBreakdowns = {
  ...netWorth,
  breakdowns: {
    byCustomer: [
      {
        customerId,
        name: "Amy Able",
        currencies: [
          {
            currency: "NZD",
            assets: { cash: 1000, holdings: 5000, total: 6000 },
            liabilities: { total: 0 },
            net: 6000,
          },
        ],
      },
    ],
    byAccountType: [
      {
        type: "Brokerage",
        currencies: [
          {
            currency: "NZD",
            assets: { cash: 1000, holdings: 5000, total: 6000 },
            liabilities: { total: 0 },
            net: 6000,
          },
        ],
      },
    ],
    topHoldings: [
      {
        instrumentId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
        symbol: "DEMO",
        quantity: 100,
        marketValue: 5000,
        currency: "NZD",
      },
      {
        instrumentId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
        symbol: "SMALL",
        quantity: 2,
        marketValue: 40,
        currency: "USD",
      },
    ],
  },
};

function requestedUrls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  return fetchMock.mock.calls.map(([input]) => {
    if (typeof input === "string") {
      return input;
    }
    if (input instanceof Request) {
      return input.url;
    }
    return String(input);
  });
}

function renderDashboard(body: unknown) {
  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });

  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input) => {
    const url = typeof input === "string" ? input : input instanceof Request ? input.url : String(input);
    if (url.endsWith("/net-worth")) {
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }
    return new Response("", { status: 404 });
  });

  const router = createMemoryRouter(
    [
      { path: "/dashboard", element: <DashboardPage /> },
      { path: "/customers/:id", element: <p>customer detail</p> },
    ],
    { initialEntries: ["/dashboard"] },
  );

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock, router };
}

describe("DashboardPage", () => {
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

  it("renders per-currency assets, liabilities, and net from GET /net-worth", async () => {
    const { fetchMock } = renderDashboard(netWorth);

    const nzd = await screen.findByRole("row", { name: /NZD/ });
    expect(within(nzd).getByText("1000")).toBeInTheDocument();
    expect(within(nzd).getByText("5000")).toBeInTheDocument();
    expect(within(nzd).getAllByText("6000")).toHaveLength(2);
    expect(within(nzd).getByText("0")).toBeInTheDocument();

    const usd = screen.getByRole("row", { name: /USD/ });
    expect(within(usd).getByText("5")).toBeInTheDocument();
    expect(within(usd).getByText("15")).toBeInTheDocument();
    expect(within(usd).getAllByText("20")).toHaveLength(2);
    expect(within(usd).getByText("0")).toBeInTheDocument();

    expect(screen.getByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: /account/i })).not.toBeInTheDocument();

    const urls = requestedUrls(fetchMock);
    expect(urls).toEqual(["http://localhost:3000/api/net-worth"]);
    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("renders by customer, by account type, and top holdings in API order", async () => {
    const { fetchMock } = renderDashboard(withBreakdowns);

    expect(await screen.findByRole("heading", { name: "By customer" })).toBeInTheDocument();
    expect(screen.getByText("Amy Able")).toBeInTheDocument();

    expect(screen.getByRole("heading", { name: "By account type" })).toBeInTheDocument();
    expect(screen.getByText("Brokerage")).toBeInTheDocument();

    const holdings = screen.getByRole("table", { name: "Top holdings" });
    const symbols = within(holdings)
      .getAllByRole("row")
      .slice(1)
      .map((row) => within(row).getAllByRole("cell")[0]?.textContent);
    expect(symbols).toEqual(["DEMO", "SMALL"]);
    expect(within(holdings).getByText("5000")).toBeInTheDocument();
    expect(within(holdings).getByText("40")).toBeInTheDocument();

    expect(requestedUrls(fetchMock)).toEqual(["http://localhost:3000/api/net-worth"]);
    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("links each customer to their detail and shows that customer's net", async () => {
    const { router } = renderDashboard(withBreakdowns);

    const link = await screen.findByRole("link", { name: "Amy Able" });
    expect(link).toHaveAttribute("href", `/customers/${customerId}`);

    const row = screen.getByRole("row", { name: /Amy Able/ });
    expect(within(row).getByText("NZD")).toBeInTheDocument();
    expect(within(row).getByText("6000")).toBeInTheDocument();

    fireEvent.click(link);
    expect(router.state.location.pathname).toBe(`/customers/${customerId}`);
    expect(screen.getByText("customer detail")).toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows no balances when the read has no currencies", async () => {
    renderDashboard({
      ...netWorth,
      currencies: [],
      breakdowns: { byCustomer: [], byAccountType: [], topHoldings: [] },
    });

    expect(await screen.findByText("No balances.")).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: /NZD|USD/ })).not.toBeInTheDocument();
    expect(screen.queryByText("Hidden Customer")).not.toBeInTheDocument();
  });
});
