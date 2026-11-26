import { configureStore } from "@reduxjs/toolkit";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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
    byCustodyMode: [],
    topHoldings: [],
  },
};

const customerId = "cccccccccccccccc-cccc-cccc-cccc-cccccccccccc";

/** valuation-history §1.1 at 10:00: A1 opened before the range, nothing before Monday 9 in this cut. */
const series = {
  reportingCurrency: "NZD",
  from: "2026-03-07",
  to: "2026-03-11",
  days: [
    { date: "2026-03-07", currencies: [], total: 0, complete: true, accounts: 0, incompleteAccounts: 0, missingAccounts: 0 },
    { date: "2026-03-08", currencies: [], total: 0, complete: true, accounts: 0, incompleteAccounts: 0, missingAccounts: 0 },
    {
      date: "2026-03-09",
      currencies: [{ currency: "NZD", total: 56067.91, status: "Folded", fx: null, value: 56067.91 }],
      total: 56067.91,
      complete: true,
      accounts: 2,
      incompleteAccounts: 0,
      missingAccounts: 0,
    },
    {
      date: "2026-03-10",
      currencies: [{ currency: "NZD", total: 55890.52, status: "Folded", fx: null, value: 55890.52 }],
      total: 55890.52,
      complete: true,
      accounts: 2,
      incompleteAccounts: 0,
      missingAccounts: 0,
    },
    {
      date: "2026-03-11",
      currencies: [
        { currency: "NZD", total: 34750.72, status: "Folded", fx: null, value: 34750.72 },
        { currency: "USD", total: 10, status: "NoRate", fx: null, value: null },
      ],
      total: 34750.72,
      complete: false,
      accounts: 2,
      incompleteAccounts: 1,
      missingAccounts: 1,
    },
  ],
};

const currencyCatalog = [
  { code: "NZD", name: "New Zealand Dollar", decimalPlaces: 2, isActive: true },
  { code: "USD", name: "US Dollar", decimalPlaces: 2, isActive: true },
];

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
    byCustodyMode: [],
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

function renderDashboard(body: unknown, options: { series?: unknown } = {}) {
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
    if (url.includes("/net-worth/funds-under-management") && options.series) {
      return new Response(JSON.stringify(options.series), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }
    if (url.includes("/currencies")) {
      return new Response(JSON.stringify(currencyCatalog), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }
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

function currency(code: string, cash: number, holdings: number) {
  return {
    currency: code,
    assets: { cash, holdings, total: cash + holdings },
    liabilities: { total: 0 },
    net: cash + holdings,
  };
}

const withCustodyModes = {
  ...netWorth,
  breakdowns: {
    ...netWorth.breakdowns,
    byCustodyMode: [
      { custodyMode: "Custodial", currencies: [currency("NZD", 1000, 5000)] },
      { custodyMode: "External", currencies: [currency("NZD", 2500, 0), currency("USD", 0, 800)] },
    ],
  },
};

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
    expect(urls.filter((url) => url.endsWith("/net-worth"))).toEqual(["http://localhost:3000/api/net-worth"]);
    expect(urls.every((url) => /\/api\/(net-worth|currencies)/.test(url))).toBe(true);
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

    expect(requestedUrls(fetchMock).filter((url) => url.endsWith("/net-worth"))).toEqual(["http://localhost:3000/api/net-worth"]);
    const request = fetchMock.mock.calls[0][0] as Request;
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
    expect(document.querySelector('input[type="password"]')).toBeNull();
  });

  it("shows the currency net for each account type", async () => {
    renderDashboard(withBreakdowns);

    const heading = await screen.findByRole("heading", { name: "By account type" });
    const section = heading.closest("section");
    expect(section).not.toBeNull();
    expect(within(section!).getByText("Brokerage")).toBeInTheDocument();
    expect(within(section!).getByText("NZD")).toBeInTheDocument();
    expect(within(section!).getByText("6000")).toBeInTheDocument();
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

  it("shows funds under management and external assets per currency below the totals", async () => {
    renderDashboard(withCustodyModes);

    const managedHeading = await screen.findByRole("heading", { name: "Funds under management" });
    const managed = managedHeading.closest("section")!;
    const managedNzd = within(managed).getByRole("row", { name: /NZD/ });
    expect(within(managedNzd).getByText("6000")).toBeInTheDocument();
    expect(within(managed).queryByText("USD")).not.toBeInTheDocument();

    const externalHeading = screen.getByRole("heading", { name: "External assets" });
    const external = externalHeading.closest("section")!;
    expect(within(within(external).getByRole("row", { name: /NZD/ })).getByText("2500")).toBeInTheDocument();
    expect(within(within(external).getByRole("row", { name: /USD/ })).getByText("800")).toBeInTheDocument();

    const totals = screen.getAllByRole("table")[0];
    const byCustomer = screen.getByRole("heading", { name: "By customer" });
    expect(totals.compareDocumentPosition(managedHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(managedHeading.compareDocumentPosition(externalHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(externalHeading.compareDocumentPosition(byCustomer) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.queryByText(/has not checked/)).not.toBeInTheDocument();
  });

  it("shows None. under a custody mode that has no entry", async () => {
    renderDashboard({
      ...netWorth,
      breakdowns: {
        ...netWorth.breakdowns,
        byCustodyMode: [{ custodyMode: "External", currencies: [currency("NZD", 2500, 0)] }],
      },
    });

    const managed = (await screen.findByRole("heading", { name: "Funds under management" })).closest("section")!;
    expect(within(managed).getByText("None.")).toBeInTheDocument();
    expect(within(managed).queryByRole("row")).not.toBeInTheDocument();
    const external = screen.getByRole("heading", { name: "External assets" }).closest("section")!;
    expect(within(external).getByText("2500")).toBeInTheDocument();
  });

  it("shows no balances when the read has no currencies", async () => {
    renderDashboard({
      ...netWorth,
      currencies: [],
      breakdowns: { byCustomer: [], byAccountType: [], byCustodyMode: [], topHoldings: [] },
    });

    expect(await screen.findByText("No balances.")).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: /NZD|USD/ })).not.toBeInTheDocument();
    expect(screen.queryByText("Hidden Customer")).not.toBeInTheDocument();
  });
});

describe("DashboardPage valuation history", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-03-13T10:00:00Z"));
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/dashboard",
      search: "",
      href: "http://localhost:3000/dashboard",
      assign: vi.fn(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function seriesUrls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
    return requestedUrls(fetchMock).filter((url) => url.includes("/net-worth/funds-under-management"));
  }

  it("asks for the last 90 days up to the last final day, and 30 days or 1 year when chosen (D107)", async () => {
    const { fetchMock } = renderDashboard(netWorth, { series });

    expect(await screen.findByRole("heading", { name: "Funds under management over time" })).toBeInTheDocument();
    expect(seriesUrls(fetchMock)).toEqual(["http://localhost:3000/api/net-worth/funds-under-management?from=2025-12-12"]);
    expect(screen.getByRole("button", { name: "90 days" })).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(screen.getByRole("button", { name: "30 days" }));
    await waitFor(() =>
      expect(seriesUrls(fetchMock)).toContain("http://localhost:3000/api/net-worth/funds-under-management?from=2026-02-10"),
    );
    fireEvent.click(screen.getByRole("button", { name: "1 year" }));
    await waitFor(() =>
      expect(seriesUrls(fetchMock)).toContain("http://localhost:3000/api/net-worth/funds-under-management?from=2025-03-12"),
    );
    expect(seriesUrls(fetchMock).every((url) => !url.includes("to="))).toBe(true);
  });

  it("draws the daily total from the first day with an account, with a hollow marker and its reason on an incomplete day (D107)", async () => {
    renderDashboard(netWorth, { series });

    const chart = (await screen.findByRole("heading", { name: "Funds under management over time" })).closest("section")!;
    expect(
      await within(chart).findByText("In NZD, at each day's exchange rate. Up to 2026-03-11; a day is added once its prices are final."),
    ).toBeInTheDocument();
    const points = within(chart).getAllByRole("button", { name: /^2026-03-/ });
    expect(points.map((point) => point.getAttribute("aria-label"))).toEqual([
      "2026-03-09: 56,067.91 NZD",
      "2026-03-10: 55,890.52 NZD",
      "2026-03-11: 34,750.72 NZD. Incomplete: 1 accounts not fully valued, 1 accounts not valued yet, USD not converted (no exchange rate)",
    ]);
    expect(points.map((point) => point.getAttribute("data-incomplete"))).toEqual(["false", "false", "true"]);
    expect(points[2]).toHaveAttribute("fill", "none");

    fireEvent.focus(points[2]);
    expect(within(chart).getByRole("status")).toHaveTextContent(
      "2026-03-11: 34,750.72 NZD. Incomplete: 1 accounts not fully valued, 1 accounts not valued yet, USD not converted (no exchange rate)",
    );

    fireEvent.click(within(chart).getByText("Show figures"));
    const figures = within(chart).getByRole("table", { name: "Funds under management over time" });
    const rows = within(figures).getAllByRole("row").slice(1).map((row) => row.textContent);
    expect(rows).toEqual([
      "2026-03-0956,067.91 NZD",
      "2026-03-1055,890.52 NZD",
      "2026-03-1134,750.72 NZDIncomplete: 1 accounts not fully valued, 1 accounts not valued yet, USD not converted (no exchange rate)",
    ]);
  });

  it("says so when the range has no funds under management (D107)", async () => {
    renderDashboard(netWorth, { series: { ...series, days: series.days.map((day) => ({ ...day, currencies: [], total: 0, accounts: 0, complete: true, incompleteAccounts: 0, missingAccounts: 0 })) } });

    expect(await screen.findByText("No funds under management in this period.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^2026-03-/ })).not.toBeInTheDocument();
  });

  it("names an old exchange rate on an incomplete day (D107)", async () => {
    const stale = {
      ...series,
      days: [
        {
          date: "2026-03-11",
          currencies: [{ currency: "AUD", total: 1000, status: "StaleRate", fx: { rate: 1.1, date: "2026-03-03", source: "Frankfurter" }, value: null }],
          total: 0,
          complete: false,
          accounts: 1,
          incompleteAccounts: 0,
          missingAccounts: 0,
        },
      ],
    };
    renderDashboard(netWorth, { series: stale });

    const point = await screen.findByRole("button", { name: /^2026-03-11/ });
    expect(point).toHaveAttribute("aria-label", "2026-03-11: 0.00 NZD. Incomplete: AUD not converted (exchange rate too old)");
  });

  it("shows the total in the reporting currency under the per-currency totals (D108)", async () => {
    renderDashboard({ ...netWorth, folded: folded(true) }, { series });

    expect(await screen.findByText("Total in NZD: 9,885.60 at today's exchange rates")).toBeInTheDocument();
    expect(screen.queryByText(/Not counted/)).not.toBeInTheDocument();
    const totals = screen.getAllByRole("table")[0];
    const line = screen.getByText(/Total in NZD/);
    const managed = screen.getByRole("heading", { name: "Funds under management" });
    expect(totals.compareDocumentPosition(line) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(line.compareDocumentPosition(managed) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("names each currency the total leaves out (D108)", async () => {
    renderDashboard({ ...netWorth, folded: folded(false) }, { series });

    expect(await screen.findByText("Total in NZD: 8,500.00 at today's exchange rates")).toBeInTheDocument();
    expect(screen.getByText("Not counted: GBP (no exchange rate), USD (exchange rate too old)")).toBeInTheDocument();
  });

  it("hides the total when the only currency is the reporting currency (D108)", async () => {
    renderDashboard(
      {
        ...netWorth,
        currencies: [netWorth.currencies[0]],
        folded: {
          currency: "NZD",
          total: 6000,
          complete: true,
          currencies: [{ currency: "NZD", total: 6000, status: "Folded", fx: null, value: 6000 }],
        },
      },
      { series },
    );

    expect(await screen.findByRole("row", { name: /NZD/ })).toBeInTheDocument();
    expect(screen.queryByText(/Total in NZD/)).not.toBeInTheDocument();
  });
});

function folded(complete: boolean) {
  return complete
    ? {
        currency: "NZD",
        total: 9885.6,
        complete: true,
        currencies: [
          { currency: "NZD", total: 8500, status: "Folded", fx: null, value: 8500 },
          { currency: "USD", total: 800, status: "Folded", fx: { rate: 1.732, date: "2026-03-13", source: "Frankfurter" }, value: 1385.6 },
        ],
      }
    : {
        currency: "NZD",
        total: 8500,
        complete: false,
        currencies: [
          { currency: "GBP", total: 50, status: "NoRate", fx: null, value: null },
          { currency: "NZD", total: 8500, status: "Folded", fx: null, value: 8500 },
          { currency: "USD", total: 800, status: "StaleRate", fx: { rate: 1.7, date: "2026-03-01", source: "Frankfurter" }, value: null },
        ],
      };
}
