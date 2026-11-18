import { configureStore } from "@reduxjs/toolkit";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { createMemoryRouter, RouterProvider } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { appRoutes } from "@/app/router";
import { sessionSlice } from "@/features/session/sessionSlice";
import { api } from "@/shared/api/api";
import { chooseOption, openSelect, pickOption } from "@/test/chooseOption";

const me = {
  id: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  name: "Cee Customer",
  email: "cee@firm.example",
  role: "customer",
  status: "Active",
  tenantId: "tttttttt-tttt-tttt-tttt-tttttttttttt",
  tenantCode: "firm",
  adviserId: null,
  rowVersion: "AAAA",
};

const netWorth = {
  asOf: "2026-11-02T00:00:00.000Z",
  currencies: [
    {
      currency: "NZD",
      assets: { cash: 100, holdings: 40, total: 140 },
      liabilities: { total: 0 },
      net: 140,
    },
  ],
  breakdowns: {
    byCustomer: [{ customerId: me.id, name: "Someone else", currencies: [] }],
    byCustodyMode: [],
    byAccountType: [
      {
        type: "Bank",
        currencies: [
          {
            currency: "NZD",
            assets: { cash: 100, holdings: 0, total: 100 },
            liabilities: { total: 0 },
            net: 100,
          },
        ],
      },
    ],
    topHoldings: [
      {
        instrumentId: "iiiiiiii-iiii-iiii-iiii-iiiiiiiiiiii",
        symbol: "DEMO",
        quantity: 2,
        marketValue: 40,
        currency: "NZD",
      },
    ],
  },
};

const reportId = "8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11";

function callOf(input: RequestInfo | URL, init?: RequestInit) {
  const request = input instanceof Request ? input : null;
  const url = request?.url ?? String(input);
  const method = (init?.method ?? request?.method ?? "GET").toUpperCase();
  const credentials = init?.credentials ?? request?.credentials;
  const headers = new Headers(init?.headers ?? request?.headers);
  return { url, method, credentials, headers };
}

type AccountItem = { id: string; name: string; type: string; custodyMode: string; currency: string; cashBalance: number; status: string };

function renderAt(
  path: string,
  person = me,
  activity: unknown[] = [],
  custodyMode = "External",
  options: { netWorth?: unknown; accounts?: AccountItem[] } = {},
) {
  const accounts = options.accounts ?? [
    { id: "account-1", name: "Everyday", type: "Bank", custodyMode, currency: "NZD", cashBalance: 100, status: "Open" },
  ];
  const fetchMock = vi.mocked(fetch);
  fetchMock.mockImplementation(async (input, init) => {
    const { url, method } = callOf(input, init);
    if (url.endsWith("/users/me") && method === "PUT") {
      return new Response(null, { status: 204 });
    }
    if (url.endsWith("/users/me/password") && method === "PUT") {
      return new Response(null, { status: 204 });
    }
    if (url.endsWith("/users/me")) {
      return json(person);
    }
    if (url.includes(`/users/customers/${person.id}/net-worth`)) {
      return json(options.netWorth ?? netWorth);
    }
    if (url.includes("/accounts/") && url.endsWith("/cash-balance")) {
      return json({ accountId: "account-1", currency: "NZD", cashBalance: 100 });
    }
    if (url.includes("/accounts/") && url.endsWith("/holdings")) {
      return json({ items: [] });
    }
    if (url.includes("/transactions?accountId=")) {
      return json({ items: activity, page: 1, pageSize: 20, totalCount: activity.length });
    }
    if (url.endsWith("/accounts/account-1")) {
      return json({ id: "account-1", name: "Everyday", type: "Bank", custodyMode, currency: "NZD", status: "Open" });
    }
    if (url.includes("/accounts/missing")) {
      return new Response(JSON.stringify({ detail: "missing" }), { status: 404 });
    }
    if (url.includes("/accounts?")) {
      const mode = new URL(url, "http://localhost").searchParams.get("custodyMode");
      const items = mode ? accounts.filter((account) => account.custodyMode === mode) : accounts;
      return json({ items, page: 1, pageSize: 100, totalCount: items.length });
    }
    if (url.includes("/reports?scope=Customer")) {
      return json([
        { id: reportId, code: "customer-transactions", name: "Customer Transactions" },
        { id: "unknown-report", code: "mystery", name: "Mystery" },
      ]);
    }
    if (url.endsWith("/reports/request") && method === "POST") {
      return json({ id: "execution-1" }, 202);
    }
    if (url.includes("/reports/executions?")) {
      return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    }
    return new Response("", { status: 404 });
  });

  const store = configureStore({
    reducer: {
      session: sessionSlice.reducer,
      [api.reducerPath]: api.reducer,
    },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return { fetchMock, router };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function urls(fetchMock: ReturnType<typeof vi.mocked<typeof fetch>>) {
  return fetchMock.mock.calls.map(([input]) => (
    typeof input === "string" ? input : input instanceof Request ? input.url : String(input)
  ));
}

describe("Customer Portal", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", {
      origin: "http://localhost:3000",
      pathname: "/",
      search: "",
      href: "http://localhost:3000/",
      assign: vi.fn(),
    });
  });

  it("lands a customer on the dashboard and reads their own net worth", async () => {
    const { fetchMock } = renderAt("/");

    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Accounts" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Reports" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Profile" })).toBeInTheDocument();
    expect(screen.getByText("Cee Customer")).toBeInTheDocument();
    expect(await screen.findByRole("cell", { name: "NZD" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Bank" })).toBeInTheDocument();
    expect(screen.getByText(/DEMO/)).toBeInTheDocument();
    expect(screen.queryByText("Someone else")).not.toBeInTheDocument();
    expect(urls(fetchMock).some((url) => url.includes("/tenants"))).toBe(false);
    expect(urls(fetchMock).some((url) => url.includes(`/users/customers/${me.id}/net-worth`))).toBe(true);
    const meCall = fetchMock.mock.calls
      .map(([input, init]) => callOf(input, init))
      .find((call) => call.url.includes("/users/me") && call.method === "GET");
    expect(meCall?.credentials).toBe("include");
    expect(meCall?.headers.get("Authorization")).toBeNull();
  });

  it("sends a non-customer to forbidden only after the role is known", async () => {
    renderAt("/dashboard", { ...me, role: "tenantAdmin", name: "Tess" });
    expect(screen.queryByRole("heading", { name: "Forbidden" })).not.toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Forbidden" })).toBeInTheDocument();
  });

  it("shows the custody label next to the type on the accounts list", async () => {
    renderAt("/accounts");

    expect(await screen.findByRole("link", { name: "Everyday" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Custody" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Held elsewhere" })).toBeInTheDocument();
    expect(screen.queryByText("External")).not.toBeInTheDocument();
  });

  it.each([
    ["External", "Held elsewhere"],
    ["Custodial", "Held on platform"],
    ["Vault", "Vault"],
  ])("shows custody mode %s on the account page as %s", async (custodyMode, label) => {
    renderAt("/accounts/account-1", me, [], custodyMode);

    expect(await screen.findByRole("heading", { name: "Everyday" })).toBeInTheDocument();
    expect(screen.getByText("Custody")).toBeInTheDocument();
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  const note = "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures.";

  function currency(code: string, cash: number, holdings: number) {
    return {
      currency: code,
      assets: { cash, holdings, total: cash + holdings },
      liabilities: { total: 0 },
      net: cash + holdings,
    };
  }

  function withCustodyModes(byCustodyMode: unknown[]) {
    return { ...netWorth, breakdowns: { ...netWorth.breakdowns, byCustodyMode } };
  }

  it("shows held on platform and held elsewhere on the dashboard with the note under held elsewhere", async () => {
    renderAt("/dashboard", me, [], "External", {
      netWorth: withCustodyModes([
        { custodyMode: "Custodial", currencies: [currency("NZD", 1000, 5000)] },
        { custodyMode: "External", currencies: [currency("NZD", 2500, 0), currency("USD", 0, 800)] },
      ]),
    });

    const heading = await screen.findByRole("heading", { name: "Held on platform and elsewhere" });
    const section = heading.closest("section")!;
    const rows = within(section).getAllByRole("row").slice(1);
    expect(rows.map((row) => within(row).getAllByRole("cell")[0].firstChild?.textContent)).toEqual([
      "Held on platform",
      "Held elsewhere",
    ]);
    expect(within(rows[0]).getByText("NZD 6000")).toBeInTheDocument();
    expect(within(rows[0]).queryByText(note)).not.toBeInTheDocument();
    expect(within(rows[1]).getByText("NZD 2500, USD 800")).toBeInTheDocument();
    expect(within(rows[1]).getByText(note)).toBeInTheDocument();

    const byType = screen.getByRole("heading", { name: "By account type" });
    expect(heading.compareDocumentPosition(byType) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("shows only the custody modes the customer has on the dashboard", async () => {
    renderAt("/dashboard", me, [], "External", {
      netWorth: withCustodyModes([{ custodyMode: "Custodial", currencies: [currency("NZD", 70, 0)] }]),
    });

    const section = (await screen.findByRole("heading", { name: "Held on platform and elsewhere" })).closest("section")!;
    expect(within(section).getByText("Held on platform")).toBeInTheDocument();
    expect(within(section).queryByText("Held elsewhere")).not.toBeInTheDocument();
    expect(screen.queryByText(note)).not.toBeInTheDocument();
  });

  it("shows no balances in the custody section when there is no entry", async () => {
    renderAt("/dashboard", me, [], "External", { netWorth: withCustodyModes([]) });

    const section = (await screen.findByRole("heading", { name: "Held on platform and elsewhere" })).closest("section")!;
    expect(within(section).getByText("No balances.")).toBeInTheDocument();
    expect(within(section).queryByRole("row")).not.toBeInTheDocument();
  });

  it("lists accounts in two groups from two filtered requests with the note under held elsewhere", async () => {
    const { fetchMock } = renderAt("/accounts", me, [], "External", {
      accounts: [
        { id: "account-1", name: "Everyday", type: "Bank", custodyMode: "External", currency: "NZD", cashBalance: 100, status: "Open" },
        { id: "account-2", name: "Managed", type: "Brokerage", custodyMode: "Custodial", currency: "USD", cashBalance: 70, status: "Open" },
      ],
    });

    const onPlatform = await screen.findByRole("heading", { name: "Held on platform" });
    const elsewhere = await screen.findByRole("heading", { name: "Held elsewhere" });
    expect(onPlatform.compareDocumentPosition(elsewhere) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const platformSection = onPlatform.closest("section")!;
    const elsewhereSection = elsewhere.closest("section")!;
    expect(within(platformSection).getByRole("link", { name: "Managed" })).toBeInTheDocument();
    expect(within(platformSection).queryByRole("link", { name: "Everyday" })).not.toBeInTheDocument();
    expect(within(platformSection).queryByText(note)).not.toBeInTheDocument();
    expect(within(elsewhereSection).getByRole("link", { name: "Everyday" })).toBeInTheDocument();
    expect(within(elsewhereSection).getByText(note)).toBeInTheDocument();

    const accountUrls = urls(fetchMock).filter((url) => url.includes("/accounts?"));
    expect(accountUrls.some((url) => url.includes("custodyMode=Custodial") && url.includes("pageSize=100"))).toBe(true);
    expect(accountUrls.some((url) => url.includes("custodyMode=External") && url.includes("pageSize=100"))).toBe(true);
    expect(accountUrls.every((url) => url.includes("custodyMode="))).toBe(true);
  });

  it("hides an empty account group and shows no accounts when both are empty", async () => {
    renderAt("/accounts");

    expect(await screen.findByRole("heading", { name: "Held elsewhere" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Held on platform" })).not.toBeInTheDocument();
    cleanup();

    renderAt("/accounts", me, [], "External", { accounts: [] });
    expect(await screen.findByText("No accounts.")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Held elsewhere" })).not.toBeInTheDocument();
  });

  it.each([
    ["External", true],
    ["Custodial", false],
  ])("shows the note on the account page of a %s account: %s", async (custodyMode, shown) => {
    renderAt("/accounts/account-1", me, [], custodyMode);

    expect(await screen.findByRole("heading", { name: "Everyday" })).toBeInTheDocument();
    if (shown) {
      expect(screen.getByText(note)).toBeInTheDocument();
    } else {
      expect(screen.queryByText(note)).not.toBeInTheDocument();
    }
  });

  it("shows not found for another account and no write controls", async () => {
    renderAt("/accounts/missing");
    expect(await screen.findByText("Not found")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /close/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /new account/i })).not.toBeInTheDocument();
  });

  it("shows an internal transfer as Transfer with the visible leg amount and no counterparty", async () => {
    renderAt("/accounts/account-1", me, [
      {
        id: "71111111-1111-1111-1111-111111111111",
        tenantId: "22222222-2222-2222-2222-222222222222",
        type: "InternalTransfer",
        bookedAt: "2026-11-09T20:00:00Z",
        memo: null,
        reference: null,
        originalTransactionId: null,
        reversedByTransactionId: null,
        rowVersion: "AAAA",
        cash: [{ accountId: "account-1", customerId: me.id, amount: 500, currency: "NZD" }],
        security: [],
      },
    ]);

    const row = await screen.findByRole("row", { name: /Transfer/ });
    expect(row).toHaveTextContent("Transfer");
    expect(row).not.toHaveTextContent("InternalTransfer");
    expect(row).toHaveTextContent("500 NZD");
    expect(row).toHaveTextContent("2026-11-10 09:00");
    expect(screen.getByRole("columnheader", { name: "Amount" })).toBeInTheDocument();
  });

  it("shows an FX conversion as Currency exchange with this account's leg and the rate line", async () => {
    renderAt("/accounts/account-1", me, [
      {
        id: "72222222-2222-2222-2222-222222222222",
        tenantId: "22222222-2222-2222-2222-222222222222",
        type: "FxConversion",
        bookedAt: "2026-11-09T20:00:00Z",
        memo: null,
        reference: null,
        originalTransactionId: null,
        reversedByTransactionId: null,
        rowVersion: "AAAA",
        cash: [
          { accountId: "account-1", customerId: me.id, amount: -845.74, currency: "NZD" },
          { accountId: "account-2", customerId: me.id, amount: 500, currency: "USD" },
        ],
        security: [],
        fx: { baseCurrency: "NZD", quoteCurrency: "USD", rate: 0.5912, rateAsOfDate: "2026-11-09" },
      },
    ]);

    const row = await screen.findByRole("row", { name: /Currency exchange/ });
    expect(row).not.toHaveTextContent("FxConversion");
    expect(row).toHaveTextContent("-845.74 NZD");
    expect(row).not.toHaveTextContent("500 USD");
    expect(row).toHaveTextContent("1 NZD = 0.5912 USD (as of 2026-11-09)");
  });

  it("generates a customer report with the caller's id and no schedule controls", async () => {
    const { fetchMock } = renderAt("/reports");
    await openSelect("Report");
    expect(screen.getByRole("option", { name: "Customer Transactions" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /schedule/i })).not.toBeInTheDocument();
    await pickOption("Mystery");
    expect(screen.getByText("parameters not available in this version")).toBeInTheDocument();
    await chooseOption("Report", "Customer Transactions");
    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-03-31" } });
    fireEvent.submit(screen.getByRole("button", { name: "Generate" }).closest("form")!);

    await waitFor(() => {
      expect(fetchMock.mock.calls.some(([input, init]) => callOf(input, init).url.endsWith("/reports/request"))).toBe(true);
    });
    const requestCall = fetchMock.mock.calls.find(([input, init]) => callOf(input, init).url.endsWith("/reports/request"));
    expect(requestCall).toBeTruthy();
    const request = callOf(requestCall![0], requestCall![1]);
    const raw = requestCall![0] instanceof Request
      ? await requestCall![0].clone().text()
      : String(requestCall![1]?.body);
    const body = JSON.parse(raw);
    expect(body.parameters.customerId).toBe(me.id);
    expect(request.headers.get("X-MyWealth-Request")).toBe("1");
    expect(request.headers.get("Authorization")).toBeNull();
    expect(request.credentials).toBe("include");
  });

  it("starts login again after a password change", async () => {
    renderAt("/profile");
    fireEvent.change(await screen.findByLabelText("Current password"), { target: { value: "Password1!" } });
    fireEvent.change(screen.getByLabelText("New password"), { target: { value: "Password2!" } });
    fireEvent.submit(screen.getByRole("button", { name: "Change password" }).closest("form")!);
    await waitFor(() => {
      expect(window.location.assign).toHaveBeenCalledWith(expect.stringContaining("/bff/login?returnUrl="));
    });
  });
});
