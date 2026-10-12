import { configureStore } from "@reduxjs/toolkit";
import { render, screen, within } from "@testing-library/react";
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
    byCustomer: [
      {
        customerId: "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc",
        name: "Hidden Customer",
        currencies: [],
      },
    ],
    byAccountType: [{ type: "Brokerage", currencies: [] }],
    topHoldings: [
      {
        instrumentId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
        symbol: "SECRET",
        quantity: 10,
        marketValue: 5000,
        currency: "NZD",
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

  const router = createMemoryRouter([{ path: "/dashboard", element: <DashboardPage /> }], {
    initialEntries: ["/dashboard"],
  });

  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );

  return { fetchMock };
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
    expect(screen.queryByText("Hidden Customer")).not.toBeInTheDocument();
    expect(screen.queryByText("SECRET")).not.toBeInTheDocument();
    expect(screen.queryByText("Brokerage")).not.toBeInTheDocument();
    expect(screen.queryByRole("row", { name: /account/i })).not.toBeInTheDocument();

    const urls = requestedUrls(fetchMock);
    expect(urls).toEqual(["http://localhost:3000/api/net-worth"]);
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
